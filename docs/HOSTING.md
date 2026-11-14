# Porch Light: home server hosting brief

This brief is for whoever is building the hosting environment on the home Proxmox server. It covers what the app is, what it needs from the server, the split of responsibilities, and what "done" looks like.

## 1. What the app is

Porch Light is a small, invite-only web app (an installable PWA) where a few circles of dad friends trade skills, tools, and favors, including watching each other's kids. It keeps a ledger of who owes whom and sends phone notifications.

- **Users:** a pilot of roughly 10–30 people. Light traffic, a few requests per minute at peak.
- **Sensitive data:** names, emails, and children's details (ages, allergies, emergency contact numbers). Treat the database as sensitive personal data.
- **Availability goal:** best effort, but it must recover from a reboot or power cut without anyone touching it. An hour of downtime is tolerable; losing data is not.

## 2. How the app runs

The app is one container plus Postgres:

| Piece | Details |
| --- | --- |
| App container | ASP.NET Core 8. Serves the API under `/api/*` and the web front end for every other path. Listens on HTTP port **8080** inside the container. Health check: `GET /api/health` returns `{"ok":true}`. |
| Database | PostgreSQL 16. The app applies its own schema migrations on startup. |
| Background work | The app sends reminder notifications from an in-process timer, so it needs to stay running (no scale-to-zero). |
| Outbound calls | Google (to verify sign-in tokens) and the browser push services (Apple, Google, Mozilla) to deliver notifications. |

The app expects to be reached over **HTTPS on its own hostname**, at the root path (not a subpath), with the front end and API on the same origin. Google sign-in and push notifications both require HTTPS.

## 3. Who does what

**The app repo provides** (https://github.com/rrh62093/porch-light, folder `deploy/`):

- `deploy/Dockerfile`: builds one image containing the API and the web front end. Build context is the repo root.
- `deploy/compose.yml`: the app, Postgres, and `cloudflared`, with restart policies, a database health check, log rotation, and no published ports. Use it as is; if something needs to change, note why in the runbook.
- `deploy/.env.example`: every setting, with notes on how to generate each value.
- Database migrations, which run automatically when the app starts.
- Cache headers: the app already sends `Cache-Control: no-cache` for pages, the service worker, and the manifest, and long-lived `immutable` caching for `/assets/*`.

Start it from the `deploy/` folder with `docker compose up -d --build`. The Compose project name is `porchlight` (set in `.env`).

**The hosting environment provides** (your scope):

1. The VM and its OS, hardened and kept up to date.
2. Docker and Docker Compose, running the stack so it comes back after reboots.
3. Public HTTPS through **Cloudflare Tunnel**, with no ports opened on the home router.
4. Network isolation from the rest of the home network.
5. Secret storage on the VM.
6. Nightly offsite database backups, plus a tested restore procedure.
7. Uptime monitoring with alerts.
8. A simple, repeatable way to deploy new versions.
9. A short runbook (see section 6).

## 4. Requirements

### 4.1 VM

- Ubuntu Server 24.04 LTS, 2 vCPU, 4 GB RAM, 40 GB disk. Thin provisioning is fine.
- Encrypted disk if practical (Proxmox storage encryption or LUKS). If encryption would block unattended reboots, say so and we'll decide together.
- Proxmox: start at boot, QEMU guest agent installed, and a snapshot taken before any risky change.
- Proxmox-level VM backups (vzdump) are welcome, but they **do not replace** the offsite database backups below, since they live in the same house.
- Automatic security updates (`unattended-upgrades`), with reboots scheduled in a quiet window (for example 4 a.m.).
- Time sync on (reminders depend on correct time). Timezone UTC is fine; the app stores UTC.

### 4.2 Access

- SSH by key only. No password login, no root login.
- An admin user for you and a `deploy` user that can run Docker for app deployments. I'll provide a public key for the deploy user so the developer (Claude Code on the owner's Mac) can deploy.
- SSH is reachable from the home LAN only, never from the internet. Optional: Cloudflare Access or Tailscale for remote admin.

### 4.3 Network isolation

