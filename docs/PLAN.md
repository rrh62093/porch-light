# Dad Skills Swap App: Implementation Plan

*Working title: Porch Light. Planning conversation with Rob, November 2026. Written in plain markdown so it can be exported, versioned in a repo, or pasted into any tool.*

## 1. Vision

A mobile-first web app where dads build invite-only friend circles and trade skills, tools, and favors, including kid-watching. The origin: watching a friend's kids for about $20 an hour, or trading it for help hanging a light fixture. Every circle is made of real friends, so trust is built in.

The first release is a pilot with Rob and his dad friends only. Wider release, and the legal review that comes with it (terms of service, childcare liability), is deliberately deferred.

## 2. Product Decisions (Locked)

### Circles

- Invite-only. A dad can belong to multiple circles (for example neighborhood dads and work friends).
- The circle creator is an admin and can promote others. For the pilot, only admins can invite.
- Admin tools are minimal: remove a member, and members can send a private flag to an admin.

### Profiles

- Skills, tools, and things you're looking for.
- Childcare metadata lives on the profile, not on each request: kids' ages, allergies, contact number. A childcare request attaches it automatically, with an optional per-request note.
- Kid details are visible only to the other dad on an accepted childcare deal, never to the whole circle. Before a deal, the circle only sees that you offer or need childcare.

### Requests and offers

