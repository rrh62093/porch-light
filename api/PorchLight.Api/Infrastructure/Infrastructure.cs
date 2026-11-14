using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;

namespace PorchLight.Api.Infrastructure;

/// <summary>Thrown from endpoints to return a clean JSON error with a status code.</summary>
public class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;

    public static ApiException NotFound(string what = "Not found") => new(404, what);
    public static ApiException Forbidden(string why = "You don't have access to that.") => new(403, why);
    public static ApiException Bad(string why) => new(400, why);
    public static ApiException Conflict(string why) => new(409, why);
}

public static class ErrorHandling
{
    public static void UseApiErrors(this WebApplication app)
    {
        app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
        {
            var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
            var (status, message) = ex switch
            {
                ApiException api => (api.Status, api.Message),
                BadHttpRequestException bad => (400, bad.Message),
                _ => (500, "Something went wrong on our end."),
            };
            if (status == 500)
                ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(ex, "Unhandled error");
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { error = message });
        }));
    }
}

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new ApiException(401, "Please sign in.");
}

public static class Text
{
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw ApiException.Bad($"{field} is required.");
        if (v.Length > max) throw ApiException.Bad($"{field} must be {max} characters or fewer.");
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw ApiException.Bad($"{field} must be {max} characters or fewer.");
        return v;
    }

    public static string Email(string? value)
    {
        var v = Required(value, "Email", 320).ToLowerInvariant();
        if (!v.Contains('@') || v.StartsWith('@') || v.EndsWith('@'))
            throw ApiException.Bad("That doesn't look like an email address.");
        return v;
    }
}
