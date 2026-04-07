using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class IndexModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly NetRunnerConfig _config;
    private readonly SettingsService _settings;
    private readonly IWebHostEnvironment _env;

    public List<(AppConfig Config, AppState State)> Apps { get; set; } = [];
    public UiSettings Settings { get; set; } = new();
    public string Hostname { get; set; } = "";
    public string OsInfo { get; set; } = "";
    public string DotnetVersion { get; set; } = "";
    public string HostIp { get; set; } = "localhost";
    public string AppVersion { get; set; } = "";
    public string Environment { get; set; } = "";

    public IndexModel(AppManager appManager, NetRunnerConfig config, SettingsService settings, IWebHostEnvironment env)
    {
        _appManager = appManager;
        _config = config;
        _settings = settings;
        _env = env;
    }

    public async Task OnGetAsync()
    {
        Apps = await _appManager.GetAllApps();
        Settings = _settings.Current;

        Hostname = System.Net.Dns.GetHostName();
        OsInfo = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        DotnetVersion = RuntimeInformation.FrameworkDescription;
        HostIp = _config.HostIp;
        AppVersion = typeof(IndexModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
        Environment = _env.EnvironmentName;
    }
}
