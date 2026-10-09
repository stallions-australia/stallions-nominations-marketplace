-- ============================================================
-- Development seed data for Stallions Nominations Marketplace
-- Run against the local dev database (StallionsNomsDev)
-- Safe to re-run: uses IF NOT EXISTS / MERGE patterns
--
-- Run AFTER the EF migrations (it reads fee amounts from the seeded PlatformSettings row,
-- so no fee amount is repeated here).
--
-- IMPORTANT: EF Core stores all enums as strings in this project.
--   ListingType:        'Auction'
--   ListingStatus:      'Draft', 'Active', 'Sold', 'Expired', 'Cancelled'
--   UserRole:           'Buyer', 'StudFarmAdmin', 'Staff'
--   UserStatus:         'PendingVerification', 'Active', 'Suspended'
--   SubscriptionStatus: 'Pending', 'Paid', 'Waived'
--   PaymentMethod:      'Card', 'Invoice', 'BankTransfer', 'Waived'
-- ============================================================

SET XACT_ABORT ON;  -- any error rolls back the whole seed
BEGIN TRANSACTION;

-- ── Fix any previously inserted rows that used integer values ────
-- (Safe no-op if rows don't exist or already have string values)
UPDATE Listings SET ListingType = 'Auction'    WHERE ListingType = '1';
UPDATE Listings SET Status = 'Draft'     WHERE Status = '0';
UPDATE Listings SET Status = 'Active'    WHERE Status = '1';
UPDATE Listings SET Status = 'Sold'      WHERE Status = '2';
UPDATE Listings SET Status = 'Expired'   WHERE Status = '3';
UPDATE Listings SET Status = 'Cancelled' WHERE Status = '4';
UPDATE Users SET Role = 'Buyer'         WHERE Role = '0';
UPDATE Users SET Role = 'StudFarmAdmin' WHERE Role = '1';
UPDATE Users SET Role = 'Staff'         WHERE Role = '2';
UPDATE Users SET Status = 'PendingVerification' WHERE Status = '0';
UPDATE Users SET Status = 'Active'     WHERE Status = '1';
UPDATE Users SET Status = 'Suspended'  WHERE Status = '2';

-- ── Seasons ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM Seasons WHERE Name = '2025 Season')
BEGIN
    INSERT INTO Seasons (Id, Name, StartDate, EndDate, IsOpen, CreatedAt)
    VALUES (
        '11111111-0000-0000-0000-000000000001',
        '2025 Season',
        '2025-08-01', '2026-01-31',
        1, GETUTCDATE()
    );
END

-- ── Users (stub stud farm admins) ───────────────────────────
-- Placeholder rows so the FK from StudFarms → Users is satisfied.
-- ObjectId values are fake GUIDs; replace with real Entra OIDs
-- once you have test user accounts.

IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = '00000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO Users (Id, ObjectId, Email, DisplayName, Role, Status, CreatedAt)
    VALUES (
        '00000000-0000-0000-0000-000000000001',
        'aaaaaaaa-0000-0000-0000-000000000001',
        'coolmore-admin@dev.local',
        'Coolmore Admin (Dev)',
        'StudFarmAdmin', 'Active', GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = '00000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO Users (Id, ObjectId, Email, DisplayName, Role, Status, CreatedAt)
    VALUES (
        '00000000-0000-0000-0000-000000000002',
        'aaaaaaaa-0000-0000-0000-000000000002',
        'arrowfield-admin@dev.local',
        'Arrowfield Admin (Dev)',
        'StudFarmAdmin', 'Active', GETUTCDATE()
    );
END

-- ── Stud Farms ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM StudFarms WHERE Name = 'Coolmore Australia (Dev)')
BEGIN
    INSERT INTO StudFarms (Id, UserId, Name, ABN, ContactPhone, ContactEmail, Address, IsActive, CreatedAt)
    VALUES (
        '22222222-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000001',
        'Coolmore Australia (Dev)',
        '12 345 678 901',
        '02 4998 6700',
        'info@coolmoreaustralia.com.au',
        'Jerrys Plains NSW 2330',
        1, GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT 1 FROM StudFarms WHERE Name = 'Arrowfield Stud (Dev)')
BEGIN
    INSERT INTO StudFarms (Id, UserId, Name, ABN, ContactPhone, ContactEmail, Address, IsActive, CreatedAt)
    VALUES (
        '22222222-0000-0000-0000-000000000002',
        '00000000-0000-0000-0000-000000000002',
        'Arrowfield Stud (Dev)',
        '98 765 432 109',
        '02 6545 3000',
        'info@arrowfield.com.au',
        'Scone NSW 2337',
        1, GETUTCDATE()
    );
END

-- ── Stallions ────────────────────────────────────────────────
DECLARE @CoolmoreId UNIQUEIDENTIFIER = '22222222-0000-0000-0000-000000000001';
DECLARE @ArrowfieldId UNIQUEIDENTIFIER = '22222222-0000-0000-0000-000000000002';
DECLARE @SeasonId UNIQUEIDENTIFIER = '11111111-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Fastnet Rock (Dev)')
BEGIN
    DECLARE @FastnetId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000001';
    INSERT INTO Stallions (Id, StudFarmId, Name, YearOfBirth, IsActive, CreatedAt)
    VALUES (@FastnetId, @CoolmoreId, 'Fastnet Rock (Dev)', 2001, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @FastnetId,
        'https://images.unsplash.com/photo-1553284966-19b8815c7817?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Snitzel (Dev)')
