using System.Security.Claims;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PorchLight.Api.Data;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

namespace PorchLight.Api.Endpoints;

public class AuthOptions
{
    public string? GoogleClientId { get; set; }
    /// <summary>Local development only: sign in as any email without Google.</summary>
    public bool EnableDevLogin { get; set; }
    /// <summary>Emails allowed to sign up without an invite (the first circle creators).</summary>
    public List<string> BootstrapEmails { get; set; } = new();
}

public record GoogleLoginBody(string Credential);
public record DevLoginBody(string Email, string? Name);
public record UpdateMeBody(string Name);
public record NotificationPrefsBody(bool NewRequests, bool Responses, bool DealUpdates, bool Reminders);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/config", (IOptions<AuthOptions> a, IOptions<PushOptions> p, IHostEnvironment env) => new
        {
            googleClientId = a.Value.GoogleClientId,
            devLogin = a.Value.EnableDevLogin && env.IsDevelopment(),
            vapidPublicKey = p.Value.Enabled ? p.Value.VapidPublicKey : null,
        });

        auth.MapPost("/google", async (GoogleLoginBody body, HttpContext http, AppDb db, IOptions<AuthOptions> opts) =>
        {
            if (string.IsNullOrWhiteSpace(opts.Value.GoogleClientId))
                throw ApiException.Bad("Google sign-in isn't configured on the server.");
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(body.Credential,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = [opts.Value.GoogleClientId] });
            }
            catch (InvalidJwtException)
            {
                throw new ApiException(401, "Google sign-in failed. Please try again.");
            }
            if (!payload.EmailVerified) throw new ApiException(401, "Your Google email isn't verified.");

            var user = await FindOrCreate(db, opts.Value, payload.Email, payload.Name, payload.Subject);
            await SignIn(http, user);
            return Me(user);
        });

        auth.MapPost("/dev", async (DevLoginBody body, HttpContext http, AppDb db, IOptions<AuthOptions> opts, IHostEnvironment env) =>
        {
            if (!(opts.Value.EnableDevLogin && env.IsDevelopment())) throw ApiException.NotFound();
            var email = Text.Email(body.Email);
            var user = await FindOrCreate(db, opts.Value, email, body.Name, subject: null, skipInviteCheck: true);
            await SignIn(http, user);
            return Me(user);
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal principal, AppDb db) =>
        {
            var id = principal.UserId();
            var user = await db.Users.FindAsync(id) ?? throw new ApiException(401, "Please sign in.");
            var openLegs = LegStatusSets.Outstanding;
            return new
            {
                user = Me(user),
                counts = new
                {
                    iOwe = await db.DealLegs.CountAsync(l => l.OwedById == id && openLegs.Contains(l.Status)),
                    owedToMe = await db.DealLegs.CountAsync(l => l.OwedToId == id && openLegs.Contains(l.Status)),
                    // Things waiting on me: confirmations and called-in IOUs I haven't answered.
                    needsConfirmation = await db.DealLegs.CountAsync(l => l.OwedToId == id && l.Status == LegStatus.AwaitingConfirmation),
                    pendingInvites = await db.Invites.CountAsync(i => i.Email == user.Email && i.Status == InviteStatus.Pending),
                    circles = await db.CircleMembers.CountAsync(m => m.UserId == id),
                },
            };
        });

        me.MapPut("", async (UpdateMeBody body, ClaimsPrincipal principal, AppDb db) =>
        {
            var user = await db.Users.FindAsync(principal.UserId()) ?? throw ApiException.NotFound();
            user.Name = Text.Required(body.Name, "Name", 100);
            await db.SaveChangesAsync();
            return Me(user);
        });

        me.MapPut("/notifications", async (NotificationPrefsBody body, ClaimsPrincipal principal, AppDb db) =>
        {
            var user = await db.Users.FindAsync(principal.UserId()) ?? throw ApiException.NotFound();
            user.NotifyNewRequests = body.NewRequests;
            user.NotifyResponses = body.Responses;
            user.NotifyDealUpdates = body.DealUpdates;
            user.NotifyReminders = body.Reminders;
            await db.SaveChangesAsync();
            return Me(user);
        });
    }

    private static object Me(User u) => new
    {
        u.Id,
        u.Name,
        u.Email,
        notifications = new
        {
            newRequests = u.NotifyNewRequests,
            responses = u.NotifyResponses,
            dealUpdates = u.NotifyDealUpdates,
            reminders = u.NotifyReminders,
        },
    };

    private static async Task<User> FindOrCreate(AppDb db, AuthOptions opts, string email, string? name, string? subject,
        bool skipInviteCheck = false)
    {
        email = email.Trim().ToLowerInvariant();
        var user = subject is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject)
            : null;
        user ??= await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            // Invite-only: new accounts need a pending invite, or to be a bootstrap admin.
            var allowed = skipInviteCheck
                || opts.BootstrapEmails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase))
                || await db.Invites.AnyAsync(i => i.Email == email && i.Status == InviteStatus.Pending);
            if (!allowed)
                throw ApiException.Forbidden("Porch Light is invite-only. Ask a friend who runs a circle to invite this email.");

            user = new User { Email = email, Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim() };
            db.Users.Add(user);
        }

        if (subject is not null && user.GoogleSubject is null) user.GoogleSubject = subject;
        if (user.Email != email) user.Email = email;
        await db.SaveChangesAsync();
        return user;
    }

    private static Task SignIn(HttpContext http, User user)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}

public static class LegStatusSets
{
    /// <summary>Legs that still represent something owed.</summary>
    public static readonly LegStatus[] Outstanding = [LegStatus.Open, LegStatus.CalledIn, LegStatus.AwaitingConfirmation];
}
