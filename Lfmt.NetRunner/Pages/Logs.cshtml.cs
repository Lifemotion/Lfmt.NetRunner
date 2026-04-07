using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class LogsModel : PageModel
{
    private readonly AppManager _appManager;

    public List<DeploymentLogEntry> Entries { get; set; } = [];

    public LogsModel(AppManager appManager)
    {
        _appManager = appManager;
    }

    public async Task OnGetAsync()
    {
        Entries = await _appManager.GetGlobalDeployLog();
    }
}
