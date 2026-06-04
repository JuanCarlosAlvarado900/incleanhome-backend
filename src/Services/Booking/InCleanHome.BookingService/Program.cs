using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MassTransit;
using InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using InCleanHome.API.Shared.Domain.Repositories;
using InCleanHome.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using InCleanHome.API.Booking.Domain.Repositories;
using InCleanHome.API.Booking.Domain.Services;
using InCleanHome.API.Booking.Application.Internal.CommandServices;
using InCleanHome.API.Booking.Application.Internal.QueryServices;
using InCleanHome.API.Booking.Infrastructure.Persistence.EFC.Repositories;
using InCleanHome.API.Booking.Infrastructure.Pipeline.Middleware.Extensions;
using InCleanHome.Shared.Infrastructure.Messaging;
using InCleanHome.BookingService.Consumers;
using InCleanHome.API.IAM.Interfaces.ACL;
using InCleanHome.API.Profiles.Interfaces.ACL;
using InCleanHome.API.Notifications.Interfaces.ACL;
using InCleanHome.API.Profiles.Domain.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers & Routing
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddControllers();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllPolicy",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

// DbContext configuration (PostgreSQL)
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Port=5432;Database=incleanhome_booking;Username=postgres;Password=root";

if (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://"))
{
    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':');
    var port = uri.Port > 0 ? uri.Port : 5432;

    connectionString = $"Host={uri.Host};Port={port};Database={uri.LocalPath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;";
}

Console.WriteLine($"[BookingService] Database connection host: {new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Host}");

builder.Services.AddDbContext<BookingDbContext>(options =>
{
    options.UseNpgsql(connectionString)
           .LogTo(Console.WriteLine, LogLevel.Information);
});

// DI registration
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<BookingDbContext>()));
builder.Services.AddScoped<DbContext, BookingDbContext>(sp => sp.GetRequiredService<BookingDbContext>());
builder.Services.AddScoped<IBookingRequestRepository, BookingRequestRepository>();
builder.Services.AddScoped<IBookingRequestCommandService, BookingRequestCommandService>();
builder.Services.AddScoped<IBookingRequestQueryService, BookingRequestQueryService>();

// Mock Services for other contexts (to allow autonomous runtime testing)
builder.Services.AddScoped<IIamContextFacade, IamContextFacadeMock>();
builder.Services.AddScoped<IProfilesContextFacade, ProfilesContextFacadeMock>();
builder.Services.AddScoped<INotificationsContextFacade, NotificationsContextFacadeMock>();
builder.Services.AddScoped<IWorkerProfileQueryService, WorkerProfileQueryServiceMock>();
builder.Services.AddScoped<IWorkerProfileCommandService, WorkerProfileCommandServiceMock>();

// MassTransit & RabbitMQ
var rabbitMqUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL") 
                  ?? builder.Configuration["RabbitMQ:Url"] 
                  ?? "amqp://guest:guest@localhost:5672";

builder.Services.AddSharedMassTransit(rabbitMqUrl, x =>
{
    x.AddConsumer<PaymentProcessedConsumer>();
});

// Swagger Gen
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "InCleanHome.BookingService API",
        Version = "v1",
        Description = "InCleanHome Booking Microservice"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT (without Bearer prefix)",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme }
            },
            Array.Empty<string>()
        }
    });
});

foreach (var descriptor in builder.Services)
{
    if (descriptor.ServiceType.FullName?.Contains("MassTransit") == true)
    {
        Console.WriteLine($"Service: {descriptor.ServiceType.FullName} -> {descriptor.ImplementationType?.FullName ?? descriptor.ImplementationFactory?.Method.ToString() ?? "instance"} ({descriptor.Lifetime})");
    }
}

var app = builder.Build();

// Database auto-creation
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    context.Database.EnsureCreated();
    Console.WriteLine("[BookingService] Database structure checked/created successfully.");
}

// HTTP request pipeline
if (app.Environment.IsDevelopment() || true) // enable Swagger in production for easy student evaluation
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BookingService V1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root "/"
    });
}

app.UseCors("AllowAllPolicy");
app.UseRequestAuthorization();
app.MapControllers();

app.Run();
