using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public record PushKeys(string P256dh, string Auth);
public record SubscribeBody(string Endpoint, PushKeys Keys);
public record UnsubscribeBody(string Endpoint);

public static class PushEndpoints
{
    public static void MapPush(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/push").RequireAuthorization();

        g.MapPost("/subscribe", async (SubscribeBody body, ClaimsPrincipal p, AppDb db) =>
        {
            var me = p.UserId();
            if (!Uri.TryCreate(body.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw ApiException.Bad("Invalid push endpoint.");
            var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == body.Endpoint);
            if (sub is null) db.PushSubscriptions.Add(sub = new PushSubscription { Endpoint = body.Endpoint });
            sub.UserId = me; // A shared device follows whoever signed in last.
            sub.P256dh = Text.Required(body.Keys?.P256dh, "p256dh", 200);
            sub.Auth = Text.Required(body.Keys?.Auth, "auth", 100);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/unsubscribe", async (UnsubscribeBody body, ClaimsPrincipal p, AppDb db) =>
        {
            await db.PushSubscriptions.Where(s => s.Endpoint == body.Endpoint && s.UserId == p.UserId()).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        g.MapPost("/test", async (ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            if (!await db.PushSubscriptions.AnyAsync(s => s.UserId == me))
                throw ApiException.Bad("This device isn't subscribed yet.");
            await notifier.Notify([me], NotifyCategory.DealUpdate,
                new("Porch Light is on", "Notifications are working on this device.", "/settings"));
            return Results.NoContent();
        });
    }
}
