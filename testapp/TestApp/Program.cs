var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TestApp is running. Status: Healthy");

app.MapGet("/health", () => "Healthy");

app.Run();
