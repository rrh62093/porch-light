using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public record OfferBody(string Description, bool IsChildcare = false);

public record CreateRequestBody(
    Guid? CircleId, Guid? RecipientId, string Title, string? Description,
    DateTimeOffset? NeededFrom, DateTimeOffset? NeededTo, string? SuggestedRate,
    bool IsChildcare, string? ChildcareNote, List<OfferBody>? Offers);

public record RespondBody(
    string Kind, Guid? ResponderId, Guid? SelectedOfferId, string? CustomText, bool Loose,
    bool CustomIsChildcare, string? Message, DateTimeOffset? ProposedTime);

public static class RequestEndpoints
{
    private static readonly RequestStatus[] Active = [RequestStatus.Open, RequestStatus.InNegotiation];

    public static void MapRequests(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/requests").RequireAuthorization();

        g.MapGet("", async (string? view, Guid? circleId, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            var q = access.VisibleRequests(me);
            q = view switch
            {
                "mine" => q.Where(r => r.AuthorId == me),
                "direct" => q.Where(r => r.RecipientId == me && Active.Contains(r.Status)),
                "closed" => q.Where(r => !Active.Contains(r.Status)),
                _ => q.Where(r => Active.Contains(r.Status)),
            };
            if (circleId is { } cid) q = q.Where(r => r.CircleId == cid);

            return await q.OrderByDescending(r => r.CreatedAt).Take(100)
                .Select(r => new
                {
                    r.Id, r.Title, r.Description, r.Status, r.NeededFrom, r.NeededTo, r.SuggestedRate,
                    r.IsChildcare, r.CreatedAt,
                    isCalledIn = r.CalledInLegId != null,
                    author = new { r.Author!.Id, r.Author.Name },
                    circle = r.Circle == null ? null : new { r.Circle.Id, r.Circle.Name },
                    recipient = r.Recipient == null ? null : new { r.Recipient.Id, r.Recipient.Name },
                    offers = r.Offers.OrderBy(o => o.SortOrder).Select(o => o.Description).ToList(),
                    responderCount = r.Counters.Select(c => c.ResponderId).Distinct().Count(),
                    // Whose move it is, from my point of view.
                    awaitingMe = r.Counters.Any(c => c.Status == CounterStatus.Pending && c.AuthorId != me &&
                        (r.AuthorId == me || c.ResponderId == me)),
                })
                .ToListAsync();
        });

        g.MapPost("", async (CreateRequestBody body, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            if ((body.CircleId is null) == (body.RecipientId is null))
                throw ApiException.Bad("Post to a circle or send directly to one dad.");
            if (body.CircleId is { } cid) await access.RequireMember(cid, me);
            if (body.RecipientId is { } rid && (rid == me || !await access.CanViewProfile(me, rid)))
                throw ApiException.Bad("You can only send requests to dads in your circles.");
            if (body.NeededFrom is { } from && body.NeededTo is { } to && to < from)
                throw ApiException.Bad("The end time is before the start time.");

            var offers = (body.Offers ?? []).Where(o => !string.IsNullOrWhiteSpace(o.Description)).ToList();
            if (offers.Count == 0) throw ApiException.Bad("Add at least one thing you'd give in exchange.");
            if (offers.Count > 8) throw ApiException.Bad("Keep it to eight offers or fewer.");

            var req = new Request
            {
                AuthorId = me,
                CircleId = body.CircleId,
                RecipientId = body.RecipientId,
                Title = Text.Required(body.Title, "Title", 140),
                Description = Text.Optional(body.Description, "Description", 2000),
                NeededFrom = body.NeededFrom,
                NeededTo = body.NeededTo,
                SuggestedRate = Text.Optional(body.SuggestedRate, "Suggested rate", 60),
                IsChildcare = body.IsChildcare,
                ChildcareNote = body.IsChildcare ? Text.Optional(body.ChildcareNote, "Childcare note", 1000) : null,
                Offers = offers.Select((o, i) => new RequestOffer
                {
                    Description = Text.Required(o.Description, "Offer", 200), IsChildcare = o.IsChildcare, SortOrder = i,
                }).ToList(),
            };
            db.Requests.Add(req);
            await db.SaveChangesAsync();

            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            var msg = new PushMessage($"{myName}: {req.Title}", "Offering " + string.Join(" or ", req.Offers.Select(o => o.Description)), $"/requests/{req.Id}");
            if (req.CircleId is { } circle)
            {
                var members = await db.CircleMembers.Where(m => m.CircleId == circle).Select(m => m.UserId).ToListAsync();
                await notifier.Notify(members, NotifyCategory.NewRequest, msg, fromUserId: me, circleId: circle);
            }
            else
            {
                await notifier.Notify([req.RecipientId!.Value], NotifyCategory.Response, msg, fromUserId: me);
            }
            return new { req.Id };
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            var req = await access.RequireVisibleRequest(id, me);
            var author = await db.Users.Where(u => u.Id == req.AuthorId).Select(u => new { u.Id, u.Name }).FirstAsync();
            var circle = req.CircleId is null ? null
                : await db.Circles.Where(c => c.Id == req.CircleId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync();
            var recipient = req.RecipientId is null ? null
                : await db.Users.Where(u => u.Id == req.RecipientId).Select(u => new { u.Id, u.Name }).FirstAsync();

            var isAuthor = req.AuthorId == me;
            var counters = await db.Counters.Where(c => c.RequestId == id && (isAuthor || c.ResponderId == me))
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id, c.ResponderId, c.Kind, c.Status, c.Message, c.CustomText, c.Loose, c.CustomIsChildcare,
                    c.ProposedTime, c.CreatedAt, c.SelectedOfferId,
                    selectedOffer = c.SelectedOffer == null ? null : c.SelectedOffer.Description,
                    author = new { c.Author!.Id, c.Author.Name },
                    responderName = c.Responder!.Name,
                })
                .ToListAsync();
            var threads = counters.GroupBy(c => c.ResponderId)
                .Select(t => new { responder = new { id = t.Key, name = t.First().responderName }, messages = t.ToList() })
                .ToList();

            var deal = await db.Deals.Where(d => d.RequestId == id)
                .Select(d => new { d.Id, d.RequesterId, d.ResponderId }).FirstOrDefaultAsync();
            // The childcare note is part of the sensitive kid details: author and deal counterparty only.
            var canSeeChildNote = isAuthor || (deal is not null && deal.ResponderId == me);

            object? calledIn = null;
            Guid? dealId = deal?.Id;
            if (req.CalledInLegId is { } legId)
            {
                var leg = await db.DealLegs.FirstAsync(l => l.Id == legId);
                calledIn = new { leg.Id, leg.Description, leg.Kind, leg.Status, leg.DealId, leg.ScheduledAt };
                dealId = leg.DealId;
            }

            var canRespond = !isAuthor && Active.Contains(req.Status) &&
                (req.RecipientId == me || (req.CircleId is { } cid && await access.IsMember(cid, me)));

            return new
            {
                req.Id, req.Title, req.Description, req.Status, req.NeededFrom, req.NeededTo, req.SuggestedRate,
                req.IsChildcare, req.CreatedAt,
                childcareNote = canSeeChildNote ? req.ChildcareNote : null,
                author, circle, recipient, isAuthor, canRespond,
                offers = req.Offers.OrderBy(o => o.SortOrder).Select(o => new { o.Id, o.Description, o.IsChildcare }),
                threads,
                dealId,
                calledIn,
            };
        });

