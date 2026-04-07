using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class LogsModel : PageModel
{
    private readonly AppManager _appManager;
    private readonly SettingsService _settingsService;

    public List<DeploymentLogEntry> Entries { get; set; } = [];
    public UiSettings Settings { get; set; } = new();

    public LogsModel(AppManager appManager, SettingsService settingsService)
    {
        _appManager = appManager;
        _settingsService = settingsService;
    }

    public async Task OnGetAsync()
    {
        Entries = await _appManager.GetGlobalDeployLog();
        Settings = _settingsService.Current;
    }
}
