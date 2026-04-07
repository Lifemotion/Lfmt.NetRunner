using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class DetailModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly SystemdService _systemd;

    public AppConfig? Config { get; set; }
    public AppState? State { get; set; }
    public List<DeploymentLogEntry> DeployLog { get; set; } = [];
    public string JournalLogs { get; set; } = "";

    public DetailModel(AppManager appManager, SystemdService systemd)
    {
        _appManager = appManager;
        _systemd = systemd;
    }

    public async Task<IActionResult> OnGetAsync(string name)
    {
        Config = _appManager.GetAppConfig(name);
        if (Config == null) return NotFound();

        State = await _appManager.GetAppState(name);
        DeployLog = await _appManager.GetDeployLog(name);

        try
        {
            JournalLogs = await _systemd.GetJournalLogs(name, 50);
        }
        catch
        {
            JournalLogs = "(unable to read logs)";
        }

        return Page();
    }
}
