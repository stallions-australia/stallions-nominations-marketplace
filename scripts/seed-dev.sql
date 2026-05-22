-- ============================================================
-- Development seed data for Stallions Nominations Marketplace
-- Run against the local dev database (stallions_dev)
-- Safe to re-run: uses IF NOT EXISTS / MERGE patterns
--
-- Enum values (stored as int in DB):
--   ListingType:   FixedPrice=0, Auction=1
--   ListingStatus: Draft=0, Active=1, Sold=2, Expired=3, Cancelled=4
-- ============================================================

BEGIN TRANSACTION;

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
-- These are placeholder rows so the FK from StudFarms → Users is satisfied.
-- EntraObjectId values are fake GUIDs; replace them with the real Entra OIDs
-- for your test accounts once you know them.
--   UserRole:   Buyer=0, StudFarmAdmin=1, Staff=2
--   UserStatus: PendingVerification=0, Active=1, Suspended=2

IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = '00000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO Users (Id, EntraObjectId, Email, DisplayName, Role, Status, CreatedAt)
    VALUES (
        '00000000-0000-0000-0000-000000000001',
        'aaaaaaaa-0000-0000-0000-000000000001',   -- Replace with real Entra OID
        'coolmore-admin@dev.local',
        'Coolmore Admin (Dev)',
        1 /* StudFarmAdmin */, 1 /* Active */, GETUTCDATE()
    );
END

IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = '00000000-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO Users (Id, EntraObjectId, Email, DisplayName, Role, Status, CreatedAt)
    VALUES (
        '00000000-0000-0000-0000-000000000002',
        'aaaaaaaa-0000-0000-0000-000000000002',   -- Replace with real Entra OID
        'arrowfield-admin@dev.local',
        'Arrowfield Admin (Dev)',
        1 /* StudFarmAdmin */, 1 /* Active */, GETUTCDATE()
    );
END

-- ── Stud Farms ───────────────────────────────────────────────
-- Note: UserId references must match actual Entra ID user objects.
-- For local dev, insert placeholder GUIDs and update them once
-- you have the test user IDs from Entra ID.

IF NOT EXISTS (SELECT 1 FROM StudFarms WHERE Name = 'Coolmore Australia (Dev)')
BEGIN
    INSERT INTO StudFarms (Id, UserId, Name, ABN, ContactPhone, ContactEmail, Address, IsActive, CreatedAt)
    VALUES (
        '22222222-0000-0000-0000-000000000001',
        '00000000-0000-0000-0000-000000000001',  -- Replace with stud farm admin user ID
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
        '00000000-0000-0000-0000-000000000002',  -- Replace with second stud farm admin user ID
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

-- ── Fixed Price Listings (Active=1, fee set by staff) ────────
-- Fastnet Rock: first 20 at $8,000
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000001',
        '33333333-0000-0000-0000-000000000001', @SeasonId, @CoolmoreId,
        0 /* FixedPrice */, 1 /* Active */, 2.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000001', 8000.00, 20, 17);
END

-- Fastnet Rock: next 10 at $10,000
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000002')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000002',
        '33333333-0000-0000-0000-000000000001', @SeasonId, @CoolmoreId,
        0 /* FixedPrice */, 1 /* Active */, 2.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000002', 10000.00, 10, 10);
END

-- Snitzel: limited quantity, nearly sold out
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000003')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000003',
        '33333333-0000-0000-0000-000000000002', @SeasonId, @ArrowfieldId,
        0 /* FixedPrice */, 1 /* Active */, 3.0, GETUTCDATE(), GETUTCDATE());

    INSERT INTO FixedPriceListings (Id, PriceIncGst, Quantity, QuantityRemaining)
    VALUES ('44444444-0000-0000-0000-000000000003', 15000.00, 5, 2);
END

-- ── Auction Listings ─────────────────────────────────────────
-- So You Think: ending in ~4 hours (tests "ending soon" styling)
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000004')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000004',
        '33333333-0000-0000-0000-000000000003', @SeasonId, @CoolmoreId,
        1 /* Auction */, 1 /* Active */, 2.0, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, StartingPrice, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000004',
        5000.00, 12000.00, 0, 25.00,
        DATEADD(hour, 4, GETUTCDATE()));   -- Ends in 4 hours (triggers "ending soon" badge)
END

-- Deep Impact: 5 days remaining, no reserve
IF NOT EXISTS (SELECT 1 FROM Listings WHERE Id = '44444444-0000-0000-0000-000000000005')
BEGIN
    INSERT INTO Listings (Id, StallionId, SeasonId, StudFarmId, ListingType, Status,
        PlatformFeePercent, PublishedAt, CreatedAt)
    VALUES ('44444444-0000-0000-0000-000000000005',
        '33333333-0000-0000-0000-000000000004', @SeasonId, @ArrowfieldId,
        1 /* Auction */, 1 /* Active */, 1.5, GETUTCDATE(), GETUTCDATE());

    INSERT INTO AuctionListings (Id, StartingPrice, ReservePrice, IsNoReserve,
        MinimumBidIncrement, EndDateTime)
    VALUES ('44444444-0000-0000-0000-000000000005',
        8000.00, NULL, 1, 25.00,
        DATEADD(day, 5, GETUTCDATE()));    -- Ends in 5 days, no reserve
END

COMMIT;

-- ── Reminder ─────────────────────────────────────────────────
PRINT 'Seed data inserted.';
PRINT 'ACTION REQUIRED: Update the placeholder UserId values in StudFarms to match';
PRINT 'the actual test user GUIDs from your Entra ID tenant.';
PRINT 'Test user role assignments must be done in Entra ID app roles, not the database.';
