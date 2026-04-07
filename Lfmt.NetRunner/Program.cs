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
builder.Services.AddControllers(options =>
{
    options.Filters.Add<Lfmt.NetRunner.Filters.ApiExceptionFilter>();
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

app.Run();