- Put the VM on its own VLAN or Proxmox bridge if the network gear supports it. Otherwise, use a host firewall (Proxmox firewall or `ufw` in the VM).
- **Inbound:** nothing from the internet. SSH from the admin LAN only.
- **Outbound:** the VM must not be able to reach other devices on the home LAN (except DNS and the gateway). It needs internet access for:
  - Cloudflare Tunnel: TCP/UDP 7844 to Cloudflare, plus 443.
  - HTTPS 443 for Google APIs (`www.googleapis.com`, `oauth2.googleapis.com`) and push services (`fcm.googleapis.com`, `web.push.apple.com`, `*.push.apple.com`, `updates.push.services.mozilla.com`).
  - Container registries (Docker Hub, `mcr.microsoft.com`), GitHub, Ubuntu updates, and the backup destination.

  Allowing all outbound 443 is acceptable; a strict allowlist is not required.

### 4.4 Containers

- Run the app, Postgres, and `cloudflared` with the provided `deploy/compose.yml` (one project, `restart: unless-stopped`).
- Postgres:
  - Image `postgres:16-alpine`.
  - Data on a named volume or a dedicated directory.
  - **Not published to any host port.** Only the app container can reach it, over the Compose network.
- The app's port 8080 should not be published either; `cloudflared` reaches it over the Compose network (`http://app:8080`). The app trusts forwarded headers from whatever proxies it, so it must only be reachable through the tunnel.
- Set Docker log rotation (for example `max-size: 10m`, `max-file: 3`) so logs can't fill the disk.

### 4.5 Cloudflare

- A domain on Cloudflare (the owner will provide it or buy one). The app gets a hostname such as `porchlight.<domain>`.
- A named Cloudflare Tunnel (token-based, running as the `cloudflared` container), routing that hostname to `http://app:8080`.
- Settings:
  - SSL/TLS: "Always Use HTTPS" on.
  - **Caching:** add a Cache Rule to bypass the cache for `/api/*`, `/sw.js`, `/index.html`, `/`, and `/manifest.webmanifest`. If the service worker or `index.html` get cached at the edge, phones won't pick up new versions. Caching `/assets/*` is fine, since those filenames are content-hashed.
  - **Bot protection:** Bot Fight Mode and any JS challenges must be off for this hostname, or at least for `/api/*`. The installed app calls the API with `fetch`, which can't solve challenges.
  - Leave Rocket Loader and any HTML rewriting off.
  - WebSockets are not needed.

### 4.6 App configuration and secrets

