using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

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

// Max upload size from settings (default 100 MB)
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 100 * 1024 * 1024);

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

// Cookie authentication (used when [auth] is configured)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<AuthMiddleware>();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

app.Run();
