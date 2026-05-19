using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudFarm> StudFarms => Set<StudFarm>();
    public DbSet<Stallion> Stallions => Set<Stallion>();
    public DbSet<StallionImage> StallionImages => Set<StallionImage>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<AuctionListing> AuctionListings => Set<AuctionListing>();
    public DbSet<FixedPriceListing> FixedPriceListings => Set<FixedPriceListing>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<NominationBinding> NominationBindings => Set<NominationBinding>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EnquiryMessage> EnquiryMessages => Set<EnquiryMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.EntraObjectId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(u => u.EntraObjectId).HasMaxLength(36).IsRequired();

            e.HasOne(u => u.VerifiedBy)
                .WithMany()
                .HasForeignKey(u => u.VerifiedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── StudFarms ────────────────────────────────────────────────────────
        modelBuilder.Entity<StudFarm>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.UserId).IsUnique();
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.ABN).HasMaxLength(14);
            e.Property(f => f.ContactPhone).HasMaxLength(20);
            e.Property(f => f.ContactEmail).HasMaxLength(256);
            e.Property(f => f.Address).HasMaxLength(500);

            e.HasOne(f => f.User)
                .WithOne(u => u.StudFarm)
                .HasForeignKey<StudFarm>(f => f.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Stallions ────────────────────────────────────────────────────────
        modelBuilder.Entity<Stallion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.Colour).HasMaxLength(50);
            e.Property(s => s.Sire).HasMaxLength(200);
            e.Property(s => s.Dam).HasMaxLength(200);
            e.Property(s => s.RegistrationNumber).HasMaxLength(100);

            e.HasOne(s => s.StudFarm)
                .WithMany(f => f.Stallions)
                .HasForeignKey(s => s.StudFarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── StallionImages ───────────────────────────────────────────────────
        modelBuilder.Entity<StallionImage>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.BlobPath).HasMaxLength(500).IsRequired();

            e.HasOne(i => i.Stallion)
                .WithMany(s => s.Images)
                .HasForeignKey(i => i.StallionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seasons ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Season>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).HasMaxLength(100).IsRequired();

            e.HasOne(s => s.OpenedBy)
                .WithMany()
                .HasForeignKey(s => s.OpenedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── Listings (TPT base) ──────────────────────────────────────────────
        modelBuilder.Entity<Listing>(e =>
        {
            e.ToTable("Listings");
            e.HasKey(l => l.Id);
            e.Property(l => l.ListingType).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.PlatformFeePercent).HasPrecision(5, 2);

            e.HasIndex(l => new { l.Status, l.SeasonId });
            e.HasIndex(l => new { l.StudFarmId, l.Status });

            e.HasOne(l => l.Stallion)
                .WithMany(s => s.Listings)
                .HasForeignKey(l => l.StallionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.Season)
                .WithMany(s => s.Listings)
                .HasForeignKey(l => l.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.StudFarm)
                .WithMany(f => f.Listings)
                .HasForeignKey(l => l.StudFarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AuctionListings (TPT child) ──────────────────────────────────────
        modelBuilder.Entity<AuctionListing>(e =>
        {
            e.ToTable("AuctionListings");
            e.Property(a => a.StartingPrice).HasPrecision(12, 2);
            e.Property(a => a.ReservePrice).HasPrecision(12, 2);
            e.Property(a => a.MinimumBidIncrement).HasPrecision(12, 2);

            e.HasOne(a => a.WinningBid)
                .WithMany()
                .HasForeignKey(a => a.WinningBidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── FixedPriceListings (TPT child) ───────────────────────────────────
        modelBuilder.Entity<FixedPriceListing>(e =>
        {
            e.ToTable("FixedPriceListings");
            e.Property(f => f.PriceIncGst).HasPrecision(12, 2);
        });

        // ── Bids ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Bid>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.AmountIncGst).HasPrecision(12, 2);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(b => new { b.AuctionListingId, b.AmountIncGst });

            e.HasOne(b => b.AuctionListing)
                .WithMany(a => a.Bids)
                .HasForeignKey(b => b.AuctionListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(b => b.Buyer)
                .WithMany(u => u.Bids)
                .HasForeignKey(b => b.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Purchases ────────────────────────────────────────────────────────
        modelBuilder.Entity<Purchase>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.TotalPriceIncGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeIncGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeExGst).HasPrecision(12, 2);
            e.Property(p => p.PlatformFeeGst).HasPrecision(12, 2);
            e.Property(p => p.RefundAmount).HasPrecision(12, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.MareName).HasMaxLength(200).IsRequired();
            e.Property(p => p.MareRegistration).HasMaxLength(100);
            e.Property(p => p.MareBreed).HasMaxLength(100);
            e.Property(p => p.PaymentProvider).HasMaxLength(50);
            e.Property(p => p.PaymentReference).HasMaxLength(200);

            e.HasOne(p => p.Listing)
                .WithMany(l => l.Purchases)
                .HasForeignKey(p => p.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Buyer)
                .WithMany(u => u.Purchases)
                .HasForeignKey(p => p.BuyerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Bid)
                .WithMany()
                .HasForeignKey(p => p.BidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── NominationBindings ───────────────────────────────────────────────
        modelBuilder.Entity<NominationBinding>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.PurchaseId).IsUnique();
            e.Property(n => n.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(n => n.PdfBlobPath).HasMaxLength(500);

            e.HasOne(n => n.Purchase)
                .WithOne(p => p.NominationBinding)
                .HasForeignKey<NominationBinding>(n => n.PurchaseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(n => n.AcknowledgedBy)
                .WithMany()
                .HasForeignKey(n => n.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── Enquiries ────────────────────────────────────────────────────────
        modelBuilder.Entity<Enquiry>(e =>
        {
            e.HasKey(eq => eq.Id);
            e.Property(eq => eq.Status).HasMaxLength(20);

            e.HasOne(eq => eq.Listing)
                .WithMany(l => l.Enquiries)
                .HasForeignKey(eq => eq.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(eq => eq.Buyer)
                .WithMany(u => u.Enquiries)
                .HasForeignKey(eq => eq.BuyerUserId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(eq => eq.StudFarmUser)
                .WithMany()
                .HasForeignKey(eq => eq.StudFarmUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── EnquiryMessages ──────────────────────────────────────────────────
        modelBuilder.Entity<EnquiryMessage>(e =>
        {
            e.HasKey(m => m.Id);

            e.HasOne(m => m.Enquiry)
                .WithMany(eq => eq.Messages)
                .HasForeignKey(m => m.EnquiryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── AuditLog ─────────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).ValueGeneratedOnAdd();
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.Property(a => a.Action).HasMaxLength(100).IsRequired();

            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.OccurredAt);

            e.HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