        g.MapPost("/{id:guid}/respond", async (Guid id, RespondBody body, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            var req = await access.RequireVisibleRequest(id, me);
            if (!Active.Contains(req.Status)) throw ApiException.Conflict("This request is no longer open.");
            var isAuthor = req.AuthorId == me;
            var kind = body.Kind?.ToLowerInvariant() switch
            {
                "accept" => CounterKind.Accept,
                "counter" => CounterKind.Counter,
                "decline" => CounterKind.Decline,
                _ => throw ApiException.Bad("Kind must be accept, counter, or decline."),
            };

            Guid responderId;
            if (isAuthor)
            {
                responderId = body.ResponderId ?? throw ApiException.Bad("Pick whose offer you're answering.");
                if (!await db.Counters.AnyAsync(c => c.RequestId == id && c.ResponderId == responderId))
                    throw ApiException.Bad("That dad hasn't responded yet.");
                if (kind == CounterKind.Accept) throw ApiException.Bad("Accept their latest counter instead.");
            }
            else
            {
                responderId = me;
                if (req.RecipientId is { } rid && rid != me) throw ApiException.Forbidden();
                if (req.CircleId is { } cid && !await access.IsMember(cid, me)) throw ApiException.Forbidden();
                if (kind == CounterKind.Decline && req.CalledInLegId is not null)
                    throw ApiException.Bad("You can't decline an IOU being called in, but you can counter with a better time.");
            }

            var terms = await ValidateTerms(req, body);
            await SupersedePending(db, id, responderId);
            var counter = new Counter
            {
                RequestId = id, ResponderId = responderId, AuthorId = me, Kind = kind,
                SelectedOfferId = terms.Offer?.Id, CustomText = terms.CustomText, Loose = body.Loose,
                CustomIsChildcare = body.CustomIsChildcare && terms.CustomText is not null,
                Message = Text.Optional(body.Message, "Message", 2000), ProposedTime = body.ProposedTime,
                Status = kind == CounterKind.Counter ? CounterStatus.Pending : CounterStatus.Accepted,
            };
            db.Counters.Add(counter);

            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            var other = isAuthor ? responderId : req.AuthorId;
            Guid? dealId = null;

            switch (kind)
            {
                case CounterKind.Accept:
                    dealId = await Agree(db, notifier, req, responderId, counter, me, myName);
                    break;
                case CounterKind.Counter:
                    if (req.Status == RequestStatus.Open) req.Status = RequestStatus.InNegotiation;
                    await db.SaveChangesAsync();
                    await notifier.Notify([other], NotifyCategory.Response,
                        new($"{myName} countered: {req.Title}", Describe(counter, terms.Offer) ?? "New terms proposed.", $"/requests/{id}"),
                        fromUserId: me);
                    break;
                case CounterKind.Decline:
                    counter.Status = CounterStatus.Declined;
                    if (req.RecipientId == me) req.Status = RequestStatus.Closed; // Direct request turned down.
                    await db.SaveChangesAsync();
                    await notifier.Notify([other], NotifyCategory.Response,
                        new($"{myName} passed on: {req.Title}", counter.Message ?? "No worries.", $"/requests/{id}"), fromUserId: me);
                    break;
            }
            return new { counterId = counter.Id, dealId };
        });

