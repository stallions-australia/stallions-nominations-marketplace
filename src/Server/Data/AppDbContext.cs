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
    public DbSet<MakeAnOfferListing> MakeAnOfferListings => Set<MakeAnOfferListing>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EnquiryMessage> EnquiryMessages => Set<EnquiryMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StudDirectory> StudDirectories => Set<StudDirectory>();
    public DbSet<StallionDirectory> StallionDirectories => Set<StallionDirectory>();
    public DbSet<TermsDocument> TermsDocuments => Set<TermsDocument>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<StallionSeasonSubscription> StallionSeasonSubscriptions => Set<StallionSeasonSubscription>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<ProcessedPaymentEvent> ProcessedPaymentEvents => Set<ProcessedPaymentEvent>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<IntroOffer> IntroOffers => Set<IntroOffer>();
    public DbSet<TaxDocument> TaxDocuments => Set<TaxDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.ObjectId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] <> ''"); // allow multiple empty-email rows (token may not carry email claim)
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(u => u.ObjectId).HasMaxLength(36).IsRequired();
            e.Property(u => u.PaymentCustomerId).HasMaxLength(255);
            e.Property(u => u.PaymentCustomerProvider).HasMaxLength(20);

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

            e.HasOne(f => f.StudDirectory)
                .WithMany()
                .HasForeignKey(f => f.StudDirectoryId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
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

            e.HasOne(s => s.StallionDirectory)
                .WithMany()
                .HasForeignKey(s => s.StallionDirectoryId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
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
            e.Property(l => l.CloseReason).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(l => l.BuyerFeeIncGst).HasPrecision(12, 2);

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
            e.Property(a => a.ReservePrice).HasPrecision(12, 2);
            e.Property(a => a.MinimumBidIncrement).HasPrecision(12, 2);

            e.HasOne(a => a.WinningBid)
                .WithMany()
                .HasForeignKey(a => a.WinningBidId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ── Make an Offer listings (TPT) ─────────────────────────────────────
        modelBuilder.Entity<MakeAnOfferListing>(e =>
        {
            e.ToTable("MakeAnOfferListings");
            e.HasOne(m => m.SourceAuctionListing)
                .WithMany()
                .HasForeignKey(m => m.SourceAuctionListingId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(m => m.SourceAuctionListingId);
        });

        // ── Offers ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Offer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.AmountIncGst).HasPrecision(12, 2);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.ConcurrencyStamp).IsConcurrencyToken();
            e.HasOne(o => o.Listing).WithMany(l => l.Offers).HasForeignKey(o => o.ListingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
            // One live offer per buyer per listing.
            e.HasIndex(o => new { o.ListingId, o.BuyerUserId }).IsUnique().HasFilter("[Status] = 'Pending'");
            e.HasIndex(o => new { o.Status, o.ExpiresAt });
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
            e.Property(p => p.BuyerFeeIncGst).HasPrecision(12, 2);
            e.Property(p => p.BuyerFeeExGst).HasPrecision(12, 2);
            e.Property(p => p.BuyerFeeGst).HasPrecision(12, 2);
            e.Property(p => p.BalancePayableToStudIncGst).HasPrecision(12, 2);
            e.Property(p => p.RefundAmount).HasPrecision(12, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.PaymentProvider).HasMaxLength(50);
            e.Property(p => p.PaymentReference).HasMaxLength(200);
            e.Property(p => p.LastChargeFailure).HasMaxLength(500);
            e.Property(p => p.ChargeCustomerId).HasMaxLength(255);
            e.Property(p => p.ChargePaymentMethodId).HasMaxLength(255);
            e.Property(p => p.ChargeDescription).HasMaxLength(300);
            e.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
            // One winning bid can never have two live sale records. Voided rows are excluded:
            // the removed interim checkout could leave several per bid on dev data.
            e.HasIndex(p => p.BidId).IsUnique().HasFilter("[BidId] IS NOT NULL AND [Status] <> 'Voided'");
            e.HasIndex(p => new { p.Status, p.ChargeDueBy });

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

            e.HasOne(p => p.Offer)
                .WithMany()
                .HasForeignKey(p => p.OfferId)
                .OnDelete(DeleteBehavior.NoAction);
            // One accepted offer can never have two live sale records.
            e.HasIndex(p => p.OfferId).IsUnique().HasFilter("[OfferId] IS NOT NULL AND [Status] <> 'Voided'");
        });

        // ── Enquiries ────────────────────────────────────────────────────────
        modelBuilder.Entity<Enquiry>(e =>
        {
            e.HasKey(eq => eq.Id);
            e.Property(eq => eq.Status).HasConversion<string>().HasMaxLength(20);

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

        // ── StudDirectory ────────────────────────────────────────────────────
        modelBuilder.Entity<StudDirectory>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(200).IsRequired();
            e.Property(d => d.Website).HasMaxLength(500);
            e.Property(d => d.Address).HasMaxLength(500);
            e.Property(d => d.Town).HasMaxLength(200);
            e.Property(d => d.State).HasMaxLength(100);
            e.Property(d => d.Country).HasMaxLength(100);
            e.Property(d => d.Phone).HasMaxLength(50);
            e.Property(d => d.Email).HasMaxLength(256);
            e.Property(d => d.LogoUrl).HasMaxLength(1000);
            e.HasIndex(d => d.ArionStudId);
        });

        // ── StallionDirectory ────────────────────────────────────────────────
        modelBuilder.Entity<StallionDirectory>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(200).IsRequired();
            e.Property(d => d.Colour).HasMaxLength(50);
            e.Property(d => d.Height).HasMaxLength(20);
            e.Property(d => d.SireName).HasMaxLength(200);
            e.Property(d => d.DamName).HasMaxLength(200);
            e.HasIndex(d => d.StallionId);
            e.HasIndex(d => d.ArionId);

            e.HasOne(d => d.StudDirectory)
                .WithMany(s => s.Stallions)
                .HasForeignKey(d => d.StudDirectoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TermsDocument ──────────────────────────────────────────────────────
        modelBuilder.Entity<TermsDocument>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Body).IsRequired();
            e.HasIndex(t => t.Version).IsUnique();
            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PlatformSettings (single row) ──────────────────────────────────────
        modelBuilder.Entity<PlatformSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.BuyerFeeIncGst).HasPrecision(12, 2);
            e.Property(s => s.StandardListingFeeIncGst).HasPrecision(12, 2);
            e.Property(s => s.MinimumBidIncrement).HasPrecision(12, 2);
            e.Property(s => s.BusinessLegalName).HasMaxLength(200);
            e.Property(s => s.BusinessAbn).HasMaxLength(11);
            e.Property(s => s.BusinessAddress).HasMaxLength(500);
            e.Property(s => s.BusinessEmail).HasMaxLength(256);
            e.Property(s => s.BankAccountName).HasMaxLength(200);
            e.Property(s => s.BankBsb).HasMaxLength(6);
            e.Property(s => s.BankAccountNumber).HasMaxLength(10);

            e.HasOne(s => s.UpdatedBy)
                .WithMany()
                .HasForeignKey(s => s.UpdatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            // The only place default fee amounts appear in code. Staff change them via api/settings.
            e.HasData(new PlatformSettings
            {
                Id = Entities.PlatformSettings.SingletonId,
                BuyerFeeIncGst = 150m,
                StandardListingFeeIncGst = 990m,
                MinimumBidIncrement = 25m,
                ChargeGracePeriodHours = 2,
                OfferExpiryDays = 7,
                InvoicePaymentTermsDays = 14,
                UpdatedAt = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        // ── StallionSeasonSubscriptions ───────────────────────────────────────
        modelBuilder.Entity<StallionSeasonSubscription>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.StallionId, s.SeasonId }).IsUnique();
            e.HasIndex(s => new { s.StudFarmId, s.SeasonId });
            e.Property(s => s.FeeIncGst).HasPrecision(12, 2);
            e.Property(s => s.FeeExGst).HasPrecision(12, 2);
            e.Property(s => s.GstAmount).HasPrecision(12, 2);
            e.Property(s => s.StandardFeeIncGst).HasPrecision(12, 2);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.PaymentReference).HasMaxLength(200);
            e.Property(s => s.WaiverReason).HasMaxLength(500);
            e.Property(s => s.Notes).HasMaxLength(1000);
            e.Property(s => s.PendingCheckoutUrl).HasMaxLength(2048);

            e.HasOne(s => s.Stallion)
                .WithMany()
                .HasForeignKey(s => s.StallionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.Season)
                .WithMany()
                .HasForeignKey(s => s.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.StudFarm)
                .WithMany()
                .HasForeignKey(s => s.StudFarmId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(s => s.CreatedBy)
                .WithMany()
                .HasForeignKey(s => s.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(s => s.IntroOffer)
                .WithMany()
                .HasForeignKey(s => s.IntroOfferId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── SavedCards (one per buyer) ────────────────────────────────────────
        modelBuilder.Entity<SavedCard>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.UserId).IsUnique();
            e.Property(c => c.Provider).HasMaxLength(20).IsRequired();
            e.Property(c => c.ProviderCustomerId).HasMaxLength(255).IsRequired();
            e.Property(c => c.ProviderPaymentMethodId).HasMaxLength(255).IsRequired();
            e.Property(c => c.Brand).HasMaxLength(30).IsRequired();
            e.Property(c => c.Last4).HasMaxLength(4).IsRequired();

            e.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProcessedPaymentEvents (idempotency) ──────────────────────────────
        modelBuilder.Entity<ProcessedPaymentEvent>(e =>
        {
            e.HasKey(p => p.EventId);
            e.Property(p => p.EventId).HasMaxLength(255);
            e.Property(p => p.Provider).HasMaxLength(20).IsRequired();
            e.Property(p => p.Type).HasMaxLength(100).IsRequired();
        });

        // ── Outbound emails (outbox) ─────────────────────────────────────────
        modelBuilder.Entity<OutboundEmail>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.ToAddress).HasMaxLength(320).IsRequired();
            e.Property(m => m.Subject).HasMaxLength(300).IsRequired();
            e.Property(m => m.Template).HasMaxLength(50).IsRequired();
            e.Property(m => m.RelatedEntityType).HasMaxLength(50);
            e.Property(m => m.LastError).HasMaxLength(1000);
            e.Property(m => m.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(m => new { m.SentAt, m.FailedAt, m.NextAttemptAt });
        });

        // ── Intro offers ─────────────────────────────────────────────────────
        modelBuilder.Entity<IntroOffer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(100).IsRequired();
            e.Property(o => o.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.AmountOffIncGst).HasPrecision(12, 2);
            e.HasIndex(o => new { o.IsRetired, o.EndsAt });
            e.HasOne(o => o.StudFarm).WithMany().HasForeignKey(o => o.StudFarmId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(o => o.Season).WithMany().HasForeignKey(o => o.SeasonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(o => o.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        // ── Tax documents (numbered from a sequence; never edited except Unpaid → Paid) ──
        modelBuilder.HasSequence<long>("TaxDocumentNumbers").StartsAt(1).IncrementsBy(1);
        modelBuilder.Entity<TaxDocument>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Number).HasMaxLength(20).IsRequired();
            e.HasIndex(d => d.Number).IsUnique();
            e.Property(d => d.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(d => d.Description).HasMaxLength(300).IsRequired();
            e.Property(d => d.StandardAmountIncGst).HasPrecision(12, 2);
            e.Property(d => d.IntroOfferName).HasMaxLength(100);
            e.Property(d => d.DiscountIncGst).HasPrecision(12, 2);
            e.Property(d => d.AmountIncGst).HasPrecision(12, 2);
            e.Property(d => d.AmountExGst).HasPrecision(12, 2);
            e.Property(d => d.GstAmount).HasPrecision(12, 2);
            e.Property(d => d.SupplierName).HasMaxLength(200).IsRequired();
            e.Property(d => d.SupplierAbn).HasMaxLength(11).IsRequired();
            e.Property(d => d.SupplierAddress).HasMaxLength(500).IsRequired();
            e.Property(d => d.SupplierEmail).HasMaxLength(256);
            e.Property(d => d.RecipientName).HasMaxLength(200).IsRequired();
            e.Property(d => d.RecipientEmail).HasMaxLength(320).IsRequired();
            e.Property(d => d.RecipientAbn).HasMaxLength(14);
            e.Property(d => d.RecipientAddress).HasMaxLength(500);
            e.Property(d => d.BankAccountName).HasMaxLength(200);
            e.Property(d => d.BankBsb).HasMaxLength(6);
            e.Property(d => d.BankAccountNumber).HasMaxLength(10);
            e.Property(d => d.Reason).HasMaxLength(600);
            e.Property(d => d.ConcurrencyStamp).IsConcurrencyToken();

            // One invoice per fee and one adjustment note per invoice — a second issue (race, retry) hits these.
            e.HasIndex(d => d.SubscriptionId).IsUnique().HasFilter("[Kind] = 'ListingFeeInvoice'");
            e.HasIndex(d => d.PurchaseId).IsUnique().HasFilter("[Kind] = 'BuyerFeeInvoice'");
            e.HasIndex(d => d.OriginalDocumentId).IsUnique().HasFilter("[Kind] = 'AdjustmentNote'");
            e.HasIndex(d => new { d.Kind, d.IssuedAt });
            e.HasIndex(d => d.StudFarmId);
            e.HasIndex(d => d.BuyerUserId);

            e.HasOne<StallionSeasonSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Purchase>().WithMany().HasForeignKey(d => d.PurchaseId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(d => d.OriginalDocument).WithMany().HasForeignKey(d => d.OriginalDocumentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<StudFarm>().WithMany().HasForeignKey(d => d.StudFarmId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(d => d.BuyerUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RenewConcurrencyStamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RenewConcurrencyStamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The original stamp is what EF checks against the database; the new one marks this update.
    private void RenewConcurrencyStamps()
    {
        foreach (var entry in ChangeTracker.Entries<IHasConcurrencyStamp>())
            if (entry.State == EntityState.Modified)
                entry.Entity.ConcurrencyStamp = Guid.NewGuid();
    }
}
