using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class IndexModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly NetRunnerConfig _config;
    private readonly IWebHostEnvironment _env;

    public List<(AppConfig Config, AppState State)> Apps { get; set; } = [];
    public string Hostname { get; set; } = "";
    public string OsInfo { get; set; } = "";
    public string DotnetVersion { get; set; } = "";
    public string ListenUrl { get; set; } = "";
    public string Environment { get; set; } = "";
    public string AppsRoot { get; set; } = "";
    public int AppCount { get; set; }

    public IndexModel(AppManager appManager, NetRunnerConfig config, IWebHostEnvironment env)
    {
        _appManager = appManager;
        _config = config;
        _env = env;
    }

    public async Task OnGetAsync()
    {
        Apps = await _appManager.GetAllApps();

        Hostname = System.Net.Dns.GetHostName();
        OsInfo = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        DotnetVersion = RuntimeInformation.FrameworkDescription;
        ListenUrl = _config.Listen;
        Environment = _env.EnvironmentName;
        AppsRoot = _config.AppsRoot;
        AppCount = Apps.Count;
    }
}