        g.MapPost("/{id:guid}/counters/{counterId:guid}/accept", async (Guid id, Guid counterId, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            var req = await access.RequireVisibleRequest(id, me);
            if (!Active.Contains(req.Status)) throw ApiException.Conflict("This request is no longer open.");
            var counter = await db.Counters.FirstOrDefaultAsync(c => c.Id == counterId && c.RequestId == id)
                ?? throw ApiException.NotFound("Counter not found.");
            if (counter.Status != CounterStatus.Pending) throw ApiException.Conflict("That counter is no longer on the table.");
            var otherParty = counter.AuthorId == req.AuthorId ? counter.ResponderId : req.AuthorId;
            if (otherParty != me) throw ApiException.Forbidden("Only the other dad can accept this counter.");

            counter.Status = CounterStatus.Accepted;
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            var dealId = await Agree(db, notifier, req, counter.ResponderId, counter, me, myName);
            return new { dealId };
        });

        g.MapPost("/{id:guid}/counters/{counterId:guid}/decline", async (Guid id, Guid counterId, ClaimsPrincipal p, AppDb db, Access access, Notifier notifier) =>
        {
            var me = p.UserId();
            var req = await access.RequireVisibleRequest(id, me);
            var counter = await db.Counters.FirstOrDefaultAsync(c => c.Id == counterId && c.RequestId == id && c.Status == CounterStatus.Pending)
                ?? throw ApiException.NotFound("Counter not found.");
            var otherParty = counter.AuthorId == req.AuthorId ? counter.ResponderId : req.AuthorId;
            if (otherParty != me) throw ApiException.Forbidden();
            counter.Status = CounterStatus.Declined;
            if (req.Status == RequestStatus.InNegotiation &&
                !await db.Counters.AnyAsync(c => c.RequestId == id && c.Id != counterId && c.Status == CounterStatus.Pending))
                req.Status = RequestStatus.Open;
            await db.SaveChangesAsync();
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.Name).FirstAsync();
            await notifier.Notify([counter.AuthorId], NotifyCategory.Response,
                new($"{myName} declined your counter", req.Title, $"/requests/{id}"), fromUserId: me);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/close", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            var req = await access.RequireVisibleRequest(id, me);
            if (req.AuthorId != me) throw ApiException.Forbidden("Only the person who posted it can close it.");
            if (!Active.Contains(req.Status)) return Results.NoContent();
            req.Status = RequestStatus.Closed;
            await db.Counters.Where(c => c.RequestId == id && c.Status == CounterStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CounterStatus.Superseded));
            if (req.CalledInLegId is { } legId)
            {
                // Withdrawing a call-in puts the IOU back to plain "open".
                var leg = await db.DealLegs.FirstAsync(l => l.Id == legId);
                if (leg.Status == LegStatus.CalledIn) { leg.Status = LegStatus.Open; leg.CalledInRequestId = null; }
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private record Terms(RequestOffer? Offer, string? CustomText);

    private static Task<Terms> ValidateTerms(Request req, RespondBody body)
    {
        RequestOffer? offer = null;
        if (body.SelectedOfferId is { } oid)
            offer = req.Offers.FirstOrDefault(o => o.Id == oid) ?? throw ApiException.Bad("That offer isn't on this request.");
        var custom = Text.Optional(body.CustomText, "Your terms", 300);
        return Task.FromResult(new Terms(offer, custom));
    }

    private static async Task SupersedePending(AppDb db, Guid requestId, Guid responderId) =>
        await db.Counters.Where(c => c.RequestId == requestId && c.ResponderId == responderId && c.Status == CounterStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CounterStatus.Superseded));

