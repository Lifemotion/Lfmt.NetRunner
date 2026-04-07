using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

var builder = WebApplication.CreateBuilder(args);

// Load NetRunner config
var configPath = Environment.GetEnvironmentVariable("NETRUNNER_CONFIG")
    ?? (OperatingSystem.IsLinux()
        ? "/var/lib/netrunner/netrunner.conf"
        : Path.Combine(Directory.GetCurrentDirectory(), "netrunner.dev.conf"));

NetRunnerConfig runnerConfig;
if (File.Exists(configPath))
{
    var ini = IniParser.Parse(File.ReadAllText(configPath));
    runnerConfig = NetRunnerConfig.FromIni(ini);
}
else
{
    runnerConfig = new NetRunnerConfig();
}

// Configure listen URL
if (!builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls($"http://{runnerConfig.Listen}");

// Register services
builder.Services.AddSingleton(runnerConfig);
builder.Services.AddSingleton<SystemdService>();
builder.Services.AddSingleton<AppManager>();
builder.Services.AddSingleton<DeployService>();
builder.Services.AddSingleton<ForgejoService>();
builder.Services.AddSingleton<ServiceFileGenerator>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddTransient<HealthCheckService>();
builder.Services.AddHttpClient();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// --- Minimal API endpoints ---

app.MapPost("/api/apps/{name}/start", async (string name, SystemdService systemd) =>
{
    await systemd.Start(name);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/stop", async (string name, SystemdService systemd) =>
{
    await systemd.Stop(name);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/restart", async (string name, SystemdService systemd) =>
{
    await systemd.Restart(name);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/rollback", async (string name, DeployService deploy) =>
{
    var result = await deploy.Rollback(name);
    return result ? Results.Ok() : Results.StatusCode(500);
});

app.MapPost("/api/apps/{name}/delete", async (string name, AppManager appManager) =>
{
    await appManager.DeleteApp(name);
    return Results.Ok();
});

app.MapPost("/api/apps/{name}/deploy", async (string name, HttpRequest request, DeployService deploy) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null)
        return Results.BadRequest("No file uploaded");

    using var stream = file.OpenReadStream();
    var result = await deploy.DeployFromArchive(name, stream, file.FileName);
    return result ? Results.Ok() : Results.StatusCode(500);
});

app.MapGet("/api/apps/{name}/status", async (string name, SystemdService systemd) =>
{
    var status = await systemd.GetAppStatus(name);
    return Results.Ok(new { status = status.ToString() });
});

app.MapGet("/api/apps/{name}/logs", async (string name, SystemdService systemd) =>
{
    var logs = await systemd.GetJournalLogs(name);
    return Results.Ok(new { logs });
});

app.MapPost("/api/webhook/forgejo", async (HttpContext ctx, ForgejoService forgejo, DeployService deploy, AppManager appManager) =>
{
    // Read body
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    // Verify signature
    var signature = ctx.Request.Headers["X-Forgejo-Signature"].FirstOrDefault() ?? "";
    if (!forgejo.VerifySignature(body, signature))
        return Results.StatusCode(403);

    // Parse payload
    var payload = forgejo.ParsePayload(body);
    if (payload == null)
        return Results.BadRequest("Invalid payload");

    // Resolve app name from repo URL
    var appName = forgejo.ResolveAppName(payload.Repository.CloneUrl);
    if (appName == null)
        return Results.NotFound("No app mapped to this repository");

    // Check if app exists
    if (appManager.GetAppConfig(appName) == null)
        return Results.NotFound($"App '{appName}' not registered");

    // Check branch (default: main)
    var branch = payload.GetBranch();
    if (branch != "main" && branch != "master")
        return Results.Ok(new { message = $"Ignored push to branch '{branch}'" });

    // Clone URL from config (not payload!)
    var cloneUrl = forgejo.GetConfigCloneUrl(appName);
    if (cloneUrl == null)
        return Results.StatusCode(500);

    var commitId = payload.HeadCommit?.Id;

    // Deploy in background
    _ = Task.Run(async () =>
    {
        try
        {
            await deploy.DeployFromGit(appName, cloneUrl, branch, commitId);
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Webhook deploy failed for {App}", appName);
        }
    });

    return Results.Accepted(value: new { message = $"Deploy started for '{appName}'" });
});

app.Run();
