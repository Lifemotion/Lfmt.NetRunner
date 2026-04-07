namespace Lfmt.NetRunner.Services;

public class HealthCheckService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(IHttpClientFactory httpClientFactory, ILogger<HealthCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> CheckHealth(int port, string path, string phrase, int timeoutSeconds, int intervalSeconds)
    {
        var attempts = (int)Math.Ceiling((double)timeoutSeconds / intervalSeconds);
        var url = $"http://localhost:{port}{path}";

        _logger.LogInformation("Health check: {Url}, phrase '{Phrase}', {Attempts} attempts", url, phrase, attempts);

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));

            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (body.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Health check passed on attempt {Attempt}", i + 1);
                        return true;
                    }
                    _logger.LogWarning("Health check: status 200 but phrase not found (attempt {Attempt})", i + 1);
                }
                else
                {
                    _logger.LogWarning("Health check: status {Status} (attempt {Attempt})", (int)response.StatusCode, i + 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Health check: {Error} (attempt {Attempt})", ex.Message, i + 1);
            }
        }

        _logger.LogError("Health check failed after {Timeout}s", timeoutSeconds);
        return false;
    }
}