    private static string? Describe(Counter c, RequestOffer? offer)
    {
        var parts = new List<string>();
        if (c.Loose) parts.Add("you owe me one");
        else if (c.CustomText is not null) parts.Add(c.CustomText);
        else if (offer is not null) parts.Add(offer.Description);
        if (c.ProposedTime is { } t) parts.Add($"at {t:ddd MMM d, h:mm tt}");
        if (c.Message is not null) parts.Add($"\"{c.Message}\"");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// Both sides agreed on the terms in <paramref name="terms"/>. Either forms a deal with two legs,
    /// or (for a called-in IOU) schedules the existing leg.
    /// </summary>
    private static async Task<Guid> Agree(AppDb db, Notifier notifier, Request req, Guid responderId, Counter terms,
        Guid actorId, string actorName)
    {
        var otherParty = actorId == req.AuthorId ? responderId : req.AuthorId;
        req.Status = RequestStatus.Accepted;
        await db.Counters.Where(c => c.RequestId == req.Id && c.Status == CounterStatus.Pending && c.Id != terms.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CounterStatus.Superseded));

        if (req.CalledInLegId is { } legId)
        {
            var leg = await db.DealLegs.FirstAsync(l => l.Id == legId);
            var when = terms.ProposedTime ?? req.NeededFrom;
            if (when != leg.ScheduledAt) { leg.ScheduledAt = when; leg.ScheduleSequence++; leg.ReminderSentAt = null; }
            await db.SaveChangesAsync();
            await notifier.Notify([otherParty], NotifyCategory.DealUpdate,
                new($"{actorName} agreed: {leg.Description}",
                    when is { } t ? $"Set for {t:ddd MMM d, h:mm tt}" : "Time to be worked out.", $"/deals/{leg.DealId}"),
                fromUserId: actorId);
            return leg.DealId;
        }

        var offer = terms.SelectedOfferId is { } oid ? req.Offers.FirstOrDefault(o => o.Id == oid) : null;
        if (offer is null && terms.CustomText is null && !terms.Loose && req.Offers.Count == 1) offer = req.Offers[0];
        if (offer is null && terms.CustomText is null && !terms.Loose && req.Offers.Count > 1)
            throw ApiException.Bad("Pick which offer you'd like.");

        var loose = terms.Loose || (offer is null && terms.CustomText is null);
        var deal = new Deal
        {
            RequestId = req.Id,
            RequesterId = req.AuthorId,
            ResponderId = responderId,
            InvolvesChildcare = req.IsChildcare || (offer?.IsChildcare ?? false) || terms.CustomIsChildcare,
        };
        deal.Legs.Add(new DealLeg
        {
            OwedById = responderId, OwedToId = req.AuthorId, Description = req.Title, Kind = LegKind.Specific,
            ScheduledAt = terms.ProposedTime ?? req.NeededFrom,
        });
        deal.Legs.Add(new DealLeg
        {
            OwedById = req.AuthorId, OwedToId = responderId,
            Description = loose ? "One favor (you owe me one)" : terms.CustomText ?? offer!.Description,
            Kind = loose ? LegKind.Loose : LegKind.Specific,
        });
        db.Deals.Add(deal);
        await db.SaveChangesAsync();

        await notifier.Notify([otherParty], NotifyCategory.DealUpdate,
            new($"Deal! {actorName} agreed", req.Title, $"/deals/{deal.Id}"), fromUserId: actorId);
        // Let anyone else who was negotiating know it's been filled.
        var others = await db.Counters.Where(c => c.RequestId == req.Id && c.ResponderId != responderId)
            .Select(c => c.ResponderId).Distinct().ToListAsync();
        if (others.Count > 0)
            await notifier.Notify(others, NotifyCategory.Response,
                new("Request filled", $"{req.Title} has been taken care of. Thanks for offering.", $"/requests/{req.Id}"),
                fromUserId: req.AuthorId);
        return deal.Id;
    }
}
