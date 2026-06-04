var builder = WebApplication.CreateBuilder(args);

// Add YARP reverse proxy services
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add CORS to allow the frontend Vue application to connect
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors("AllowAllPolicy");

// Map health check or root endpoint
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ApiGateway" }));
app.MapGet("/", () => Results.Redirect("/health"));

// Map YARP reverse proxy routes
app.MapReverseProxy();

app.Run();
