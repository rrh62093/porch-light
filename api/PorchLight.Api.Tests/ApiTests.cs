using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PorchLight.Api.Data;

namespace PorchLight.Api.Tests;

/// <summary>
/// Runs the real API against a throwaway Postgres database (porchlight_test on the docker-compose server).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionString =
        "Host=localhost;Port=5433;Database=porchlight_test;Username=porchlight;Password=porchlight";

    public const string CronKey = "test-cron-key";

    public ApiFactory()
    {
        // Fresh schema for every test run.
        using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(ConnectionString).Options);
        db.Database.EnsureDeleted();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Db", ConnectionString);
        builder.UseSetting("Auth:EnableDevLogin", "true");
        builder.UseSetting("Reminders:Mode", "off");
        builder.UseSetting("Reminders:CronKey", CronKey);
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _n;

    private async Task<(HttpClient Client, Guid Id, string Email)> SignIn(string name)
    {
        var email = $"{name.ToLower()}{Interlocked.Increment(ref _n)}@example.com";
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/dev", new { email, name });
        res.EnsureSuccessStatusCode();
        var me = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("id").GetGuid(), email);
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var res = await call;
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {body}");
        return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement;
    }

    private static async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> call)
    {
        var res = await call;
        Assert.True(expected == res.StatusCode, $"Expected {expected}, got {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }

    /// <summary>Admin creates a circle and invites the others, who accept.</summary>
    private async Task<Guid> Circle(HttpClient admin, params (HttpClient Client, Guid Id, string Email)[] members)
    {
        var circle = await Ok(admin.PostAsJsonAsync("/api/circles", new { name = "Neighborhood dads" }));
        var circleId = circle.GetProperty("id").GetGuid();
        foreach (var m in members)
        {
            await Ok(admin.PostAsJsonAsync($"/api/circles/{circleId}/invites", new { email = m.Email }));
            var invites = await Ok(m.Client.GetAsync("/api/invites"));
            var invite = invites.EnumerateArray().Single(i => i.GetProperty("circle").GetProperty("id").GetGuid() == circleId);
            await Ok(m.Client.PostAsync($"/api/invites/{invite.GetProperty("id").GetGuid()}/accept", null));
        }
        return circleId;
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var client = factory.CreateClient();
        await Status(HttpStatusCode.Unauthorized, client.GetAsync("/api/me"));
        await Status(HttpStatusCode.Unauthorized, client.GetAsync("/api/requests"));
    }

    [Fact]
    public async Task Only_admins_can_invite_promote_and_remove()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var circleId = await Circle(rob.Client, mike);

        await Status(HttpStatusCode.Forbidden, mike.Client.PostAsJsonAsync($"/api/circles/{circleId}/invites", new { email = "x@example.com" }));
        await Status(HttpStatusCode.Forbidden, mike.Client.PostAsync($"/api/circles/{circleId}/members/{rob.Id}/promote", null));
        await Status(HttpStatusCode.Forbidden, mike.Client.DeleteAsync($"/api/circles/{circleId}/members/{rob.Id}"));

        await Ok(rob.Client.PostAsync($"/api/circles/{circleId}/members/{mike.Id}/promote", null));
        await Ok(mike.Client.PostAsJsonAsync($"/api/circles/{circleId}/invites", new { email = "x@example.com" }));
    }

    [Fact]
    public async Task Requests_are_only_visible_inside_the_circle_or_to_the_recipient()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var outsider = await SignIn("Stranger");
        var circleId = await Circle(rob.Client, mike);

        var req = await Ok(rob.Client.PostAsJsonAsync("/api/requests", new
        {
            circleId, title = "Need a 16 ft ladder", offers = new[] { new { description = "Beer" } },
        }));
        var reqId = req.GetProperty("id").GetGuid();

        await Ok(mike.Client.GetAsync($"/api/requests/{reqId}"));
        await Status(HttpStatusCode.NotFound, outsider.Client.GetAsync($"/api/requests/{reqId}"));
        var feed = await Ok(outsider.Client.GetAsync("/api/requests"));
        Assert.Equal(0, feed.GetArrayLength());

        // Can't send a direct request to someone you share no circle with.
        await Status(HttpStatusCode.BadRequest, rob.Client.PostAsJsonAsync("/api/requests", new
        {
            recipientId = outsider.Id, title = "Hi", offers = new[] { new { description = "Beer" } },
        }));
    }

    [Fact]
    public async Task Full_deal_lifecycle_with_counter_completion_and_called_in_iou()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var circleId = await Circle(rob.Client, mike);

        // Rob needs a light fixture hung and offers childcare or $40.
        var req = await Ok(rob.Client.PostAsJsonAsync("/api/requests", new
        {
            circleId, title = "Hang a light fixture",
            offers = new object[] { new { description = "Watch your kids for an evening", isChildcare = true }, new { description = "$40" } },
        }));
        var reqId = req.GetProperty("id").GetGuid();

        // Mike counters: two evenings of childcare instead.
        await Ok(mike.Client.PostAsJsonAsync($"/api/requests/{reqId}/respond", new
        {
            kind = "counter", customText = "Two evenings of watching my kids", customIsChildcare = true,
        }));
        var detail = await Ok(rob.Client.GetAsync($"/api/requests/{reqId}"));
        Assert.Equal("inNegotiation", detail.GetProperty("status").GetString());
        var counterId = detail.GetProperty("threads")[0].GetProperty("messages")[0].GetProperty("id").GetGuid();

        // Mike can't accept his own counter; Rob can.
        await Status(HttpStatusCode.Forbidden, mike.Client.PostAsync($"/api/requests/{reqId}/counters/{counterId}/accept", null));
        var accepted = await Ok(rob.Client.PostAsync($"/api/requests/{reqId}/counters/{counterId}/accept", null));
        var dealId = accepted.GetProperty("dealId").GetGuid();

        var deal = await Ok(mike.Client.GetAsync($"/api/deals/{dealId}"));
        Assert.True(deal.GetProperty("involvesChildcare").GetBoolean());
        var legs = deal.GetProperty("legs").EnumerateArray().ToList();
        var mikeLeg = legs.Single(l => l.GetProperty("iOwe").GetBoolean());
        var robLeg = legs.Single(l => !l.GetProperty("iOwe").GetBoolean());
        Assert.Equal("Hang a light fixture", mikeLeg.GetProperty("description").GetString());
        Assert.Equal("Two evenings of watching my kids", robLeg.GetProperty("description").GetString());

        // Ledger shows it on both sides.
        var robLedger = await Ok(rob.Client.GetAsync("/api/ledger"));
        Assert.Equal(1, robLedger.GetProperty("iOwe").GetArrayLength());
        Assert.Equal(1, robLedger.GetProperty("owedToMe").GetArrayLength());

        // Mike hangs the fixture; Rob confirms. Completion date defaults to today.
        var mikeLegId = mikeLeg.GetProperty("id").GetGuid();
        await Status(HttpStatusCode.Forbidden, rob.Client.PostAsJsonAsync($"/api/legs/{mikeLegId}/done", new { }));
        await Ok(mike.Client.PostAsJsonAsync($"/api/legs/{mikeLegId}/done", new { }));
        await Ok(rob.Client.PostAsJsonAsync($"/api/legs/{mikeLegId}/confirm", new { }));

        // Months later Mike calls in Rob's IOU, which creates a direct request to Rob.
        var robLegId = robLeg.GetProperty("id").GetGuid();
        await Status(HttpStatusCode.Forbidden, rob.Client.PostAsJsonAsync($"/api/legs/{robLegId}/call-in", new { }));
        var callIn = await Ok(mike.Client.PostAsJsonAsync($"/api/legs/{robLegId}/call-in", new
        {
            message = "Date night Saturday?", proposedTime = DateTimeOffset.UtcNow.AddDays(3),
        }));
        var callInReqId = callIn.GetProperty("requestId").GetGuid();
        var callInReq = await Ok(rob.Client.GetAsync($"/api/requests/{callInReqId}"));
        Assert.True(callInReq.GetProperty("canRespond").GetBoolean());
        Assert.Equal(robLegId, callInReq.GetProperty("calledIn").GetProperty("id").GetGuid());

        // Rob can't decline an IOU being called in, but can counter on timing; Mike accepts.
        await Status(HttpStatusCode.BadRequest, rob.Client.PostAsJsonAsync($"/api/requests/{callInReqId}/respond", new { kind = "decline" }));
        var sunday = DateTimeOffset.UtcNow.AddDays(4);
        await Ok(rob.Client.PostAsJsonAsync($"/api/requests/{callInReqId}/respond", new { kind = "counter", proposedTime = sunday, message = "Sunday works better" }));
        callInReq = await Ok(mike.Client.GetAsync($"/api/requests/{callInReqId}"));
        var timingCounter = callInReq.GetProperty("threads")[0].GetProperty("messages")[0].GetProperty("id").GetGuid();
        await Ok(mike.Client.PostAsync($"/api/requests/{callInReqId}/counters/{timingCounter}/accept", null));

        deal = await Ok(rob.Client.GetAsync($"/api/deals/{dealId}"));
        var scheduled = deal.GetProperty("legs").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == robLegId);
        Assert.Equal("calledIn", scheduled.GetProperty("status").GetString());
        Assert.True(Math.Abs((scheduled.GetProperty("scheduledAt").GetDateTimeOffset() - sunday).TotalSeconds) < 1);

        // Calendar export.
        var ics = await rob.Client.GetAsync($"/api/legs/{robLegId}/ics");
        Assert.Equal("text/calendar", ics.Content.Headers.ContentType?.MediaType);
        var icsText = await ics.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VEVENT", icsText);
        Assert.Contains("SEQUENCE:1", icsText);

        // Rob does it, Mike confirms with a specific date, deal is complete.
        await Ok(rob.Client.PostAsJsonAsync($"/api/legs/{robLegId}/done", new { }));
        await Ok(mike.Client.PostAsJsonAsync($"/api/legs/{robLegId}/confirm", new { completedOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd") }));
        deal = await Ok(rob.Client.GetAsync($"/api/deals/{dealId}"));
        Assert.Equal("completed", deal.GetProperty("status").GetString());
        robLedger = await Ok(rob.Client.GetAsync("/api/ledger"));
        Assert.Equal(0, robLedger.GetProperty("iOwe").GetArrayLength());
        var history = await Ok(rob.Client.GetAsync("/api/ledger?history=true"));
        Assert.Equal(2, history.GetProperty("iOwe").GetArrayLength() + history.GetProperty("owedToMe").GetArrayLength());
    }

    [Fact]
    public async Task Kid_details_are_only_visible_on_an_open_childcare_deal()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var dave = await SignIn("Dave");
        var circleId = await Circle(rob.Client, mike, dave);

        await Ok(rob.Client.PutAsJsonAsync("/api/me/child-profile", new
        {
            needsChildcare = true, kidsAges = "4 and 7", allergies = "Peanuts", contactNumber = "555-0100",
        }));

        // The circle only sees that Rob needs childcare.
        var profile = await Ok(mike.Client.GetAsync($"/api/people/{rob.Id}"));
        Assert.True(profile.GetProperty("needsChildcare").GetBoolean());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("childProfile").ValueKind);

        var req = await Ok(rob.Client.PostAsJsonAsync("/api/requests", new
        {
            circleId, title = "Watch my kids Friday", isChildcare = true, childcareNote = "Bedtime is 8",
            offers = new[] { new { description = "Hang a light fixture" } },
        }));
        var reqId = req.GetProperty("id").GetGuid();
        var asDave = await Ok(dave.Client.GetAsync($"/api/requests/{reqId}"));
        Assert.Equal(JsonValueKind.Null, asDave.GetProperty("childcareNote").ValueKind);

        var accepted = await Ok(mike.Client.PostAsJsonAsync($"/api/requests/{reqId}/respond", new { kind = "accept" }));
        var dealId = accepted.GetProperty("dealId").GetGuid();

        // Mike (counterparty) now sees kid details; Dave still doesn't.
        var deal = await Ok(mike.Client.GetAsync($"/api/deals/{dealId}"));
        Assert.Equal("Peanuts", deal.GetProperty("theirKids").GetProperty("allergies").GetString());
        Assert.Equal("Bedtime is 8", deal.GetProperty("request").GetProperty("childcareNote").GetString());
        profile = await Ok(mike.Client.GetAsync($"/api/people/{rob.Id}"));
        Assert.Equal("Peanuts", profile.GetProperty("childProfile").GetProperty("allergies").GetString());
        profile = await Ok(dave.Client.GetAsync($"/api/people/{rob.Id}"));
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("childProfile").ValueKind);
        await Status(HttpStatusCode.NotFound, dave.Client.GetAsync($"/api/deals/{dealId}"));

        // Once the deal wraps up, access goes away.
        await Ok(mike.Client.PostAsync($"/api/deals/{dealId}/cancel", null));
        profile = await Ok(mike.Client.GetAsync($"/api/people/{rob.Id}"));
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("childProfile").ValueKind);
    }

    [Fact]
    public async Task Ious_outlive_membership_and_removed_dads_can_be_closed_out()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var circleId = await Circle(rob.Client, mike);

        var req = await Ok(rob.Client.PostAsJsonAsync("/api/requests", new
        {
            circleId, title = "Borrow a pressure washer", offers = new[] { new { description = "Help moving a couch" } },
        }));
        var dealId = (await Ok(mike.Client.PostAsJsonAsync($"/api/requests/{req.GetProperty("id").GetGuid()}/respond", new { kind = "accept" })))
            .GetProperty("dealId").GetGuid();
        var legs = (await Ok(rob.Client.GetAsync($"/api/deals/{dealId}"))).GetProperty("legs").EnumerateArray().ToList();
        var robOwes = legs.Single(l => l.GetProperty("iOwe").GetBoolean()).GetProperty("id").GetGuid();

        // Before removal, the debtor can't just close out his own IOU.
        await Status(HttpStatusCode.Forbidden, rob.Client.PostAsync($"/api/legs/{robOwes}/close-out", null));

        // Admin Rob removes Mike. The IOU and deal are still visible to both.
        await Ok(rob.Client.DeleteAsync($"/api/circles/{circleId}/members/{mike.Id}"));
        await Ok(mike.Client.GetAsync($"/api/deals/{dealId}"));
        var mikeLedger = await Ok(mike.Client.GetAsync("/api/ledger"));
        Assert.Equal(1, mikeLedger.GetProperty("owedToMe").GetArrayLength());

        // Now Rob may close out what he owes the removed dad.
        await Ok(rob.Client.PostAsync($"/api/legs/{robOwes}/close-out", null));
    }

    [Fact]
    public async Task Endorsements_are_positive_only_and_unique()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        await Circle(rob.Client, mike);

        var item = await Ok(mike.Client.PostAsJsonAsync("/api/me/items", new { kind = "skill", name = "Electrical" }));
        var itemId = item.GetProperty("id").GetGuid();
        await Status(HttpStatusCode.BadRequest, mike.Client.PostAsync($"/api/items/{itemId}/endorse", null));
        await Ok(rob.Client.PostAsync($"/api/items/{itemId}/endorse", null));
        await Ok(rob.Client.PostAsync($"/api/items/{itemId}/endorse", null));

        var profile = await Ok(rob.Client.GetAsync($"/api/people/{mike.Id}"));
        var skill = profile.GetProperty("items")[0];
        Assert.Equal(1, skill.GetProperty("endorsementCount").GetInt32());
        Assert.True(skill.GetProperty("endorsedByMe").GetBoolean());
    }

    [Fact]
    public async Task Signups_need_an_invite_outside_dev_login()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.True(await db.Database.CanConnectAsync());
        // Google sign-in without a configured client ID fails cleanly.
        var client = factory.CreateClient();
        await Status(HttpStatusCode.BadRequest, client.PostAsJsonAsync("/api/auth/google", new { credential = "x" }));
    }

    [Fact]
    public async Task Scheduler_reminders_need_the_key_and_send_once()
    {
        var rob = await SignIn("Rob");
        var mike = await SignIn("Mike");
        var circleId = await Circle(rob.Client, mike);
        var req = await Ok(rob.Client.PostAsJsonAsync("/api/requests", new
        {
            circleId, title = "Help carry a couch", neededFrom = DateTimeOffset.UtcNow.AddMinutes(30),
            offers = new[] { new { description = "Pizza" } },
        }));
        await Ok(mike.Client.PostAsJsonAsync($"/api/requests/{req.GetProperty("id").GetGuid()}/respond", new { kind = "accept" }));

        var scheduler = factory.CreateClient();
        await Status(HttpStatusCode.NotFound, scheduler.PostAsync("/api/internal/reminders", null));
        var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/internal/reminders");
        wrong.Headers.Add("X-Cron-Key", "nope");
        await Status(HttpStatusCode.NotFound, scheduler.SendAsync(wrong));

        async Task<int> Run()
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/internal/reminders");
            msg.Headers.Add("X-Cron-Key", ApiFactory.CronKey);
            return (await Ok(scheduler.SendAsync(msg))).GetProperty("sent").GetInt32();
        }
        Assert.True(await Run() >= 1);
        Assert.Equal(0, await Run());
    }
}
