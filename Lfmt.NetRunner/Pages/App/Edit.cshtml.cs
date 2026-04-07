using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class EditModel : PageModel
{
    private readonly AppManager _appManager;

    public string Name { get; set; } = "";

    [BindProperty]
    public AppConfig Input { get; set; } = new();

    public string? Error { get; set; }

    public EditModel(AppManager appManager)
    {
        _appManager = appManager;
    }

    public IActionResult OnGet(string name)
    {
        Name = name;
        var config = _appManager.GetAppConfig(name);
        if (config == null) return NotFound();

        Input = config;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string name)
    {
        Name = name;
        try
        {
            await _appManager.UpdateApp(name, Input);
            return RedirectToPage("/App/Detail", new { name });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
