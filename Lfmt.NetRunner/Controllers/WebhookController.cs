using Microsoft.AspNetCore.Mvc;
using Lfmt.NetRunner.Services;

namespace Lfmt.NetRunner.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly ForgejoService _forgejo;
    private readonly DeployService _deploy;
    private readonly AppManager _appManager;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(ForgejoService forgejo, DeployService deploy, AppManager appManager, ILogger<WebhookController> logger)
    {
        _forgejo = forgejo;
        _deploy = deploy;
        _appManager = appManager;
        _logger = logger;
    }

    [HttpPost("forgejo")]
    public async Task<IActionResult> Forgejo()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        var signature = Request.Headers["X-Forgejo-Signature"].FirstOrDefault() ?? "";
        if (!_forgejo.VerifySignature(body, signature))
            return StatusCode(403, new { error = "Invalid signature" });

        var payload = _forgejo.ParsePayload(body);
        if (payload == null)
            return BadRequest(new { error = "Invalid payload" });

        var appName = _forgejo.ResolveAppName(payload.Repository.CloneUrl);
        if (appName == null)
            return NotFound(new { error = "No app mapped to this repository" });

        if (_appManager.GetAppConfig(appName) == null)
            return NotFound(new { error = $"App '{appName}' not registered" });

        var branch = payload.GetBranch();
        if (branch != "main" && branch != "master")
            return Ok(new { message = $"Ignored push to branch '{branch}'" });

        var cloneUrl = _forgejo.GetConfigCloneUrl(appName);
        if (cloneUrl == null)
            return StatusCode(500, new { error = "Clone URL not found in config" });

        var commitId = payload.HeadCommit?.Id;

        _ = Task.Run(async () =>
        {
            try
            {
                await _deploy.DeployFromGit(appName, cloneUrl, branch, commitId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook deploy failed for {App}", appName);
            }
        });

        return Accepted(new { message = $"Deploy started for '{appName}'" });
    }
}