BEGIN
    DECLARE @SnitzelId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000002';
    INSERT INTO Stallions (Id, StudFarmId, Name, YearOfBirth, IsActive, CreatedAt)
    VALUES (@SnitzelId, @ArrowfieldId, 'Snitzel (Dev)', 2002, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @SnitzelId,
        'https://images.unsplash.com/photo-1598974357801-cbca100e65d3?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'So You Think (Dev)')
BEGIN
    DECLARE @SYTId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000003';
    INSERT INTO Stallions (Id, StudFarmId, Name, YearOfBirth, IsActive, CreatedAt)
    VALUES (@SYTId, @CoolmoreId, 'So You Think (Dev)', 2007, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @SYTId,
        'https://images.unsplash.com/photo-1534113534176-3b5d5a2c4b9c?w=800&q=80',
        1, 1, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM Stallions WHERE Name = 'Deep Impact (Dev)')
BEGIN
    DECLARE @DeepImpactId UNIQUEIDENTIFIER = '33333333-0000-0000-0000-000000000004';
    INSERT INTO Stallions (Id, StudFarmId, Name, YearOfBirth, IsActive, CreatedAt)
    VALUES (@DeepImpactId, @ArrowfieldId, 'Deep Impact (Dev)', 2002, 1, GETUTCDATE());

    INSERT INTO StallionImages (Id, StallionId, BlobPath, IsPrimary, DisplayOrder, UploadedAt)
    VALUES (NEWID(), @DeepImpactId,
        'https://images.unsplash.com/photo-1489391722045-1e6f39ca7c49?w=800&q=80',
        1, 1, GETUTCDATE());
END

-- ── Fee settings (seeded by the V2Phase1Domain migration) ────
-- Listings snapshot the buyer fee and bid increment from these, exactly as publishing does.
DECLARE @BuyerFee     DECIMAL(12,2) = (SELECT BuyerFeeIncGst           FROM PlatformSettings);
DECLARE @Increment    DECIMAL(12,2) = (SELECT MinimumBidIncrement      FROM PlatformSettings);
DECLARE @ListingFee   DECIMAL(12,2) = (SELECT StandardListingFeeIncGst FROM PlatformSettings);
DECLARE @ListingFeeGst DECIMAL(12,2) = ROUND(@ListingFee / 11, 2);
IF @BuyerFee IS NULL
    THROW 50000, 'PlatformSettings row missing - run the EF migrations before this seed.', 1;

-- ── Listing fee subscriptions ────────────────────────────────
-- Paid for every seeded stallion in the open season so dev listings can be published.
-- Created by the first Staff user if there is one, otherwise the Coolmore stub admin.
DECLARE @SeedCreatorId UNIQUEIDENTIFIER = COALESCE(
    (SELECT TOP 1 Id FROM Users WHERE Role = 'Staff' ORDER BY CreatedAt),
    '00000000-0000-0000-0000-000000000001');

INSERT INTO StallionSeasonSubscriptions (Id, StallionId, SeasonId, StudFarmId,
    FeeIncGst, FeeExGst, GstAmount, Status, PaymentMethod, PaymentReference, PaidAt,
    Notes, CreatedAt, CreatedByUserId)
SELECT NEWID(), s.Id, @SeasonId, s.StudFarmId,
    @ListingFee, @ListingFee - @ListingFeeGst, @ListingFeeGst,
    'Paid', 'Invoice', 'DEV-SEED', GETUTCDATE(),
    'Dev seed data', GETUTCDATE(), @SeedCreatorId
FROM Stallions s
WHERE s.Id IN ('33333333-0000-0000-0000-000000000001', '33333333-0000-0000-0000-000000000002',
               '33333333-0000-0000-0000-000000000003', '33333333-0000-0000-0000-000000000004')
  AND NOT EXISTS (SELECT 1 FROM StallionSeasonSubscriptions x
                  WHERE x.StallionId = s.Id AND x.SeasonId = @SeasonId);

-- ── Auction Listings ─────────────────────────────────────────
-- So You Think: ending in ~4 hours (tests "ending soon" styling), hidden reserve
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        BuyerFeeIncGst, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000004',
        '33333333-0000-0000-0000-000000000003', @SeasonId, @CoolmoreId,
        'Auction', 'Active', @BuyerFee, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000004',
        12000.00, 0, @Increment,
        DATEADD(hour, 4, GETUTCDATE()));
END

-- Deep Impact: 5 days remaining, no reserve
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        BuyerFeeIncGst, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000005',
        '33333333-0000-0000-0000-000000000004', @SeasonId, @ArrowfieldId,
        'Auction', 'Active', @BuyerFee, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000005',
        NULL, 1, @Increment,
        DATEADD(day, 5, GETUTCDATE()));
END

-- Fastnet Rock: draft auction with a reserve, ready to publish (its stallion's fee is Paid above)
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000006')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        TermsAndConditions, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000006',
        '33333333-0000-0000-0000-000000000001', @SeasonId, @CoolmoreId,
        'Auction', 'Draft', 'Dev seed terms: 45-day payment on live foal.', GETUTCDATE());

    INSERT INTO AuctionListings (Id, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000006',
        20000.00, 0, @Increment,
        DATEADD(day, 7, GETUTCDATE()));
END

COMMIT;

PRINT 'Seed data inserted/updated successfully.';
PRINT 'NOTE: Update the placeholder ObjectId values in Users once you have real Entra OIDs.';
