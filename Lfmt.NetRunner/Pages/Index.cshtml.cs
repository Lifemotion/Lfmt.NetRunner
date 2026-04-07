using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages;

public class IndexModel : PageModel
{
    private readonly AppManager _appManager;

    public List<(AppConfig Config, AppState State)> Apps { get; set; } = [];

    public IndexModel(AppManager appManager)
    {
        _appManager = appManager;
    }

    public async Task OnGetAsync()
    {
        Apps = await _appManager.GetAllApps();
    }
}
