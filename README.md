# Porch Light

Invite-only circles of dad friends trading skills, tools, and favors (including kid-watching), with a ledger of who owes whom. The product decisions live in [docs/PLAN.md](docs/PLAN.md).

| Part | Tech | Where |
| --- | --- | --- |
| Front end | React + Vite + Mantine, installable PWA with Web Push | `web/` → S3 + CloudFront |
| API | ASP.NET Core 8 minimal APIs, EF Core | `api/PorchLight.Api` → App Runner |
| Database | PostgreSQL 16 | RDS (local: Docker) |
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

All settings can be environment variables (`__` for nesting), which is how App Runner should get them.

| Setting | Example | Notes |
| --- | --- | --- |
| `ConnectionStrings__Db` | `Host=...rds.amazonaws.com;Database=porchlight;Username=...;Password=...;SSL Mode=Require` | From Secrets Manager |
| `App__PublicOrigin` | `https://porchlight.example.com` | Your CloudFront domain. Used for the same-origin check and links in calendar files |
| `Auth__GoogleClientId` | `1234-abc.apps.googleusercontent.com` | |
| `Auth__BootstrapEmails__0` | `you@gmail.com` | Who can sign up without an invite (you, to create the first circle) |
| `Push__VapidSubject` | `mailto:you@gmail.com` | |
| `Push__VapidPublicKey` / `Push__VapidPrivateKey` | from `generate-vapid` | Private key from Secrets Manager |
| `Reminders__LeadMinutes` | `120` | |

## Deploying to AWS (pilot setup, done by hand)

Target cost: tens of dollars a month (db.t4g.micro RDS, smallest App Runner instance, S3 + CloudFront).

1. **Billing alarm.** In Billing → Budgets, create a monthly cost budget (say $40) with an email alert.
2. **Google OAuth client.** Google Cloud Console → APIs & Services:
   - OAuth consent screen: External, **Testing** mode. Add each dad's Gmail as a **test user** (this also gates who can sign in).
   - Credentials → Create OAuth client ID → Web application. Authorized JavaScript origins: your CloudFront domain and `http://localhost:5173`. No redirect URIs are needed.
3. **Database.** RDS → PostgreSQL 16, db.t4g.micro, 20 GB gp3, encryption at rest on (default), not publicly accessible. Store the connection string in Secrets Manager.
4. **API image.** Create an ECR repo `porch-light-api`, then build and push:

   ```bash
   docker build --platform linux/amd64 -t porch-light-api -f api/Dockerfile api
   ```

   Tag and push it to ECR following the repo's "View push commands".
5. **App Runner.** Create a service from the ECR image, port 8080, 0.25 vCPU / 0.5 GB. Health check path `/api/health`. Add the environment variables from the table above, referencing Secrets Manager for the connection string and VAPID private key. Add a VPC connector so it can reach RDS, and allow the connector's security group into the RDS security group on 5432. Migrations run on startup.
6. **Front end.** Create a private S3 bucket. Create a CloudFront distribution:
   - Default behavior → the S3 bucket (Origin Access Control). Attach [infra/cloudfront-spa-rewrite.js](infra/cloudfront-spa-rewrite.js) as a viewer-request CloudFront Function so app routes serve `index.html`. Don't use custom error pages for this, since they would also swallow API errors.
   - Behavior `/api/*` → origin = the App Runner domain (HTTPS only). Cache policy **CachingDisabled**, origin request policy **AllViewerExceptHostHeader**, allow all HTTP methods.
   - Serving the API under the same domain keeps the session cookie first-party, which iPhone Safari requires.
   - Optional: your own domain with an ACM certificate (us-east-1).
7. **Ship the web build:**

   ```bash
   scripts/deploy-web.sh <bucket> <distribution-id>
   ```

8. Sign in with a bootstrap email, create a circle, and invite everyone.

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
