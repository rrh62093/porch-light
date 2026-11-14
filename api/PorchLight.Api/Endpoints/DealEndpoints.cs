using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public record ScheduleBody(DateTimeOffset? ScheduledAt);
public record CompleteBody(DateOnly? CompletedOn);
public record CallInBody(string? Message, DateTimeOffset? ProposedTime);

public static class DealEndpoints
{
    public static void MapDeals(this IEndpointRouteBuilder app)
    {
        // The ledger: favors I owe and favors owed to me.
        app.MapGet("/api/ledger", async (bool? history, ClaimsPrincipal p, AppDb db) =>
        {
            var me = p.UserId();
            var legs = db.DealLegs.Where(l => l.OwedById == me || l.OwedToId == me);
            legs = history == true
                ? legs.Where(l => !LegStatusSets.Outstanding.Contains(l.Status))
                : legs.Where(l => LegStatusSets.Outstanding.Contains(l.Status));

            var rows = await legs
                .OrderBy(l => l.ScheduledAt == null).ThenBy(l => l.ScheduledAt).ThenByDescending(l => l.Deal!.CreatedAt)
                .Take(200)
                .Select(l => new
                {
                    l.Id, l.DealId, l.Description, l.Kind, l.Status, l.ScheduledAt, l.CompletedOn, l.CalledInRequestId,
                    iOwe = l.OwedById == me,
                    counterparty = l.OwedById == me
                        ? new { l.OwedTo!.Id, l.OwedTo.Name }
                        : new { l.OwedBy!.Id, l.OwedBy.Name },
                    dealTitle = l.Deal!.Request!.Title,
                    createdAt = l.Deal.CreatedAt,
                })
                .ToListAsync();

            return new { iOwe = rows.Where(r => r.iOwe), owedToMe = rows.Where(r => !r.iOwe) };
        }).RequireAuthorization();

        var deals = app.MapGroup("/api/deals").RequireAuthorization();

        deals.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            var deal = await RequireDeal(db, id, me);
            var other = deal.RequesterId == me ? deal.Responder! : deal.Requester!;

            // Kid details: only the counterparty of an open childcare deal (plus the owner, who already has them).
            object? theirKids = null;
            if (deal.InvolvesChildcare && deal.Status == DealStatus.Open && await access.CanSeeChildProfile(me, other.Id))
            {
                var cp = await db.ChildProfiles.FirstOrDefaultAsync(c => c.UserId == other.Id);
                if (cp is not null) theirKids = ProfileEndpoints.ChildDto(cp);
            }

            // Gentle endorsement nudge once the favor they did for me is done.
            var theyDidTheirPart = deal.Legs.Any(l => l.OwedById == other.Id && l.Status == LegStatus.Completed);
            var endorsable = theyDidTheirPart
                ? await db.ProfileItems.Where(i => i.UserId == other.Id && i.Kind != ProfileItemKind.LookingFor &&
                        !i.Endorsements.Any(e => e.EndorsedById == me))
                    .OrderBy(i => i.Name).Select(i => new { i.Id, i.Name, i.Kind }).Take(5).ToListAsync<object>()
                : [];

            var removed = await access.WasRemovedFromSharedCircle(other.Id, me);

            return new
            {
                deal.Id, deal.Status, deal.CreatedAt, deal.CompletedAt, deal.InvolvesChildcare,
                request = new
                {
                    deal.Request!.Id, deal.Request.Title, deal.Request.Description,
                    childcareNote = deal.Status == DealStatus.Open || deal.RequesterId == me ? deal.Request.ChildcareNote : null,
                },
                requester = new { deal.Requester!.Id, deal.Requester.Name },
                responder = new { deal.Responder!.Id, deal.Responder.Name },
                counterparty = new { other.Id, other.Name, removedFromCircle = removed },
                legs = deal.Legs.OrderBy(l => l.OwedById == deal.RequesterId).Select(l => new
                {
                    l.Id, l.Description, l.Kind, l.Status, l.ScheduledAt, l.CompletedOn, l.CompletedAt, l.CalledInRequestId,
                    owedBy = new { l.OwedBy!.Id, l.OwedBy.Name },
                    owedTo = new { l.OwedTo!.Id, l.OwedTo.Name },
                    iOwe = l.OwedById == me,
                }),
                theirKids,
                endorsable,
            };
        });

        deals.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            var deal = await RequireDeal(db, id, me);
            if (deal.Status != DealStatus.Open) throw ApiException.Conflict("This deal is already wrapped up.");
            if (deal.Legs.Any(l => l.Status is LegStatus.Completed or LegStatus.AwaitingConfirmation))
                throw ApiException.Conflict("Part of this deal already happened. Close out the remaining IOU instead.");
            deal.Status = DealStatus.Cancelled;
            foreach (var l in deal.Legs) l.Status = LegStatus.ClosedOut;
            await db.SaveChangesAsync();
            var other = deal.RequesterId == me ? deal.ResponderId : deal.RequesterId;
            await notifier.Notify([other], NotifyCategory.DealUpdate,
                new("Deal called off", deal.Request!.Title, $"/deals/{id}"), fromUserId: me);
            return Results.NoContent();
        });

        var legs = app.MapGroup("/api/legs").RequireAuthorization();

        legs.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleBody body, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            var leg = await RequireLeg(db, id, me);
            if (!LegStatusSets.Outstanding.Contains(leg.Status)) throw ApiException.Conflict("That one's already done.");
            if (leg.ScheduledAt != body.ScheduledAt)
            {
                leg.ScheduledAt = body.ScheduledAt;
                leg.ScheduleSequence++;
                leg.ReminderSentAt = null;
            }
            await db.SaveChangesAsync();
            var other = leg.OwedById == me ? leg.OwedToId : leg.OwedById;
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([other], NotifyCategory.DealUpdate,
                new($"{myName} {(body.ScheduledAt is null ? "cleared the time for" : "scheduled")}: {leg.Description}",
                    body.ScheduledAt is { } t ? $"{t:ddd MMM d, h:mm tt}. Grab the new calendar file." : "No time set now.",
                    $"/deals/{leg.DealId}"), fromUserId: me);
            return Results.NoContent();
        });

        // The dad who owes marks it done; the dad who's owed confirms.
        legs.MapPost("/{id:guid}/done", async (Guid id, CompleteBody body, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            var leg = await RequireLeg(db, id, me);
            if (leg.OwedById != me) throw ApiException.Forbidden("Only the dad who owes this can mark it done.");
            if (leg.Status is not (LegStatus.Open or LegStatus.CalledIn)) throw ApiException.Conflict("That one isn't open.");
            leg.Status = LegStatus.AwaitingConfirmation;
            leg.CompletedOn = body.CompletedOn ?? Today();
            await db.SaveChangesAsync();
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([leg.OwedToId], NotifyCategory.DealUpdate,
                new($"{myName} says it's done", $"{leg.Description}. Tap to confirm.", $"/deals/{leg.DealId}"), fromUserId: me);
            return Results.NoContent();
        });

        legs.MapPost("/{id:guid}/confirm", async (Guid id, CompleteBody body, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            var leg = await RequireLeg(db, id, me);
            if (leg.OwedToId != me) throw ApiException.Forbidden("Only the dad who was owed can confirm it.");
            if (!LegStatusSets.Outstanding.Contains(leg.Status)) throw ApiException.Conflict("That one's already closed.");
            // Closing always records when it happened; defaults to today.
            leg.CompletedOn = body.CompletedOn ?? leg.CompletedOn ?? Today();
            if (leg.CompletedOn > Today().AddDays(1)) throw ApiException.Bad("The completion date can't be in the future.");
            leg.Status = LegStatus.Completed;
            leg.CompletedAt = DateTimeOffset.UtcNow;
            await CloseRequestForLeg(db, leg);
            await MaybeCompleteDeal(db, leg.DealId);
            await db.SaveChangesAsync();
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([leg.OwedById], NotifyCategory.DealUpdate,
                new($"{myName} marked it complete", $"{leg.Description}. You're square on this one.", $"/deals/{leg.DealId}"), fromUserId: me);
            return Results.NoContent();
        });

        legs.MapPost("/{id:guid}/call-in", async (Guid id, CallInBody body, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = p.UserId();
            var leg = await RequireLeg(db, id, me);
            if (leg.OwedToId != me) throw ApiException.Forbidden("Only the dad who's owed can call this in.");
            if (leg.Status != LegStatus.Open) throw ApiException.Conflict("This IOU isn't open to call in.");

            // Calling in creates a new request addressed directly to the debtor. Works even if they've left the circle.
            var req = new Request
            {
                AuthorId = me,
                RecipientId = leg.OwedById,
                Title = leg.Kind == LegKind.Loose ? "Calling in a favor" : leg.Description,
                Description = Text.Optional(body.Message, "Message", 2000),
                NeededFrom = body.ProposedTime,
                CalledInLegId = leg.Id,
            };
            db.Requests.Add(req);
            leg.Status = LegStatus.CalledIn;
            leg.CalledInRequestId = req.Id;
            await db.SaveChangesAsync();

            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([leg.OwedById], NotifyCategory.DealUpdate,
                new($"{myName} is calling in an IOU", req.Description ?? leg.Description, $"/requests/{req.Id}"), fromUserId: me);
            return new { requestId = req.Id };
        });

        legs.MapPost("/{id:guid}/close-out", async (Guid id, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            var leg = await RequireLeg(db, id, me);
            if (!LegStatusSets.Outstanding.Contains(leg.Status)) throw ApiException.Conflict("That one's already closed.");
            // The dad who's owed can always let it go. The dad who owes can only close it out if
            // an admin removed the other dad from a circle they shared.
            if (leg.OwedById == me && !await access.WasRemovedFromSharedCircle(leg.OwedToId, me))
                throw ApiException.Forbidden("Only the dad who's owed can close this out.");
            leg.Status = LegStatus.ClosedOut;
            leg.CompletedOn ??= Today();
            leg.CompletedAt = DateTimeOffset.UtcNow;
            await CloseRequestForLeg(db, leg);
            await MaybeCompleteDeal(db, leg.DealId);
            await db.SaveChangesAsync();
            var other = leg.OwedById == me ? leg.OwedToId : leg.OwedById;
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([other], NotifyCategory.DealUpdate,
                new($"{myName} closed out an IOU", leg.Description, $"/deals/{leg.DealId}"), fromUserId: me);
            return Results.NoContent();
        });

        legs.MapGet("/{id:guid}/ics", async (Guid id, ClaimsPrincipal p, AppDb db, HttpContext http, IConfiguration config) =>
        {
            var me = p.UserId();
            var leg = await db.DealLegs.Include(l => l.OwedBy).Include(l => l.OwedTo)
                .FirstOrDefaultAsync(l => l.Id == id && (l.OwedById == me || l.OwedToId == me))
                ?? throw ApiException.NotFound();
            if (leg.ScheduledAt is not { } start) throw ApiException.Bad("Set a time first.");
            var summary = leg.OwedById == me
                ? $"{leg.Description} for {leg.OwedTo!.Name}"
                : $"{leg.OwedBy!.Name}: {leg.Description}";
            var origin = config["App:PublicOrigin"]?.TrimEnd('/') ?? $"{http.Request.Scheme}://{http.Request.Host}";
            var url = $"{origin}/deals/{leg.DealId}";
            var ics = Ics.Event(leg.Id, leg.ScheduleSequence, start, TimeSpan.FromHours(1), summary,
                "Tracked in Porch Light.", url);
            return Results.File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "porch-light.ics");
        });
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Deal> RequireDeal(AppDb db, Guid id, Guid me) =>
        await db.Deals
            .Include(d => d.Request).Include(d => d.Requester).Include(d => d.Responder)
            .Include(d => d.Legs).ThenInclude(l => l.OwedBy)
            .Include(d => d.Legs).ThenInclude(l => l.OwedTo)
            .FirstOrDefaultAsync(d => d.Id == id && (d.RequesterId == me || d.ResponderId == me))
        ?? throw ApiException.NotFound("Deal not found.");

    private static async Task<DealLeg> RequireLeg(AppDb db, Guid id, Guid me) =>
        await db.DealLegs.FirstOrDefaultAsync(l => l.Id == id && (l.OwedById == me || l.OwedToId == me))
        ?? throw ApiException.NotFound("IOU not found.");

    private static async Task CloseRequestForLeg(AppDb db, DealLeg leg)
    {
        if (leg.CalledInRequestId is not { } rid) return;
        var req = await db.Requests.FirstOrDefaultAsync(r => r.Id == rid);
        if (req is not null && req.Status != RequestStatus.Closed) req.Status = RequestStatus.Closed;
    }

    private static async Task MaybeCompleteDeal(AppDb db, Guid dealId)
    {
        var deal = await db.Deals.Include(d => d.Legs).FirstAsync(d => d.Id == dealId);
        if (deal.Status == DealStatus.Open && deal.Legs.All(l => !LegStatusSets.Outstanding.Contains(l.Status)))
        {
            deal.Status = DealStatus.Completed;
            deal.CompletedAt = DateTimeOffset.UtcNow;
        }
    }
}
