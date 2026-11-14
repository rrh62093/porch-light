using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PorchLight.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Circle> Circles => Set<Circle>();
    public DbSet<CircleMember> CircleMembers => Set<CircleMember>();
    public DbSet<CircleRemoval> CircleRemovals => Set<CircleRemoval>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<ProfileItem> ProfileItems => Set<ProfileItem>();
    public DbSet<Endorsement> Endorsements => Set<Endorsement>();
    public DbSet<ChildProfile> ChildProfiles => Set<ChildProfile>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestOffer> RequestOffers => Set<RequestOffer>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealLeg> DealLegs => Set<DealLeg>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<UserMute> UserMutes => Set<UserMute>();
    public DbSet<Flag> Flags => Set<Flag>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.GoogleSubject).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
        });

        b.Entity<Circle>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CircleMember>(e =>
        {
            e.HasKey(x => new { x.CircleId, x.UserId });
            e.HasOne(x => x.Circle).WithMany(c => c.Members).HasForeignKey(x => x.CircleId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<CircleRemoval>(e => e.HasIndex(x => new { x.CircleId, x.UserId }));

        b.Entity<Invite>(e =>
        {
            e.HasIndex(x => new { x.Email, x.Status });
            e.HasOne(x => x.Circle).WithMany().HasForeignKey(x => x.CircleId);
            e.HasOne(x => x.InvitedBy).WithMany().HasForeignKey(x => x.InvitedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProfileItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Endorsement>(e =>
        {
            e.HasIndex(x => new { x.ProfileItemId, x.EndorsedById }).IsUnique();
            e.HasOne(x => x.ProfileItem).WithMany(p => p.Endorsements).HasForeignKey(x => x.ProfileItemId);
            e.HasOne(x => x.EndorsedBy).WithMany().HasForeignKey(x => x.EndorsedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ChildProfile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasOne(x => x.User).WithOne().HasForeignKey<ChildProfile>(x => x.UserId);
        });

        b.Entity<Request>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(140);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Circle).WithMany().HasForeignKey(x => x.CircleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CalledInLeg).WithMany().HasForeignKey(x => x.CalledInLegId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Offers).WithOne().HasForeignKey(o => o.RequestId);
            e.HasMany(x => x.Counters).WithOne(c => c.Request).HasForeignKey(c => c.RequestId);
            e.HasIndex(x => new { x.CircleId, x.Status });
            e.HasIndex(x => x.RecipientId);
            e.HasIndex(x => x.AuthorId);
        });

        b.Entity<Counter>(e =>
        {
            e.HasOne(x => x.Responder).WithMany().HasForeignKey(x => x.ResponderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SelectedOffer).WithMany().HasForeignKey(x => x.SelectedOfferId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.RequestId, x.ResponderId });
        });

        b.Entity<Deal>(e =>
        {
            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Responder).WithMany().HasForeignKey(x => x.ResponderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Legs).WithOne(l => l.Deal).HasForeignKey(l => l.DealId);
            // One deal per request; also guards against two dads accepting at the same moment.
            e.HasIndex(x => x.RequestId).IsUnique();
        });

        b.Entity<DealLeg>(e =>
        {
            e.HasOne(x => x.OwedBy).WithMany().HasForeignKey(x => x.OwedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OwedTo).WithMany().HasForeignKey(x => x.OwedToId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OwedById, x.Status });
            e.HasIndex(x => new { x.OwedToId, x.Status });
            e.HasIndex(x => new { x.ScheduledAt, x.ReminderSentAt });
        });

        b.Entity<PushSubscription>(e =>
        {
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<UserMute>(e => e.HasKey(x => new { x.UserId, x.MutedUserId }));

        b.Entity<Flag>(e =>
        {
            e.HasIndex(x => x.CircleId);
            e.HasOne(x => x.ReportedBy).WithMany().HasForeignKey(x => x.ReportedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AboutUser).WithMany().HasForeignKey(x => x.AboutUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
