using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public record ProfileItemBody(ProfileItemKind Kind, string Name, string? Description, bool AvailableForTrade = true);
public record ChildProfileBody(bool OffersChildcare, bool NeedsChildcare, string? KidsAges, string? Allergies,
    string? ContactNumber, string? Notes);

public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/people").RequireAuthorization();

        // Everyone I share a circle with, for picking a dad to send a direct request to.
        people.MapGet("", async (ClaimsPrincipal p, AppDb db) =>
        {
            var me = p.UserId();
            return await db.Users
                .Where(u => u.Id != me && db.CircleMembers.Any(m => m.UserId == u.Id &&
                    db.CircleMembers.Any(o => o.CircleId == m.CircleId && o.UserId == me)))
                .OrderBy(u => u.Name)
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
        });

        people.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            if (!await access.CanViewProfile(me, id)) throw ApiException.NotFound("Person not found.");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw ApiException.NotFound("Person not found.");

            var items = await db.ProfileItems.Where(i => i.UserId == id)
                .OrderBy(i => i.Kind).ThenByDescending(i => i.Endorsements.Count).ThenBy(i => i.Name)
                .Select(i => new
                {
                    i.Id, i.Kind, i.Name, i.Description, i.AvailableForTrade,
                    endorsementCount = i.Endorsements.Count,
                    endorsedByMe = i.Endorsements.Any(e => e.EndorsedById == me),
                    endorsers = i.Endorsements.OrderBy(e => e.CreatedAt).Select(e => e.EndorsedBy!.Name).Take(10).ToList(),
                })
                .ToListAsync();

            var cp = await db.ChildProfiles.FirstOrDefaultAsync(c => c.UserId == id);
            var canSeeKids = cp is not null && await access.CanSeeChildProfile(me, id);

            var sharedCircles = await db.CircleMembers
                .Where(m => m.UserId == id && db.CircleMembers.Any(o => o.CircleId == m.CircleId && o.UserId == me))
                .Select(m => new { m.Circle!.Id, m.Circle.Name }).ToListAsync();

            return new
            {
                user.Id,
                user.Name,
                // Email only to circle-mates, so they can recognize each other.
                email = sharedCircles.Count > 0 || id == me ? user.Email : null,
                isMe = id == me,
                items,
                offersChildcare = cp?.OffersChildcare ?? false,
                needsChildcare = cp?.NeedsChildcare ?? false,
                childProfile = canSeeKids ? ChildDto(cp!) : null,
                sharedCircles,
                mutedByMe = await db.UserMutes.AnyAsync(m => m.UserId == me && m.MutedUserId == id),
            };
        });

        people.MapPut("/{id:guid}/mute", async (Guid id, MuteBody body, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            if (id == me || !await access.CanViewProfile(me, id)) throw ApiException.NotFound("Person not found.");
            var mute = await db.UserMutes.FindAsync(me, id);
            if (body.Muted && mute is null) db.UserMutes.Add(new UserMute { UserId = me, MutedUserId = id });
            if (!body.Muted && mute is not null) db.UserMutes.Remove(mute);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // --- My skills, tools, and things I'm looking for ---

        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapPost("/items", async (ProfileItemBody body, ClaimsPrincipal p, AppDb db) =>
        {
            var item = new ProfileItem { UserId = p.UserId() };
            Apply(item, body);
            db.ProfileItems.Add(item);
            await db.SaveChangesAsync();
            return new { item.Id };
        });

        me.MapPut("/items/{id:guid}", async (Guid id, ProfileItemBody body, ClaimsPrincipal p, AppDb db) =>
        {
            var item = await db.ProfileItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == p.UserId())
                ?? throw ApiException.NotFound();
            Apply(item, body);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        me.MapDelete("/items/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db) =>
        {
            var item = await db.ProfileItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == p.UserId())
                ?? throw ApiException.NotFound();
            db.Endorsements.RemoveRange(db.Endorsements.Where(e => e.ProfileItemId == id));
            db.ProfileItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        me.MapGet("/child-profile", async (ClaimsPrincipal p, AppDb db) =>
        {
            var cp = await db.ChildProfiles.FirstOrDefaultAsync(c => c.UserId == p.UserId());
            return cp is null ? new ChildProfileBody(false, false, null, null, null, null) : (object)ChildDto(cp);
        });

        me.MapPut("/child-profile", async (ChildProfileBody body, ClaimsPrincipal p, AppDb db) =>
        {
            var id = p.UserId();
            var cp = await db.ChildProfiles.FirstOrDefaultAsync(c => c.UserId == id);
            if (cp is null) db.ChildProfiles.Add(cp = new ChildProfile { UserId = id });
            cp.OffersChildcare = body.OffersChildcare;
            cp.NeedsChildcare = body.NeedsChildcare;
            cp.KidsAges = Text.Optional(body.KidsAges, "Kids' ages", 200);
            cp.Allergies = Text.Optional(body.Allergies, "Allergies", 1000);
            cp.ContactNumber = Text.Optional(body.ContactNumber, "Contact number", 40);
            cp.Notes = Text.Optional(body.Notes, "Notes", 2000);
            cp.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return ChildDto(cp);
        });

        // --- Endorsements: positive only, any circle-mate, no deal required ---

        var items = app.MapGroup("/api/items").RequireAuthorization();

        items.MapPost("/{id:guid}/endorse", async (Guid id, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            var item = await db.ProfileItems.FirstOrDefaultAsync(i => i.Id == id) ?? throw ApiException.NotFound();
            if (item.UserId == me) throw ApiException.Bad("You can't endorse yourself.");
            if (item.Kind == ProfileItemKind.LookingFor) throw ApiException.Bad("Only skills and tools can be endorsed.");
            if (!await access.CanViewProfile(me, item.UserId)) throw ApiException.NotFound();
            if (!await db.Endorsements.AnyAsync(e => e.ProfileItemId == id && e.EndorsedById == me))
            {
                db.Endorsements.Add(new Endorsement { ProfileItemId = id, EndorsedById = me });
                await db.SaveChangesAsync();
                var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
                await notifier.Notify([item.UserId], NotifyCategory.DealUpdate,
                    new($"{myName} endorsed your {item.Name}", "Nice.", $"/people/{item.UserId}"), fromUserId: me);
            }
            return Results.NoContent();
        });

        items.MapDelete("/{id:guid}/endorse", async (Guid id, ClaimsPrincipal p, AppDb db) =>
        {
            var e = await db.Endorsements.FirstOrDefaultAsync(e => e.ProfileItemId == id && e.EndorsedById == p.UserId());
            if (e is not null) { db.Endorsements.Remove(e); await db.SaveChangesAsync(); }
            return Results.NoContent();
        });
    }

    private static void Apply(ProfileItem item, ProfileItemBody body)
    {
        if (!Enum.IsDefined(body.Kind)) throw ApiException.Bad("Unknown kind.");
        item.Kind = body.Kind;
        item.Name = Text.Required(body.Name, "Name", 100);
        item.Description = Text.Optional(body.Description, "Description", 1000);
        item.AvailableForTrade = body.AvailableForTrade;
    }

    public static ChildProfileBody ChildDto(ChildProfile c) =>
        new(c.OffersChildcare, c.NeedsChildcare, c.KidsAges, c.Allergies, c.ContactNumber, c.Notes);
}
