using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// Decides who should get a notification (category toggles, mute by circle, mute by person)
/// and queues it. Sending happens on a background worker so requests stay fast.
/// </summary>
public class Notifier(AppDb db, Channel<(Guid UserId, PushMessage Message)> queue)
{
    public async Task Notify(IEnumerable<Guid> recipients, NotifyCategory category, PushMessage message,
        Guid? fromUserId = null, Guid? circleId = null)
    {
        var ids = recipients.Distinct().Where(id => id != fromUserId).ToList();
        if (ids.Count == 0) return;

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

        foreach (var id in await users.Select(u => u.Id).ToListAsync())
            await queue.Writer.WriteAsync((id, message));
    }
}

public class PushSender(
    Channel<(Guid UserId, PushMessage Message)> queue,
    IServiceScopeFactory scopes,
    Microsoft.Extensions.Options.IOptions<PushOptions> options,
    ILogger<PushSender> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opts = options.Value;
        var client = new WebPushClient();
        var vapid = opts.Enabled
            ? new VapidDetails(opts.VapidSubject ?? "mailto:admin@example.com", opts.VapidPublicKey, opts.VapidPrivateKey)
            : null;

        await foreach (var (userId, message) in queue.Reader.ReadAllAsync(stop))
        {
            if (vapid is null) continue; // Push not configured (typical in local dev).
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var subs = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(stop);
                var payload = JsonSerializer.Serialize(message, Json);
                foreach (var sub in subs)
                {
                    try
                    {
                        await client.SendNotificationAsync(
                            new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, vapid, stop);
                    }
                    catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                    {
                        db.PushSubscriptions.Remove(sub); // The browser unsubscribed.
                    }
                    catch (WebPushException ex)
                    {
                        log.LogWarning("Push failed with {Status}", ex.StatusCode);
                    }
                }
                await db.SaveChangesAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Push worker error");
            }
        }
    }
}

/// <summary>Sends one reminder before each scheduled favor. Lead time is configurable (Reminders:LeadMinutes).</summary>
public class ReminderWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<ReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var lead = TimeSpan.FromMinutes(config.GetValue("Reminders:LeadMinutes", 120));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
                var now = DateTimeOffset.UtcNow;
                var due = await db.DealLegs
                    .Include(l => l.OwedBy).Include(l => l.OwedTo)
                    .Where(l => l.ScheduledAt != null && l.ReminderSentAt == null &&
                                (l.Status == LegStatus.Open || l.Status == LegStatus.CalledIn) &&
                                l.ScheduledAt <= now + lead && l.ScheduledAt > now - TimeSpan.FromHours(1))
                    .ToListAsync(stop);
                foreach (var leg in due)
                {
                    leg.ReminderSentAt = now;
                    await notifier.Notify([leg.OwedById], NotifyCategory.Reminder,
                        new($"Coming up: {leg.Description}", $"You're doing this for {leg.OwedTo!.Name}.", $"/deals/{leg.DealId}"));
                    await notifier.Notify([leg.OwedToId], NotifyCategory.Reminder,
                        new($"Coming up: {leg.Description}", $"{leg.OwedBy!.Name} is doing this for you.", $"/deals/{leg.DealId}"));
                }
                await db.SaveChangesAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reminder worker error");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
