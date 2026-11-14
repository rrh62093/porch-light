using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PorchLight.Api.Data;
using WebPush;

namespace PorchLight.Api.Services;

public enum NotifyCategory { NewRequest, Response, DealUpdate, Reminder }

public record PushMessage(string Title, string Body, string Url);

public class PushOptions
{
    public string? VapidSubject { get; set; }
    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);
}

/// <summary>Shared Web Push client. Null VAPID details means push is off (typical in local dev).</summary>
public class PushClient
{
    public WebPushClient Client { get; }
    public VapidDetails? Vapid { get; }

    public PushClient(IOptions<PushOptions> options, IHttpClientFactory http)
    {
        var o = options.Value;
        Client = new WebPushClient(http.CreateClient("webpush"));
        Vapid = o.Enabled ? new VapidDetails(o.VapidSubject ?? "mailto:admin@example.com", o.VapidPublicKey, o.VapidPrivateKey) : null;
    }
}

/// <summary>
/// Decides who should get a notification (category toggles, mute by circle, mute by person) and sends it
/// before the request finishes. Sending inline matters on hosts like App Runner, which only give the app
/// CPU while a request is in flight, so background queues can stall.
/// Push failures are logged and never fail the request.
/// </summary>
public class Notifier(AppDb db, PushClient push, ILogger<Notifier> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    public async Task Notify(IEnumerable<Guid> recipients, NotifyCategory category, PushMessage message,
        Guid? fromUserId = null, Guid? circleId = null)
    {
        if (push.Vapid is null) return;
        var ids = recipients.Distinct().Where(id => id != fromUserId).ToList();
        if (ids.Count == 0) return;

        try
        {
            var users = db.Users.Where(u => ids.Contains(u.Id));
            users = category switch
            {
                NotifyCategory.NewRequest => users.Where(u => u.NotifyNewRequests),
                NotifyCategory.Response => users.Where(u => u.NotifyResponses),
                NotifyCategory.DealUpdate => users.Where(u => u.NotifyDealUpdates),
                NotifyCategory.Reminder => users.Where(u => u.NotifyReminders),
                _ => users,
            };
            if (circleId is { } cid)
                users = users.Where(u => !db.CircleMembers.Any(m => m.CircleId == cid && m.UserId == u.Id && m.Muted));
            if (fromUserId is { } from)
                users = users.Where(u => !db.UserMutes.Any(m => m.UserId == u.Id && m.MutedUserId == from));

            var subs = await db.PushSubscriptions.AsNoTracking()
                .Where(s => users.Any(u => u.Id == s.UserId))
                .ToListAsync();
            if (subs.Count == 0) return;

            var payload = JsonSerializer.Serialize(message, Json);
            var results = await Task.WhenAll(subs.Select(s => Send(s, payload)));
            var gone = subs.Zip(results).Where(x => !x.Second).Select(x => x.First.Id).ToList();
            if (gone.Count > 0)
                await db.PushSubscriptions.Where(s => gone.Contains(s.Id)).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sending notifications failed");
        }
    }

    /// <summary>Returns false when the browser has unsubscribed and the subscription should be dropped.</summary>
    private async Task<bool> Send(Data.PushSubscription sub, string payload)
    {
        using var cts = new CancellationTokenSource(SendTimeout);
        try
        {
            await push.Client.SendNotificationAsync(
                new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, push.Vapid, cts.Token);
            return true;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            log.LogWarning("Push to one device failed: {Message}", ex.Message);
            return true;
        }
    }
}

/// <summary>
/// Sends one reminder before each scheduled favor (lead time: Reminders:LeadMinutes).
/// Triggered by the in-process worker locally, or by a scheduler calling POST /api/internal/reminders in production.
/// </summary>
public class Reminders(AppDb db, Notifier notifier, IConfiguration config)
{
    public async Task<int> SendDue(CancellationToken ct = default)
    {
        var lead = TimeSpan.FromMinutes(config.GetValue("Reminders:LeadMinutes", 120));
        var now = DateTimeOffset.UtcNow;
        var due = await db.DealLegs.AsNoTracking()
            .Where(l => l.ScheduledAt != null && l.ReminderSentAt == null &&
                        (l.Status == LegStatus.Open || l.Status == LegStatus.CalledIn) &&
                        l.ScheduledAt <= now + lead && l.ScheduledAt > now - TimeSpan.FromHours(1))
            .Select(l => new { l.Id, l.DealId, l.Description, l.OwedById, l.OwedToId, OwedBy = l.OwedBy!.Name, OwedTo = l.OwedTo!.Name })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var leg in due)
        {
            // Claim the reminder atomically so overlapping runs (or two instances) never double-send.
            var claimed = await db.DealLegs.Where(l => l.Id == leg.Id && l.ReminderSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReminderSentAt, now), ct);
            if (claimed == 0) continue;
            sent++;
            await notifier.Notify([leg.OwedById], NotifyCategory.Reminder,
                new($"Coming up: {leg.Description}", $"You're doing this for {leg.OwedTo}.", $"/deals/{leg.DealId}"));
            await notifier.Notify([leg.OwedToId], NotifyCategory.Reminder,
                new($"Coming up: {leg.Description}", $"{leg.OwedBy} is doing this for you.", $"/deals/{leg.DealId}"));
        }
        return sent;
    }
}

/// <summary>In-process reminder loop for always-on hosts and local dev (Reminders:Mode = "background").</summary>
public class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<Reminders>().SendDue(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reminder worker error");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
