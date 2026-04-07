using Microsoft.AspNetCore.Mvc;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Controllers;

[ApiController]
[Route("api/apps/{name}")]
public class AppsController : ControllerBase
{
    private readonly SystemdService _systemd;
    private readonly AppManager _appManager;
    private readonly DeployService _deploy;
    private readonly SettingsService _settings;

    public AppsController(SystemdService systemd, AppManager appManager, DeployService deploy, SettingsService settings)
    {
        _systemd = systemd;
        _appManager = appManager;
        _deploy = deploy;
        _settings = settings;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(string name)
    {
        var status = await _systemd.GetAppStatus(name);
        return Ok(new { status = status.ToString() });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(string name)
    {
        var logs = await _systemd.GetJournalLogs(name, _settings.Current.JournalLines);
        return Ok(new { logs });
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(string name)
    {
        await _systemd.Start(name);
        return Ok(new { ok = true });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(string name)
    {
        await _systemd.Stop(name);
        return Ok(new { ok = true });
    }

    [HttpPost("restart")]
    public async Task<IActionResult> Restart(string name)
    {
        await _systemd.Restart(name);
        return Ok(new { ok = true });
    }

    [HttpPost("rollback")]
    public async Task<IActionResult> Rollback(string name)
    {
        var ok = await _deploy.Rollback(name);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete(string name)
    {
        await _appManager.DeleteApp(name);
        return Ok(new { ok = true });
    }

    [HttpPost("deploy")]
    public async Task<IActionResult> Deploy(string name, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        using var stream = file.OpenReadStream();
        var ok = await _deploy.DeployFromArchive(name, stream, file.FileName);
        return ok ? Ok(new { ok }) : StatusCode(500, new { ok });
    }
}
