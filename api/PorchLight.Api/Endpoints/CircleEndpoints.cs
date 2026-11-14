using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public record CreateCircleBody(string Name);
public record InviteBody(string Email);
public record MuteBody(bool Muted);
public record FlagBody(Guid? AboutUserId, string Message);

public static class CircleEndpoints
{
    public static void MapCircles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/circles").RequireAuthorization();

        g.MapGet("", async (ClaimsPrincipal p, AppDb db) =>
        {
            var me = p.UserId();
            return await db.CircleMembers.Where(m => m.UserId == me)
                .OrderBy(m => m.Circle!.Name)
                .Select(m => new
                {
                    m.Circle!.Id,
                    m.Circle.Name,
                    role = m.Role,
                    m.Muted,
                    memberCount = m.Circle.Members.Count,
                    openRequests = db.Requests.Count(r => r.CircleId == m.CircleId &&
                        (r.Status == RequestStatus.Open || r.Status == RequestStatus.InNegotiation)),
                })
                .ToListAsync();
        });

        g.MapPost("", async (CreateCircleBody body, ClaimsPrincipal p, AppDb db) =>
        {
            var me = p.UserId();
            var circle = new Circle { Name = Text.Required(body.Name, "Circle name", 80), CreatedById = me };
            circle.Members.Add(new CircleMember { UserId = me, Role = CircleRole.Admin });
            db.Circles.Add(circle);
            await db.SaveChangesAsync();
            return new { circle.Id, circle.Name };
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireMember(id, me);
            var circle = await db.Circles.FirstAsync(c => c.Id == id);
            var mine = await db.CircleMembers.FirstAsync(m => m.CircleId == id && m.UserId == me);
            var isAdmin = mine.Role == CircleRole.Admin;

            var members = await db.CircleMembers.Where(m => m.CircleId == id)
                .OrderByDescending(m => m.Role).ThenBy(m => m.User!.Name)
                .Select(m => new
                {
                    m.User!.Id,
                    m.User.Name,
                    role = m.Role,
                    m.JoinedAt,
                    // Circles only see that someone offers/needs childcare, never the details.
                    offersChildcare = db.ChildProfiles.Any(c => c.UserId == m.UserId && c.OffersChildcare),
                    needsChildcare = db.ChildProfiles.Any(c => c.UserId == m.UserId && c.NeedsChildcare),
                    skills = db.ProfileItems.Where(i => i.UserId == m.UserId && i.Kind != ProfileItemKind.LookingFor)
                        .OrderBy(i => i.Name).Select(i => i.Name).Take(4).ToList(),
                })
                .ToListAsync();

            var invites = isAdmin
                ? await db.Invites.Where(i => i.CircleId == id && i.Status == InviteStatus.Pending)
                    .OrderBy(i => i.CreatedAt).Select(i => new { i.Id, i.Email, i.CreatedAt, invitedBy = i.InvitedBy!.Name })
                    .ToListAsync<object>()
                : [];
            var openFlags = isAdmin ? await db.Flags.CountAsync(f => f.CircleId == id && !f.Resolved) : 0;

            return new { circle.Id, circle.Name, myRole = mine.Role, mine.Muted, members, invites, openFlags };
        });

        g.MapPut("/{id:guid}", async (Guid id, CreateCircleBody body, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            await access.RequireAdmin(id, p.UserId());
            var circle = await db.Circles.FirstAsync(c => c.Id == id);
            circle.Name = Text.Required(body.Name, "Circle name", 80);
            await db.SaveChangesAsync();
            return new { circle.Id, circle.Name };
        });

        // --- Invites (admins only for the pilot) ---

        g.MapPost("/{id:guid}/invites", async (Guid id, InviteBody body, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireAdmin(id, me);
            var email = Text.Email(body.Email);
            if (await db.CircleMembers.AnyAsync(m => m.CircleId == id && m.User!.Email == email))
                throw ApiException.Conflict("They're already in this circle.");
            var existing = await db.Invites.FirstOrDefaultAsync(i => i.CircleId == id && i.Email == email && i.Status == InviteStatus.Pending);
            if (existing is not null) return new { existing.Id, existing.Email };
            var invite = new Invite { CircleId = id, Email = email, InvitedById = me };
            db.Invites.Add(invite);
            await db.SaveChangesAsync();
            return new { invite.Id, invite.Email };
        });

