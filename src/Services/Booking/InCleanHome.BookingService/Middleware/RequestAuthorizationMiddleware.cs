using System.Security.Claims;
using InCleanHome.API.IAM.Domain.Model.Aggregates;
using InCleanHome.Shared.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace InCleanHome.API.Booking.Infrastructure.Pipeline.Middleware.Components
{
    public class RequestAuthorizationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        private readonly string _secret = configuration["TokenSettings:Secret"] 
            ?? "InCleanHome_SuperSecretKey_AtLeast32CharactersLongAndVerySecure_2026";

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata.Any(m => m.GetType() == typeof(AllowAnonymousAttribute)) ?? false;

            if (allowAnonymous)
            {
                await next(context);
                return;
            }

            var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid token" });
                return;
            }

            var principal = await JwtValidator.ValidateTokenAsync(token, _secret);
            if (principal == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
                return;
            }

            // Extract claims and construct a mock user object to satisfy the Casts in Booking controllers
            var sidClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Sid);
            var roleClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role);
            var emailClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

            if (sidClaim == null || roleClaim == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Required claims missing from token" });
                return;
            }

            var user = new User
            {
                Id = int.Parse(sidClaim.Value),
                Role = roleClaim.Value,
                Email = emailClaim?.Value ?? "unknown@user.com",
                DocumentsVerified = true
            };

            context.Items["User"] = user;
            await next(context);
        }
    }
}

namespace InCleanHome.API.Booking.Infrastructure.Pipeline.Middleware.Extensions
{
    using InCleanHome.API.Booking.Infrastructure.Pipeline.Middleware.Components;

    public static class RequestAuthorizationMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestAuthorization(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestAuthorizationMiddleware>();
        }
    }
}