- A request can go directly to one dad, or be posted openly to a circle (for example, needing a 16-foot-plus ladder when you don't know who has one).
- A request lists several things the requester would give in exchange, such as childcare or hanging a light fixture.
- A responder can pick one offer, accept as-is, or counter (for example, asking for two light fixtures instead of one).
- No fixed currency or credit system. Each deal sets its own terms. An optional suggested rate is allowed.

### Deals and IOUs

- Accepted deals are tracked to completion. A scheduled date and time is optional while a deal is open, but closing a deal requires a completion date, which defaults to today, so there is always a record of when each favor happened. IOUs usually have no date until they are called in.
- A deal has two legs. One leg can be fulfilled now while the other stays open, possibly for months.
- An IOU can be specific (hang one light fixture) or loose ("you owe me one"), depending on how the deal was struck.
- The holder of an IOU can call it in at any time. Calling it in creates a new request back to that dad, who can accept or counter on timing.
- The app is effectively a ledger of favors owed to me and favors I owe.
- IOUs outlive circle membership. If a dad leaves a circle, the IOU stays open and can still be followed up. If an admin removes a dad, the other dad can choose to close out the IOU instead of dealing with him.
- Scheduled deals can be exported as standard ICS calendar files so they land on people's calendars. A rescheduled deal needs a regenerated file; a subscription feed could replace this later.

### Endorsements

- No overall ratings of dads, since they could turn toxic.
- LinkedIn-style endorsements of a specific skill or tool, positive only.
- Any circle member can endorse; no completed deal is required.
- After a deal completes, the app gently prompts "want to endorse Mike's electrical work?" as a nudge, not a requirement.

### Out of scope for v1

- Photos (text only for now; S3 is already in place when you add them).
- Credits or points economy.
- Native iOS and Android apps.
- Public discovery or open signup.

## 3. User Experience

Mobile-first with a bottom tab bar, like a native app.

- **Tab 1, Requests:** open requests from the user's circles.
- **Tab 2, I Owe:** favors the user owes.
- **Tab 3, Owed to Me:** favors owed to the user.
- **Tab 4, Circles:** circle list, members, invites (admins), profile, and settings.
- **Post Request button:** a prominent center action or floating button, since posting is the main action.

Other key screens: request detail with offers and counters thread, deal detail with the two legs and completion status, profile with skills, tools, endorsements, and childcare metadata.

### Notifications

Notifications are layered, with everything on by default.

- **Category toggles:** new requests in my circles, responses to my requests (offers and counters), deal updates (accepted, completed, IOU called in), and reminders for scheduled deals. New requests can be turned off entirely since they could be chatty.
- **Mute by circle:** silence notifications from one circle without leaving it.
- **Mute by person:** silence one dad's notifications. The mute is private, and his requests still appear in the feed.

### Look and feel

The app should be easy to use, understated, and clear at a glance about what is going on. Nothing flashy or over the top.

- **Logo:** an understated but clever porch light, simple enough to work as a home-screen icon.
- **Palette (my suggestion, not yet confirmed):** neutral base colors such as warm grays and off-whites, with one warm amber accent echoing a lit porch light, used sparingly for primary actions and unread items.
- **Layout:** plain, high-contrast text, generous tap targets for thumb use, and status always visible: what is open, what I owe, what is owed to me.
- **Tone of copy:** friendly and short, never cute at the expense of clarity.
- **Design approach:** start from a clean, well-known component library rather than custom design, since Rob is not a designer and the pilot needs to stay lean.

## 4. Architecture (Simple and Cheap, on AWS)

The goal for the pilot is the simplest thing that works, at roughly tens of dollars a month, with room to scale without a rewrite.

- **Front end:** a progressive web app (installable to the home screen, with push notifications), hosted as static files on S3 behind CloudFront.
- **API:** ASP.NET Core (C#) running on AWS App Runner.
- **Database:** PostgreSQL on a small RDS instance. The data is highly relational, so SQL fits well.
- **Auth:** Google sign-in directly, no Cognito for the pilot. Create an OAuth client in Google Cloud, keep the consent screen in testing mode, and add the dad friends as named test users to avoid app verification. The API verifies Google's ID token and issues its own session.
- **Push notifications:** standard Web Push (VAPID keys) sent from the API. No AWS service is required; SNS can help with fan-out later.
- **Secrets and config:** AWS Secrets Manager or Parameter Store for database credentials and VAPID keys.

### Push notification caveat

On iPhone, web push works only after the user adds the site to the home screen and taps Allow on the permission prompt. Android is more forgiving. For the pilot, Rob walks his friends through the install.

## 5. Data Model (First Draft)

Plain-language entities and their relationships.

- **User:** id, Google subject id, name, email, created at.
- **Circle:** id, name, created by.
- **CircleMember:** circle id, user id, role (admin or member), joined at.
- **Invite:** circle id, invited email, invited by, status, created at.
- **Skill / Tool:** id, user id, kind (skill or tool), name, description, available for trade.
- **Endorsement:** id, skill or tool id, endorsed by user id, created at. Unique per endorser per item.
- **ChildProfile:** user id, kids' ages, allergies, contact number, notes. Access-controlled; see visibility rules above.
- **Request:** id, author, circle id (or direct recipient id), title, description, needed time window, status (open, in negotiation, accepted, closed), suggested rate (optional).
- **RequestOffer:** id, request id, description of what the requester will give (one request has many).
- **Counter:** id, request id, author, selected offer id or custom text, message, status, created at. Forms the negotiation thread.
- **Deal:** id, request id, the two parties, status, created at.
- **DealLeg:** id, deal id, owed by, owed to, description, kind (specific or loose), status (open, called in, completed), completed at. Two per deal.
- **PushSubscription:** user id, endpoint, keys, created at.
- **Flag:** id, circle id, reported by, about user (optional), message, created at.

### Key rules to enforce in the API

- A user can only see requests in circles they belong to, or direct requests addressed to them.
- ChildProfile is returned only to the counterparty of an accepted childcare deal, and to the owner.
- Only admins can create invites, promote members, or remove members.
- Calling in an IOU creates a new Request addressed directly to the debtor.

## 6. Phased Build Order

### Phase 0: Foundations

- AWS account setup, billing alarm, and a simple infrastructure-as-code approach (Terraform or CDK) or manual setup documented in a README.
- Repo structure: API project, front-end project, shared docs.
- Google OAuth client created with test users.
- RDS Postgres instance and migrations tooling (EF Core migrations).

### Phase 1: Identity and circles

- Google sign-in end to end, with a session from the API.
- Create a circle, admin invites by email, accept invite, member list.
- PWA shell with the four-tab bottom bar, manifest, and service worker.

### Phase 2: Profiles

- Skills and tools CRUD on the profile.
- Endorsements.
- Childcare metadata section with the visibility rules.

### Phase 3: Requests and negotiation

- Post a request (open to a circle or direct to a dad) with multiple offers.
- Respond: accept, pick an offer, or counter. Negotiation thread.
- Request feed on the Requests tab.

### Phase 4: Deals and IOUs

- Accepting creates a Deal with two legs.
- Mark legs complete (confirmed by the counterparty).
- I Owe and Owed to Me tabs. Call in an IOU, which generates a new direct request.
- Post-deal endorsement prompt.

### Phase 5: Notifications and admin

- Web Push subscription and sending for new requests, counters, accepted deals, and called-in IOUs.
- Admin remove member and private flag.

### Phase 6: Pilot launch

- Walk the dad friends through installing to the home screen and allowing notifications.
- Collect feedback for two to four weeks before deciding what comes next.

## 7. Risks and Open Items

- **Legal and liability:** paid or bartered childcare carries real liability. Fine for a friends pilot; get a lawyer to review terms before any wider release. This document is not legal advice.
- **iPhone push friction:** the home-screen install requirement may confuse some users. Plan a short install guide.
- **Child data sensitivity:** treat allergies and kids' ages as sensitive. Encrypt at rest (RDS default), restrict access in the API, and avoid logging them.
- **Google test-user limit:** testing mode caps the number of named test users, which is fine for a pilot but means verification is needed before growth.
- **N**ame check still to do: Porch Light is the working name, so confirm domain and app store availability before committing. Toolshed was dropped because similar tool-sharing apps already use it; Chit and Tab were the runner-up names. Still undecided: branding and visual identity, and the exact reminder timing for scheduled deals.

## 8. Moving This Plan Around

This document is plain markdown with no embedded tooling, so it can be exported from the doc as markdown, committed to the repo as `docs/PLAN.md`, or pasted into a new chat to resume the build. When you come back to start building, hand this document over first and start at Phase 0.
