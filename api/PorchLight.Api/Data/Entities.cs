namespace PorchLight.Api.Data;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Google "sub" claim. Null only for dev-login users.</summary>
    public string? GoogleSubject { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Always stored lower-case.</summary>
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Notification category toggles (all on by default).
    public bool NotifyNewRequests { get; set; } = true;
    public bool NotifyResponses { get; set; } = true;
    public bool NotifyDealUpdates { get; set; } = true;
    public bool NotifyReminders { get; set; } = true;
}

public enum CircleRole { Member = 0, Admin = 1 }

public class Circle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CircleMember> Members { get; set; } = new();
}

public class CircleMember
{
    public Guid CircleId { get; set; }
    public Circle? Circle { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public CircleRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>"Mute by circle": silence this circle's notifications for this member.</summary>
    public bool Muted { get; set; }
}

/// <summary>Record of an admin removing someone. Lets the other party close out shared IOUs.</summary>
public class CircleRemoval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CircleId { get; set; }
    public Guid UserId { get; set; }
    public Guid RemovedById { get; set; }
    public DateTimeOffset RemovedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum InviteStatus { Pending = 0, Accepted = 1, Revoked = 2, Declined = 3 }

public class Invite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CircleId { get; set; }
    public Circle? Circle { get; set; }
    /// <summary>Always stored lower-case.</summary>
    public string Email { get; set; } = "";
    public Guid InvitedById { get; set; }
    public User? InvitedBy { get; set; }
    public InviteStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ProfileItemKind { Skill = 0, Tool = 1, LookingFor = 2 }

/// <summary>A skill, tool, or thing the user is looking for.</summary>
public class ProfileItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public ProfileItemKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool AvailableForTrade { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Endorsement> Endorsements { get; set; } = new();
}

public class Endorsement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileItemId { get; set; }
    public ProfileItem? ProfileItem { get; set; }
    public Guid EndorsedById { get; set; }
    public User? EndorsedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Sensitive. Only returned to the owner and to the counterparty of an open deal that involves childcare.
/// Never log these fields.
/// </summary>
public class ChildProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    /// <summary>Visible to circles: "offers childcare".</summary>
    public bool OffersChildcare { get; set; }
    /// <summary>Visible to circles: "sometimes needs childcare".</summary>
    public bool NeedsChildcare { get; set; }
    public string? KidsAges { get; set; }
    public string? Allergies { get; set; }
    public string? ContactNumber { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum RequestStatus { Open = 0, InNegotiation = 1, Accepted = 2, Closed = 3 }

public class Request
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorId { get; set; }
    public User? Author { get; set; }
    /// <summary>Set when posted openly to a circle.</summary>
    public Guid? CircleId { get; set; }
    public Circle? Circle { get; set; }
    /// <summary>Set when sent directly to one dad.</summary>
    public Guid? RecipientId { get; set; }
    public User? Recipient { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset? NeededFrom { get; set; }
    public DateTimeOffset? NeededTo { get; set; }
    public RequestStatus Status { get; set; }
    public string? SuggestedRate { get; set; }
    /// <summary>The requester needs childcare; their child profile attaches to the deal.</summary>
    public bool IsChildcare { get; set; }
    public string? ChildcareNote { get; set; }
    /// <summary>Set when this request was created by calling in an IOU.</summary>
    public Guid? CalledInLegId { get; set; }
    public DealLeg? CalledInLeg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<RequestOffer> Offers { get; set; } = new();
    public List<Counter> Counters { get; set; } = new();
}

/// <summary>Something the requester will give in exchange. A request has many.</summary>
public class RequestOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public string Description { get; set; } = "";
    /// <summary>The offer is childcare (the requester would watch the responder's kids).</summary>
    public bool IsChildcare { get; set; }
    public int SortOrder { get; set; }
}

public enum CounterKind { Accept = 0, Counter = 1, Decline = 2 }
public enum CounterStatus { Pending = 0, Accepted = 1, Declined = 2, Superseded = 3 }

/// <summary>
/// One message in a negotiation thread. A thread is (request, responder). Either side can counter;
/// the other side accepts the latest pending counter. A responder's "Accept" forms the deal immediately.
/// </summary>
public class Counter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }
    /// <summary>The non-author party of the thread.</summary>
    public Guid ResponderId { get; set; }
    public User? Responder { get; set; }
    public Guid AuthorId { get; set; }
    public User? Author { get; set; }
    public CounterKind Kind { get; set; }
    public Guid? SelectedOfferId { get; set; }
    public RequestOffer? SelectedOffer { get; set; }
    /// <summary>Custom terms in place of a listed offer, e.g. "two light fixtures instead of one".</summary>
    public string? CustomText { get; set; }
    /// <summary>The requester's side becomes a loose "you owe me one".</summary>
    public bool Loose { get; set; }
    public bool CustomIsChildcare { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? ProposedTime { get; set; }
    public CounterStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum DealStatus { Open = 0, Completed = 1, Cancelled = 2 }

public class Deal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }
    public Guid RequesterId { get; set; }
    public User? Requester { get; set; }
    public Guid ResponderId { get; set; }
    public User? Responder { get; set; }
    public DealStatus Status { get; set; }
    public bool InvolvesChildcare { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DealLeg> Legs { get; set; } = new();
}

public enum LegKind { Specific = 0, Loose = 1 }
public enum LegStatus { Open = 0, CalledIn = 1, AwaitingConfirmation = 2, Completed = 3, ClosedOut = 4 }

/// <summary>One side of a deal: who owes whom what. Open legs are IOUs.</summary>
public class DealLeg
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DealId { get; set; }
    public Deal? Deal { get; set; }
    public Guid OwedById { get; set; }
    public User? OwedBy { get; set; }
    public Guid OwedToId { get; set; }
    public User? OwedTo { get; set; }
    public string Description { get; set; } = "";
    public LegKind Kind { get; set; }
    public LegStatus Status { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public int ScheduleSequence { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    /// <summary>Date the favor happened. Required to close a leg; defaults to today.</summary>
    public DateOnly? CompletedOn { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CalledInRequestId { get; set; }
}

public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>"Mute by person": private, and their requests still appear in the feed.</summary>
public class UserMute
{
    public Guid UserId { get; set; }
    public Guid MutedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Flag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CircleId { get; set; }
    public Guid ReportedById { get; set; }
    public User? ReportedBy { get; set; }
    public Guid? AboutUserId { get; set; }
    public User? AboutUser { get; set; }
    public string Message { get; set; } = "";
    public bool Resolved { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
