var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () =>
{
    var html = $"""
        <!DOCTYPE html>
        <html>
        <head><title>TestApp</title></head>
        <body style="font-family:monospace; background:#111; color:#ccc; padding:2em">
            <img src="/logo.svg" alt="NetRunner logo" style="margin-bottom:1em" />
            <h2>TestApp is running</h2>
            <pre>
        ContentRoot: {builder.Environment.ContentRootPath}
        WebRoot:     {builder.Environment.WebRootPath}
        WorkDir:     {Environment.CurrentDirectory}
        BaseDir:     {AppContext.BaseDirectory}
        User:        {Environment.UserName}
        .NET:        {Environment.Version}
            </pre>
            <p><a href="/health" style="color:#0af">/health</a>
            | <a href="/logo.svg" style="color:#0af">/logo.svg</a>
            | <a href="/static-check" style="color:#0af">/static-check</a></p>
        </body>
        </html>
        """;
    return Results.Content(html, "text/html");
});

app.MapGet("/health", () => "Healthy");

app.MapGet("/static-check", () =>
{
    var webRoot = builder.Environment.WebRootPath ?? "";
    var logoPath = Path.Combine(webRoot, "logo.svg");
    var exists = File.Exists(logoPath);
    return Results.Ok(new
    {
        webRoot,
        logoPath,
        logoExists = exists,
        contentRoot = builder.Environment.ContentRootPath,
        baseDirectory = AppContext.BaseDirectory,
        status = exists ? "PASS" : "FAIL: static file not found"
    });
});

app.Run();
