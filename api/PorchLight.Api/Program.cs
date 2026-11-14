using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PorchLight.Api.Data;
using PorchLight.Api.Endpoints;
using PorchLight.Api.Infrastructure;
using PorchLight.Api.Services;

// `dotnet run -- generate-vapid` prints a fresh key pair for Web Push.
if (args.Contains("generate-vapid"))
{
    var keys = WebPush.VapidHelper.GenerateVapidKeys();
    Console.WriteLine($"Push__VapidPublicKey={keys.PublicKey}\nPush__VapidPrivateKey={keys.PrivateKey}");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(config.GetConnectionString("Db")));
builder.Services.Configure<AuthOptions>(config.GetSection("Auth"));
builder.Services.Configure<PushOptions>(config.GetSection("Push"));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

// Keys persisted in Postgres so sessions survive App Runner restarts and multiple instances.
builder.Services.AddDataProtection().PersistKeysToDbContext<AppDb>().SetApplicationName("porch-light");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "porchlight";
        o.Cookie.HttpOnly = true;
        // Front end and API share an origin (CloudFront routes /api/* to App Runner), so Lax works and blocks CSRF.
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(90);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<Access>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddSingleton(Channel.CreateUnbounded<(Guid, PushMessage)>());
builder.Services.AddHostedService<PushSender>();
if (config.GetValue("Reminders:Enabled", true)) builder.Services.AddHostedService<ReminderWorker>();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseApiErrors();
app.UseAuthentication();
app.UseAuthorization();

// Simple same-origin check on writes, as a second line of defense behind SameSite cookies.
// Behind CloudFront the Host header is App Runner's, so the public origin is configured (App:PublicOrigin).
var publicOrigin = config["App:PublicOrigin"]?.TrimEnd('/');
app.Use(async (ctx, next) =>
{
    if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) &&
        ctx.Request.Headers.Origin is { Count: > 0 } origin &&
        !string.Equals(origin.ToString().TrimEnd('/'), publicOrigin, StringComparison.OrdinalIgnoreCase) &&
        !(Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var o) &&
          string.Equals(o.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { error = "Cross-origin request blocked." });
        return;
    }
    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));
app.MapAuth();
app.MapCircles();
app.MapProfiles();
app.MapRequests();
app.MapDeals();
app.MapPush();

// When the built front end is copied to wwwroot (single-container option), serve it too.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
