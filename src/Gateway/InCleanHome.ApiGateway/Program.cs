var builder = WebApplication.CreateBuilder(args);

// Dynamically configure YARP destinations from environment variables (for Render deploy) or fall back to local defaults
var rawBookingUrl = Environment.GetEnvironmentVariable("BOOKING_SERVICE_URL") ?? "http://booking-service:8080";
var rawPaymentUrl = Environment.GetEnvironmentVariable("PAYMENT_SERVICE_URL") ?? "http://payment-service:8000";

var bookingUrl = System.Uri.TryCreate(rawBookingUrl, UriKind.Absolute, out var bookingUri) 
    ? bookingUri.GetLeftPart(System.UriPartial.Authority) 
    : rawBookingUrl.TrimEnd('/');

var paymentUrl = System.Uri.TryCreate(rawPaymentUrl, UriKind.Absolute, out var paymentUri) 
    ? paymentUri.GetLeftPart(System.UriPartial.Authority) 
    : rawPaymentUrl.TrimEnd('/');

builder.Configuration["ReverseProxy:Clusters:booking-cluster:Destinations:destination1:Address"] = bookingUrl;
builder.Configuration["ReverseProxy:Clusters:payment-cluster:Destinations:destination1:Address"] = paymentUrl;

Console.WriteLine($"[ApiGateway] Routing booking-cluster to: {bookingUrl}");
Console.WriteLine($"[ApiGateway] Routing payment-cluster to: {paymentUrl}");

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