        g.MapDelete("/{id:guid}/invites/{inviteId:guid}", async (Guid id, Guid inviteId, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            await access.RequireAdmin(id, p.UserId());
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == inviteId && i.CircleId == id)
                ?? throw ApiException.NotFound("Invite not found.");
            invite.Status = InviteStatus.Revoked;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // --- Membership ---

        g.MapPost("/{id:guid}/members/{userId:guid}/promote", async (Guid id, Guid userId, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            await access.RequireAdmin(id, p.UserId());
            var member = await db.CircleMembers.FirstOrDefaultAsync(m => m.CircleId == id && m.UserId == userId)
                ?? throw ApiException.NotFound("Member not found.");
            member.Role = CircleRole.Admin;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireAdmin(id, me);
            if (userId == me) throw ApiException.Bad("Use Leave circle to remove yourself.");
            var member = await db.CircleMembers.FirstOrDefaultAsync(m => m.CircleId == id && m.UserId == userId)
                ?? throw ApiException.NotFound("Member not found.");
            db.CircleMembers.Remove(member);
            // Recorded so the other dad can choose to close out IOUs with him.
            db.CircleRemovals.Add(new CircleRemoval { CircleId = id, UserId = userId, RemovedById = me });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/leave", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireMember(id, me);
            var mine = await db.CircleMembers.FirstAsync(m => m.CircleId == id && m.UserId == me);
            db.CircleMembers.Remove(mine);
            if (mine.Role == CircleRole.Admin &&
                !await db.CircleMembers.AnyAsync(m => m.CircleId == id && m.UserId != me && m.Role == CircleRole.Admin))
            {
                // Don't leave a circle without an admin: promote the longest-standing member.
                var next = await db.CircleMembers.Where(m => m.CircleId == id && m.UserId != me)
                    .OrderBy(m => m.JoinedAt).FirstOrDefaultAsync();
                if (next is not null) next.Role = CircleRole.Admin;
            }
            // IOUs are untouched: they outlive circle membership.
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPut("/{id:guid}/mute", async (Guid id, MuteBody body, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireMember(id, me);
            var mine = await db.CircleMembers.FirstAsync(m => m.CircleId == id && m.UserId == me);
            mine.Muted = body.Muted;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // --- Private flags to admins ---

        g.MapPost("/{id:guid}/flags", async (Guid id, FlagBody body, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            var me = p.UserId();
            await access.RequireMember(id, me);
            if (body.AboutUserId is { } about && !await access.IsMember(id, about) &&
                !await db.CircleRemovals.AnyAsync(r => r.CircleId == id && r.UserId == about))
                throw ApiException.Bad("That person isn't in this circle.");
            db.Flags.Add(new Flag
            {
                CircleId = id, ReportedById = me, AboutUserId = body.AboutUserId,
                Message = Text.Required(body.Message, "Message", 2000),
            });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapGet("/{id:guid}/flags", async (Guid id, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            await access.RequireAdmin(id, p.UserId());
            return await db.Flags.Where(f => f.CircleId == id)
                .OrderBy(f => f.Resolved).ThenByDescending(f => f.CreatedAt)
                .Select(f => new
                {
                    f.Id, f.Message, f.Resolved, f.CreatedAt,
                    reportedBy = new { f.ReportedBy!.Id, f.ReportedBy.Name },
                    about = f.AboutUser == null ? null : new { f.AboutUser.Id, f.AboutUser.Name },
                })
                .ToListAsync();
        });

        g.MapPost("/{id:guid}/flags/{flagId:guid}/resolve", async (Guid id, Guid flagId, ClaimsPrincipal p, AppDb db, Access access) =>
        {
            await access.RequireAdmin(id, p.UserId());
            var flag = await db.Flags.FirstOrDefaultAsync(f => f.Id == flagId && f.CircleId == id) ?? throw ApiException.NotFound();
            flag.Resolved = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // --- My invites ---

        var inv = app.MapGroup("/api/invites").RequireAuthorization();

        inv.MapGet("", async (ClaimsPrincipal p, AppDb db) =>
        {
            var email = await db.Users.Where(u => u.Id == p.UserId()).Select(u => u.Email).FirstAsync();
            return await db.Invites.Where(i => i.Email == email && i.Status == InviteStatus.Pending)
                .Select(i => new { i.Id, circle = new { i.Circle!.Id, i.Circle.Name }, invitedBy = i.InvitedBy!.Name, i.CreatedAt })
                .ToListAsync();
        });

        inv.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal p, AppDb db, Notifier notifier) =>
        {
            var me = await db.Users.FirstAsync(u => u.Id == p.UserId());
            var invite = await db.Invites.Include(i => i.Circle)
                .FirstOrDefaultAsync(i => i.Id == id && i.Email == me.Email && i.Status == InviteStatus.Pending)
                ?? throw ApiException.NotFound("Invite not found.");
            invite.Status = InviteStatus.Accepted;
            if (!await db.CircleMembers.AnyAsync(m => m.CircleId == invite.CircleId && m.UserId == me.Id))
                db.CircleMembers.Add(new CircleMember { CircleId = invite.CircleId, UserId = me.Id, Role = CircleRole.Member });
            await db.SaveChangesAsync();
            await notifier.Notify([invite.InvitedById], NotifyCategory.DealUpdate,
                new($"{me.Name} joined {invite.Circle!.Name}", "Your invite was accepted.", $"/circles/{invite.CircleId}"),
                fromUserId: me.Id, circleId: invite.CircleId);
            return new { circleId = invite.CircleId };
        });

        inv.MapPost("/{id:guid}/decline", async (Guid id, ClaimsPrincipal p, AppDb db) =>
        {
            var email = await db.Users.Where(u => u.Id == p.UserId()).Select(u => u.Email).FirstAsync();
            var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id && i.Email == email && i.Status == InviteStatus.Pending)
                ?? throw ApiException.NotFound("Invite not found.");
            invite.Status = InviteStatus.Declined;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
