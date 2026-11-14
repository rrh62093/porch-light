using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;

namespace PorchLight.Api.Services;

/// <summary>
/// The visibility and permission rules from the plan, in one place:
/// - Requests: only in circles you belong to, direct to you, or your own.
/// - ChildProfile: only the owner and the counterparty of an open deal that involves childcare.
/// - Invites, promotions, removals: admins only.
/// </summary>
public class Access(AppDb db)
{
    public Task<bool> IsMember(Guid circleId, Guid userId) =>
        db.CircleMembers.AnyAsync(m => m.CircleId == circleId && m.UserId == userId);

    public Task<bool> IsAdmin(Guid circleId, Guid userId) =>
        db.CircleMembers.AnyAsync(m => m.CircleId == circleId && m.UserId == userId && m.Role == CircleRole.Admin);

    public async Task RequireMember(Guid circleId, Guid userId)
    {
        if (!await IsMember(circleId, userId)) throw ApiException.NotFound("Circle not found.");
    }

    public async Task RequireAdmin(Guid circleId, Guid userId)
    {
        await RequireMember(circleId, userId);
        if (!await IsAdmin(circleId, userId)) throw ApiException.Forbidden("Only circle admins can do that.");
    }

    public Task<bool> SharesCircle(Guid a, Guid b) =>
        db.CircleMembers.AnyAsync(m => m.UserId == a &&
            db.CircleMembers.Any(o => o.CircleId == m.CircleId && o.UserId == b));

    public Task<bool> HaveDealTogether(Guid a, Guid b) =>
        db.Deals.AnyAsync(d => (d.RequesterId == a && d.ResponderId == b) || (d.RequesterId == b && d.ResponderId == a));

    /// <summary>Profiles are visible to circle-mates and to anyone you've dealt with (IOUs outlive circles).</summary>
    public async Task<bool> CanViewProfile(Guid viewer, Guid target) =>
        viewer == target || await SharesCircle(viewer, target) || await HaveDealTogether(viewer, target);

    public async Task<bool> CanSeeChildProfile(Guid viewer, Guid owner) =>
        viewer == owner || await db.Deals.AnyAsync(d =>
            d.Status == DealStatus.Open && d.InvolvesChildcare &&
            ((d.RequesterId == viewer && d.ResponderId == owner) || (d.RequesterId == owner && d.ResponderId == viewer)));

    /// <summary>Requests the user is allowed to see, as a composable query.</summary>
    public IQueryable<Request> VisibleRequests(Guid userId) =>
        db.Requests.Where(r =>
            r.AuthorId == userId ||
            r.RecipientId == userId ||
            (r.CircleId != null && db.CircleMembers.Any(m => m.CircleId == r.CircleId && m.UserId == userId)));

    public async Task<Request> RequireVisibleRequest(Guid requestId, Guid userId)
    {
        var req = await VisibleRequests(userId)
            .Include(r => r.Offers)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        return req ?? throw ApiException.NotFound("Request not found.");
    }

    /// <summary>True when an admin removed either party from a circle they both belonged to.</summary>
    public Task<bool> WasRemovedFromSharedCircle(Guid removedUser, Guid otherUser) =>
        db.CircleRemovals.AnyAsync(r => r.UserId == removedUser &&
            (db.CircleMembers.Any(m => m.CircleId == r.CircleId && m.UserId == otherUser) ||
             db.Circles.Any(c => c.Id == r.CircleId && c.CreatedById == otherUser)));
}
