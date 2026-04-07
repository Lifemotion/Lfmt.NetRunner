var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () =>
{
    var lines = new List<string>
    {
        "TestApp is running. Status: Healthy",
        "",
        $"Working Directory: {Environment.CurrentDirectory}",
        $"Process Path: {Environment.ProcessPath}",
        $"Machine: {Environment.MachineName}",
        $"User: {Environment.UserName}",
        $"OS: {Environment.OSVersion}",
        $".NET: {Environment.Version}",
        "",
        "--- Environment Variables ---"
    };

    foreach (var kv in Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .OrderBy(e => e.Key.ToString()))
    {
        lines.Add($"{kv.Key}={kv.Value}");
    }

    return string.Join("\n", lines);
});

app.MapGet("/health", () => "Healthy");

app.Run();
