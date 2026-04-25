using Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:5200", "https://localhost:5200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapReverseProxy();

app.Run();
