using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Lfmt.NetRunner.Models;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Pages.App;

public class CreateModel : PageModel
{
    private readonly AppManager _appManager;

    [BindProperty]
    public AppConfig Input { get; set; } = new();

    public string? Error { get; set; }

    public CreateModel(AppManager appManager)
    {
        _appManager = appManager;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await _appManager.CreateApp(Input);
            return RedirectToPage("/App/Detail", new { name = Input.Name });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
