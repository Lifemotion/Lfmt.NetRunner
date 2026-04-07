using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class DetailModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly SystemdService _systemd;
    private readonly NetRunnerConfig _config;
    private readonly SettingsService _settingsService;

    public AppConfig? Config { get; set; }
    public AppState? State { get; set; }
    public UiSettings Settings { get; set; } = new();
    public List<DeploymentLogEntry> DeployLog { get; set; } = [];
    public string JournalLogs { get; set; } = "";
    public string HostIp { get; set; } = "";

    public DetailModel(AppManager appManager, SystemdService systemd, NetRunnerConfig config, SettingsService settingsService)
    {
        _appManager = appManager;
        _systemd = systemd;
        _config = config;
        _settingsService = settingsService;
    }

    public async Task<IActionResult> OnGetAsync(string name)
    {
        Config = _appManager.GetAppConfig(name);
        if (Config == null) return NotFound();

        State = await _appManager.GetAppState(name);
        Settings = _settingsService.Current;
        HostIp = _config.HostIp;
        DeployLog = await _appManager.GetDeployLog(name);

        try
        {
            JournalLogs = await _systemd.GetJournalLogs(name, Settings.JournalLines);
        }
        catch
        {
            JournalLogs = "(unable to read logs)";
        }

        return Page();
    }
}
