# Porch Light

Invite-only circles of dad friends trading skills, tools, and favors (including kid-watching), with a ledger of who owes whom. The product decisions live in [docs/PLAN.md](docs/PLAN.md).

| Part | Tech | Where |
| --- | --- | --- |
| Front end | React + Vite + Mantine, installable PWA with Web Push | `web/`, served by the API in production |
| API | ASP.NET Core 8 minimal APIs, EF Core | `api/PorchLight.Api` |
| Database | PostgreSQL 16 | Docker |
| Auth | Google Identity Services ID token → API cookie session | |
| Tests | xUnit integration tests against real Postgres | `api/PorchLight.Api.Tests` |

## Run it locally

You need the .NET 8 SDK, Node 20+, and Docker.

```bash
docker compose up -d
```

```bash
cd api/PorchLight.Api && dotnet run
```

```bash
cd web && npm install && npm run dev
```

Open http://localhost:5173. In Development the login page has a **dev sign-in** (any email, no Google needed). The API applies migrations on startup.

For sample data (a circle with Rob, Mike, and Dave, some skills, an open request, and a counter offer):

```bash
scripts/seed-dev.sh
```

Run the tests (they create and drop a `porchlight_test` database on the Docker Postgres):

```bash
dotnet test
```

Optional for local push testing: generate VAPID keys and put them in `api/PorchLight.Api/appsettings.Development.json` under `Push`.

```bash
cd api/PorchLight.Api && dotnet run -- generate-vapid
```

## How it maps to the plan

- **Circles**: create, admin invites by email, accept/decline, promote, remove, leave, mute circle, private flags to admins. New accounts need a pending invite (or be listed in `Auth:BootstrapEmails`).
- **Profiles**: skills, tools, and "looking for"; positive-only endorsements from circle-mates (unique per endorser); childcare switches visible to circles; kid details (ages, allergies, contact, notes) returned only to the owner and to the counterparty of an *open* deal that involves childcare.
- **Requests**: to a circle or direct to one dad, with several offers. Responders accept (picking an offer) or counter (another offer, custom terms, or "you owe me one", plus a time and message). The requester can accept, decline, or counter back. One deal per request (enforced by a unique index).
- **Deals and IOUs**: two legs per deal. The dad who owes marks a leg done; the dad who's owed confirms. Closing always records a completion date (defaults to today). The holder can call in an open IOU, which creates a direct request; the debtor can agree or counter on timing but not decline. IOUs outlive circle membership; if an admin removes a dad, the other side may close out IOUs with them. Scheduled legs download as `.ics` files with a stable UID and increasing SEQUENCE, so a re-downloaded file updates the calendar entry.
- **Notifications**: Web Push from the API with category toggles, mute by circle, and private mute by person. One reminder before scheduled favors (`Reminders:LeadMinutes`, default 120, since the exact timing was still open).

The access rules are in [Access.cs](api/PorchLight.Api/Services/Access.cs) and covered by [ApiTests.cs](api/PorchLight.Api.Tests/ApiTests.cs).

## Configuration

All settings can be environment variables (`__` for nesting). In production, [deploy/compose.yml](deploy/compose.yml) maps the `.env` values onto these.

| Setting | Example | Notes |
| --- | --- | --- |
| `ConnectionStrings__Db` | `Host=...rds.amazonaws.com;Database=porchlight;Username=...;Password=...;SSL Mode=Require` | |
| `App__PublicOrigin` | `https://porchlight.dad` | The public HTTPS address. Used for the same-origin check and links in calendar files |
| `Auth__GoogleClientId` | `1234-abc.apps.googleusercontent.com` | |
| `Auth__BootstrapEmails__0` | `you@gmail.com` | Who can sign up without an invite (you, to create the first circle) |
| `Push__VapidSubject` | `mailto:you@gmail.com` | |
| `Push__VapidPublicKey` / `Push__VapidPrivateKey` | from `generate-vapid` | Never change these after people subscribe |
| `Reminders__LeadMinutes` | `120` | |
| `Reminders__Mode` | `background` / `scheduler` / `off` | `background` for an always-on server; `scheduler` for hosts that only give the app CPU during requests |
| `Reminders__CronKey` | long random string | Required for `scheduler`: a scheduler calls `POST /api/internal/reminders` with header `X-Cron-Key` every 5 minutes |

## Deploying (self-hosted)

Production runs on a home server VM behind Cloudflare Tunnel. Everything is in [deploy/](deploy/):

- [deploy/Dockerfile](deploy/Dockerfile): one image with the API and the built web front end (served from `wwwroot`).
- [deploy/compose.yml](deploy/compose.yml): the app, Postgres, and `cloudflared`. Nothing is published on a host port; the tunnel is the only way in.
- [deploy/.env.example](deploy/.env.example): every setting, with notes. Copy it to `deploy/.env` (never committed) and fill it in.

```bash
cd deploy && docker compose up -d --build
```

The hosting requirements (VM, network isolation, backups, monitoring, acceptance checklist) are in [docs/HOSTING.md](docs/HOSTING.md).

Google sign-in: in Google Cloud Console → APIs & Services, set the OAuth consent screen to External in **Testing** mode and add each dad's Gmail as a **test user** (this also gates who can sign in). Then create an OAuth client ID (Web application) with your public address and `http://localhost:5173` as authorized JavaScript origins. No redirect URIs are needed.

Then sign in with the bootstrap email, create a circle, and invite everyone.

## Pilot launch checklist (Phase 6)

- Each dad is a Google test user **and** has a pending invite to a circle.
- iPhone: open the site in Safari → Share → **Add to Home Screen** → open from the home screen → Circles → Notifications & settings → **Turn on notifications** → Allow. The Settings screen shows these steps when it detects an iPhone that isn't installed yet.
- Android: Chrome offers to install; notifications work from the browser too.
- Send a test notification from Settings to confirm each device.

## Not built yet (by design or deferred)

- Photos, a credits economy, native apps, public signup (out of scope for v1 per the plan).
- Infrastructure as code: the plan allowed a documented manual setup for the pilot; Terraform or CDK can come later.
- Calendar subscription feed (ICS files are regenerated on reschedule for now).
- Legal review of terms and childcare liability before any wider release.
