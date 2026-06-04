using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InCleanHome.Shared.Infrastructure.Authentication;

public static class JwtValidator
{
    public static async Task<ClaimsPrincipal?> ValidateTokenAsync(string token, string secret)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var handler = new JsonWebTokenHandler();
        var key = Encoding.ASCII.GetBytes(secret);
        try
        {
            var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            });

            if (!result.IsValid) return null;

            return new ClaimsPrincipal(new ClaimsIdentity(result.ClaimsIdentity.Claims, "Bearer"));
        }
        catch
        {
            return null;
        }
    }
}