Copy `deploy/.env.example` to `deploy/.env` and fill it in. Keep it owned by the deploy user, mode `600`, and **never committed to git** (the repo's `.gitignore` already excludes it). The Compose file maps these short names onto the app's settings:

| Variable | Value |
| --- | --- |
| `.env` name | Value |
| --- | --- |
| `COMPOSE_PROJECT_NAME` | `porchlight` |
| `APP_TAG` | image tag; set per deploy (e.g. the git commit) so rollback is a tag change |
| `POSTGRES_PASSWORD` | generated (`openssl rand -base64 32 \| tr -d '/+='`) |
| `PUBLIC_ORIGIN` | `https://porchlight.<domain>` |
| `GOOGLE_CLIENT_ID` | from the Google Cloud console (the owner creates this) |
| `BOOTSTRAP_EMAIL` | the owner's email (lets them sign up before any invites exist) |
| `VAPID_SUBJECT` | `mailto:<owner email>` |
| `VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` | generated once with `docker run --rm porch-light generate-vapid` after building the image; **must never change** after users subscribe |
| `REMINDER_LEAD_MINUTES` | `120` |
| `TUNNEL_TOKEN` | the Cloudflare Tunnel token |

Dev sign-in is off in production (the app enforces this), and reminders run in the background inside the app.

Keep a copy of the `.env` contents (especially the database password and VAPID keys) in the owner's password manager, so the server can be rebuilt from scratch.

### 4.7 Backups

- A nightly `pg_dump` (custom format `-Fc`) of the `porchlight` database, run from a container or host cron.
- **Encrypted** before upload, for example with `age` or `gpg` using a key whose private half is **not** stored on the VM (it lives in the owner's password manager).
- Uploaded offsite. The default is an S3 bucket in the owner's existing AWS account, with versioning on and a lifecycle rule keeping 30 days. Use a dedicated IAM user that can only write to that bucket. Backblaze B2 is an acceptable alternative.
- Alert if a backup fails or hasn't run in 36 hours. A healthchecks.io ping at the end of the job works well and is free.
- **Restore test:** before go-live, restore the latest backup into a scratch Postgres container and confirm the tables have data. Document the exact commands.

### 4.8 Monitoring

- An external uptime check (UptimeRobot, Better Stack, healthchecks.io, or similar; free tier is fine) on `https://porchlight.<domain>/api/health` every 5 minutes, emailing the owner when it's down.
- Disk usage alert at 80% if easy; otherwise include disk checks in the runbook.

### 4.9 Deploying new versions

Keep it simple and scripted. Suggested: a `deploy.sh` on the VM that does the following:

1. `git pull` on a checkout of the repo.
2. Set `APP_TAG` to the new commit, then `docker compose up -d --build`.
3. Wait for `/api/health` to return OK and print the result.
4. Previous images stay tagged by commit, so rolling back means setting `APP_TAG` back and running `docker compose up -d` (without `--build`).

Building on the VM is fine at this scale. A future option is building in GitHub Actions and pulling from GHCR; not needed now.

Take a manual `pg_dump` before any deploy that includes a database migration. The deploy script can do this every time; it's small.

## 5. Acceptance checklist

The environment is ready when all of these are true:

- [ ] `https://porchlight.<domain>` loads the app with a valid certificate, from a phone on cellular data (off the home Wi‑Fi).
- [ ] `https://porchlight.<domain>/api/health` returns `{"ok":true}`.
- [ ] A port scan of the home public IP shows nothing new open.
- [ ] From inside the VM, the other home LAN devices (e.g. the router admin page, a laptop) are **not** reachable.
- [ ] Postgres and port 8080 aren't published on any host port (check with `docker compose ps` and `ss -tlnp`).
- [ ] Rebooting the VM, and separately the whole Proxmox host, brings the app back with no manual steps.
- [ ] A backup ran, landed encrypted in the offsite bucket, and was restored successfully into a scratch database.
- [ ] Stopping the app container triggers an uptime alert email within about 10 minutes.
- [ ] `deploy.sh` deploys a new commit, and a rollback was tried once.
- [ ] Response headers for `/sw.js` and `/api/me` show they were not served from Cloudflare's cache (`cf-cache-status` is not `HIT`).
- [ ] The `deploy` user can SSH in from the owner's Mac with the provided key and run the deploy script.

## 6. What to hand back

A short `RUNBOOK.md` (it can live in the repo under `deploy/`, **with no secrets in it**) that covers:

- **Layout:** VM name and ID in Proxmox, IP address, VLAN or bridge, and where the Compose project, `.env`, and backups script live.
- **Access:** how to SSH in, as which users.
- **Day-to-day commands:** how to deploy, roll back, view logs, and restart.
- **Backups:** how to run a backup by hand and how to restore one, with exact commands.
- **Monitoring:** where the uptime and backup alerts go, and how to silence them during maintenance.
- **Secrets:** which ones exist and where the master copies are kept (by name, not value).
- **Moving out:** how to migrate off this server (dump, restore elsewhere, repoint the tunnel or DNS).

## 7. Out of scope

- Kubernetes, multiple app instances, load balancers, or high availability. One VM is the design.
- Email sending (the app doesn't send email).
- File or photo storage (the app has no uploads).
- Changes to the app's code. If something in the app gets in the way of the hosting setup, write it down and pass it back rather than patching the app.

## 8. Decisions the owner still needs to make

- **Domain:** which one, and whether it's already on Cloudflare.
- **Backup destination:** S3 in the existing AWS account (default) or Backblaze B2.
- **Network isolation:** VLAN (if the switch and router support it) or firewall-only.
- **Remote admin:** whether SSH from outside the house is wanted (Tailscale or Cloudflare Access), or LAN only.
