# Data Model and API Design Spec

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Define the complete database schema and RESTful API surface for the Stallions Nominations Marketplace backend, ready for implementation using EF Core + ASP.NET Core Web API.

**Architecture:** Table Per Type (TPT) inheritance for Listings. Repository pattern for all data access. Role-based authorization enforced at the API layer via Entra ID claims.

**Tech Stack:** .NET 9, ASP.NET Core Web API, Entity Framework Core 9, Azure SQL Database, Azure Blob Storage (images + PDFs), Azure Entra ID (auth)

---

## Key Design Decisions

| Decision | Choice | Reason |
|---|---|---|
| Listing type structure | TPT inheritance — shared `Listings` base + `AuctionListings` / `FixedPriceListings` child tables | Clean schema, no nullable type-specific columns, natural EF Core mapping |
| Stud farm accounts | One login per stud farm (1:1 User → StudFarm) | Simpler security model, stud farm always has a dedicated nominations manager |
| Buyer registration | Self-register, then staff must verify before first purchase | Balances low friction with platform integrity on a high-value financial system |
| Season management | First-class `Seasons` entity with explicit dates, opened/closed by staff | Seasons are named and dated (e.g. "2026 Season" = 1/9/2026–31/1/2027), not just a year label |
| Primary listing type | Auctions are the primary and featured listing type | Fixed-price listings expose the price, enabling buyers to contact studs directly and bypass the platform |
| Nomination binding | Stud farm acknowledges in platform → PDF generated → both parties sign electronically | Creates a legally binding record under Australia's Electronic Transactions Act 1999 |
| GST tracking | Three fields on every transaction: `FeeIncGst`, `FeeExGst`, `GstAmount` | Required for BAS/tax reporting |
| Enquiries | Tracked message threads stored in DB, visible to staff | Auditable on a high-value financial platform |
| Bid retention | All bids kept with status after auction close | Required for second-bidder offer flow and audit trail |
| Audit log PK | `bigint` (not GUID) | Append-only table that will grow fast — bigint is more efficient for sequential inserts |

---

## Data Model

### Domain: Identity

#### `Users`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `EntraObjectId` | nvarchar(36) | NOT NULL, UNIQUE | Object ID from Entra ID |
| `Email` | nvarchar(256) | NOT NULL, UNIQUE | |
| `DisplayName` | nvarchar(200) | NOT NULL | |
| `Role` | nvarchar(20) | NOT NULL | `Buyer` \| `StudFarmAdmin` \| `Staff` |
| `Status` | nvarchar(30) | NOT NULL, default `PendingVerification` | `PendingVerification` \| `Active` \| `Suspended` |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |
| `VerifiedAt` | datetime2 | NULL | Set when staff approves a buyer |
| `VerifiedByUserId` | uniqueidentifier | NULL, FK → Users | Staff member who approved |

**Rules:**
- All authenticated roles are stored here; role comes from Entra ID claim, mirrored on first login
- Buyers with `Status = PendingVerification` can browse and enquire but cannot bid or purchase
- `Staff` and `StudFarmAdmin` accounts skip verification (they are invited, not self-registered)

#### `StudFarms`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `UserId` | uniqueidentifier | NOT NULL, UNIQUE, FK → Users | One farm per user account |
| `Name` | nvarchar(200) | NOT NULL | |
| `ABN` | nvarchar(14) | NULL | Australian Business Number |
| `ContactPhone` | nvarchar(20) | NULL | |
| `ContactEmail` | nvarchar(256) | NULL | May differ from login email |
| `Address` | nvarchar(500) | NULL | |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |
| `IsActive` | bit | NOT NULL, default 1 | |

---

### Domain: Catalogue

#### `Stallions`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `StudFarmId` | uniqueidentifier | NOT NULL, FK → StudFarms | |
| `Name` | nvarchar(200) | NOT NULL | |
| `YearOfBirth` | int | NULL | |
| `Colour` | nvarchar(50) | NULL | |
| `Sire` | nvarchar(200) | NULL | |
| `Dam` | nvarchar(200) | NULL | |
| `RegistrationNumber` | nvarchar(100) | NULL | |
| `Description` | nvarchar(max) | NULL | Rich text / markdown |
| `IsActive` | bit | NOT NULL, default 1 | |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |

#### `StallionImages`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `StallionId` | uniqueidentifier | NOT NULL, FK → Stallions | |
| `BlobPath` | nvarchar(500) | NOT NULL | Path within `stallion-images` container |
| `IsPrimary` | bit | NOT NULL, default 0 | Only one primary per stallion |
| `DisplayOrder` | int | NOT NULL, default 0 | |
| `UploadedAt` | datetime2 | NOT NULL, default getutcdate() | |

#### `Seasons`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `Name` | nvarchar(100) | NOT NULL | e.g. "2026 Season" |
| `StartDate` | date | NOT NULL | e.g. 2026-09-01 |
| `EndDate` | date | NOT NULL | e.g. 2027-01-31 |
| `IsOpen` | bit | NOT NULL, default 0 | Only one season open at a time |
| `OpenedAt` | datetime2 | NULL | |
| `OpenedByUserId` | uniqueidentifier | NULL, FK → Users | Staff member who opened |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |

**Rules:**
- Only one `Season` may have `IsOpen = 1` at a time (enforced in service layer)
- Stud farms may only create listings in an open season

---

### Domain: Listings

#### `Listings` (base table)
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `StallionId` | uniqueidentifier | NOT NULL, FK → Stallions | |
| `SeasonId` | uniqueidentifier | NOT NULL, FK → Seasons | |
| `StudFarmId` | uniqueidentifier | NOT NULL, FK → StudFarms | Denormalised for query performance |
| `ListingType` | nvarchar(20) | NOT NULL | `FixedPrice` \| `Auction` |
| `Status` | nvarchar(20) | NOT NULL, default `Draft` | `Draft` \| `Active` \| `Sold` \| `Expired` \| `Cancelled` |
| `PlatformFeePercent` | decimal(5,2) | NULL | Set by staff only, never auto-calculated |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |
| `PublishedAt` | datetime2 | NULL | |
| `ClosedAt` | datetime2 | NULL | |

**Rules:**
- `PlatformFeePercent` may only be set or updated by the `Staff` role — enforced at API level, never inferred
- Listings can only be created when the season `IsOpen = 1`
- Only `Draft` listings can be edited by the stud farm; `Active` listings are locked
- `StudFarmId` is stored on the listing for efficient filtering (avoids join to Stallions → StudFarms)

#### `AuctionListings` (TPT child)
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `ListingId` | uniqueidentifier | PK, FK → Listings | Shared PK — no separate identity |
| `StartingPrice` | decimal(12,2) | NOT NULL | |
| `ReservePrice` | decimal(12,2) | NULL | NULL means no-reserve |
| `IsNoReserve` | bit | NOT NULL, default 0 | Explicit flag — reserve is default |
| `MinimumBidIncrement` | decimal(12,2) | NOT NULL, default 25.00 | |
| `EndDateTime` | datetime2 | NOT NULL | Fixed — no rolling extensions |
| `WinningBidId` | uniqueidentifier | NULL, FK → Bids | Set when auction closes with a winner |

**Rules:**
- If `IsNoReserve = 1` then `ReservePrice` must be NULL
- Auction closes at `EndDateTime` regardless of bid activity
- When auction closes: if highest bid ≥ reserve (or no reserve), set `WinningBidId` and `Listings.Status = Sold`; else notify stud farm and set `Status = Expired`

#### `FixedPriceListings` (TPT child)
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `ListingId` | uniqueidentifier | PK, FK → Listings | Shared PK |
| `PriceIncGst` | decimal(12,2) | NOT NULL | All prices stored and displayed inc. GST |
| `Quantity` | int | NOT NULL | Total nominations available |
| `QuantityRemaining` | int | NOT NULL | Decrements on each purchase |

**Rules:**
- When `QuantityRemaining` reaches 0, set `Listings.Status = Sold` automatically
- `Quantity` and `QuantityRemaining` start equal at listing creation

---

### Domain: Transactions

#### `Bids`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `AuctionListingId` | uniqueidentifier | NOT NULL, FK → AuctionListings | |
| `BuyerUserId` | uniqueidentifier | NOT NULL, FK → Users | |
| `AmountIncGst` | decimal(12,2) | NOT NULL | |
| `PlacedAt` | datetime2 | NOT NULL, default getutcdate() | |
| `Status` | nvarchar(20) | NOT NULL, default `Active` | `Active` \| `Outbid` \| `Won` \| `SecondChance` \| `Declined` \| `Expired` |

**Rules:**
- New bid must exceed current highest `Active` bid by at least `MinimumBidIncrement`
- Previous highest bidder's status moves from `Active` → `Outbid` when outbid
- All bids are retained after auction close for audit trail and second-bidder flow
- Second-bidder flow: if winning bidder's payment fails, the `SecondChance` status is set on the next-highest bid. Only one second-chance offer — no cascade beyond second place
- Bidder identity is never exposed publicly; only the current highest amount is shown

#### `Purchases`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `ListingId` | uniqueidentifier | NOT NULL, FK → Listings | |
| `BuyerUserId` | uniqueidentifier | NOT NULL, FK → Users | |
| `BidId` | uniqueidentifier | NULL, FK → Bids | NULL for fixed-price purchases |
| `TotalPriceIncGst` | decimal(12,2) | NOT NULL | Full listing price inc. GST |
| `PlatformFeeIncGst` | decimal(12,2) | NOT NULL | Fee charged to buyer |
| `PlatformFeeExGst` | decimal(12,2) | NOT NULL | For BAS reporting |
| `PlatformFeeGst` | decimal(12,2) | NOT NULL | GST component (= FeeIncGst / 11) |
| `MareName` | nvarchar(200) | NOT NULL | Mandatory at checkout |
| `MareRegistration` | nvarchar(100) | NULL | |
| `MareBreed` | nvarchar(100) | NULL | |
| `PaymentProvider` | nvarchar(50) | NULL | `Stripe` \| `PayPal` \| `POLi` \| `BPAY` |
| `PaymentReference` | nvarchar(200) | NULL | Provider transaction ID |
| `PaidAt` | datetime2 | NULL | Set when payment confirmed |
| `Status` | nvarchar(20) | NOT NULL, default `Pending` | `Pending` \| `Completed` \| `Refunded` |
| `RefundAmount` | decimal(12,2) | NULL | 90% of platform fee on stud-side failure |
| `RefundedAt` | datetime2 | NULL | |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |

**Rules:**
- Buyer must provide `MareName` before purchase is created — no mare details, no purchase record
- If buyer abandons checkout or payment fails: delete the `Pending` purchase, revert listing quantity
- `PlatformFeeGst` = `PlatformFeeIncGst` / 11 (rounded to 2 decimal places)
- Refund = 90% of `PlatformFeeIncGst`; platform retains 10%
- Checkout disclosure must show: total price, fee amount, stud-farm balance arrangement, refund policy

#### `NominationBindings`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `PurchaseId` | uniqueidentifier | NOT NULL, UNIQUE, FK → Purchases | One binding per purchase |
| `Status` | nvarchar(30) | NOT NULL, default `PendingAcknowledgement` | See status flow below |
| `PdfBlobPath` | nvarchar(500) | NULL | Path within `nomination-documents` container |
| `AcknowledgedAt` | datetime2 | NULL | |
| `AcknowledgedByUserId` | uniqueidentifier | NULL, FK → Users | Stud farm user who acknowledged |
| `BuyerSignedAt` | datetime2 | NULL | |
| `FarmSignedAt` | datetime2 | NULL | |
| `CompletedAt` | datetime2 | NULL | |

**Status flow:**
```
PendingAcknowledgement
  → Acknowledged          (stud farm clicks Acknowledge in platform)
  → PdfGenerated          (system generates PDF automatically)
  → AwaitingSignatures    (both parties notified to sign)

From AwaitingSignatures, either party can sign first:
  → BuyerSigned           (buyer signed, farm still pending)
  → FarmSigned            (farm signed, buyer still pending)

From either intermediate signed state:
  → Complete              (second signature received — both BuyerSignedAt and FarmSignedAt are set)

From any state:
  → Disputed              (staff flags a problem)
```

**Rules:**
- PDF is generated automatically after stud farm acknowledges — no manual step
- Electronic signature = timestamped "I agree" action in the platform (valid under Electronic Transactions Act 1999)
- Either party can sign in either order — `Complete` is set when both `BuyerSignedAt` and `FarmSignedAt` are non-null
- The `POST /bindings/{id}/sign` endpoint uses the caller's role to determine which signature field to populate

---

### Domain: Communication

#### `Enquiries`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `ListingId` | uniqueidentifier | NOT NULL, FK → Listings | |
| `BuyerUserId` | uniqueidentifier | NOT NULL, FK → Users | Enquiring user |
| `StudFarmUserId` | uniqueidentifier | NOT NULL, FK → Users | Farm being enquired to |
| `Status` | nvarchar(20) | NOT NULL, default `Open` | `Open` \| `Closed` |
| `CreatedAt` | datetime2 | NOT NULL, default getutcdate() | |
| `ClosedAt` | datetime2 | NULL | |

#### `EnquiryMessages`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier | PK, default newid() | |
| `EnquiryId` | uniqueidentifier | NOT NULL, FK → Enquiries | |
| `SenderUserId` | uniqueidentifier | NOT NULL, FK → Users | |
| `Body` | nvarchar(max) | NOT NULL | |
| `SentAt` | datetime2 | NOT NULL, default getutcdate() | |
| `IsReadByRecipient` | bit | NOT NULL, default 0 | |

**Rules:**
- Any logged-in user can open an enquiry on a listing (buyer, farm, staff)
- Stud farm admin and staff can close threads
- Staff can see all threads; each party can only see their own
- Messages cannot be edited or deleted — full audit trail

#### `AuditLog`
| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | bigint | PK, IDENTITY(1,1) | Sequential — efficient for append-only |
| `EntityType` | nvarchar(100) | NOT NULL | e.g. `Purchase`, `Listing`, `User` |
| `EntityId` | uniqueidentifier | NOT NULL | |
| `Action` | nvarchar(100) | NOT NULL | e.g. `FeePercentSet`, `BidPlaced`, `UserVerified` |
| `UserId` | uniqueidentifier | NULL, FK → Users | NULL for system actions |
| `OccurredAt` | datetime2 | NOT NULL, default getutcdate() | |
| `Details` | nvarchar(max) | NULL | JSON snapshot of relevant fields |

**Rules:**
- All financial transactions, fee changes, and admin actions must write an audit log entry
- Logs are append-only — never updated or deleted
- `Details` stores a before/after JSON snapshot for changes to sensitive fields (fee %, user status)

---

## API Endpoints

Base path: `/api/`  
All endpoints return `application/json`. Auth via Bearer token (Entra ID JWT).

### Auth levels
| Badge | Meaning |
|---|---|
| PUBLIC | No token required |
| AUTH | Any valid Entra ID token |
| BUYER | Role = Buyer AND Status = Active |
| FARM | Role = StudFarmAdmin |
| STAFF | Role = Staff |

### Users
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/users/me` | AUTH | Own profile, role, verification status |
| PUT | `/users/me` | AUTH | Update display name / contact details |
| GET | `/users` | STAFF | List all users — filterable by role/status |
| POST | `/users/{id}/verify` | STAFF | Approve a pending buyer |
| POST | `/users/{id}/suspend` | STAFF | Suspend a user account |

### Seasons
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/seasons` | PUBLIC | List all seasons (past, current, upcoming) |
| GET | `/seasons/current` | PUBLIC | Get the currently open season |
| POST | `/seasons` | STAFF | Create a new season |
| PUT | `/seasons/{id}` | STAFF | Update season name / dates |
| POST | `/seasons/{id}/open` | STAFF | Open season — listings can now be created |
| POST | `/seasons/{id}/close` | STAFF | Close season — no new listings accepted |

### Stallions
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/stallions` | PUBLIC | Browse all stallions with active listings; own stallions when called by FARM |
| GET | `/stallions/{id}` | PUBLIC | Stallion profile + images + active listings |
| POST | `/stallions` | FARM | Create stallion profile for own stud farm |
| PUT | `/stallions/{id}` | FARM | Update stallion profile (own stallions only) |
| POST | `/stallions/{id}/images` | FARM | Upload image to Blob Storage |
| PUT | `/stallions/{id}/images/{imageId}/primary` | FARM | Set primary image |
| DELETE | `/stallions/{id}/images/{imageId}` | FARM | Remove image |

### Listings
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/listings` | PUBLIC | Browse active listings — filter by season, type, stallion |
| GET | `/listings/{id}` | PUBLIC | Full listing detail (reserve price hidden from public) |
| GET | `/listings/mine` | FARM | Own stud farm's listings — all statuses |
| POST | `/listings/auction` | FARM | Create auction listing (saved as Draft) |
| POST | `/listings/fixed-price` | FARM | Create fixed-price listing (saved as Draft) |
| PUT | `/listings/{id}` | FARM | Update listing — Draft status only, own farm only |
| POST | `/listings/{id}/publish` | FARM | Publish draft listing to marketplace |
| POST | `/listings/{id}/cancel` | STAFF | Cancel an active listing |
| POST | `/listings/{id}/relist` | FARM | Re-list expired/no-bid auction as a new draft |

### Bids
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/listings/{id}/bids/current` | PUBLIC | Current highest bid amount — no bidder identity |
| POST | `/listings/{id}/bids` | BUYER | Place a bid — must exceed current by min increment |
| GET | `/listings/{id}/bids` | STAFF | Full bid history with bidder details |
| GET | `/bids/mine` | BUYER | Own bid history across all auctions |

### Checkout & Purchases
| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/listings/{id}/checkout` | BUYER | Initiate checkout — fixed price or winning bid. Returns payment session |
| POST | `/purchases/{id}/complete` | PUBLIC | Payment provider webhook — validates signature, marks purchase complete, triggers binding creation |
| GET | `/purchases` | AUTH | Own purchases (buyer) or all purchases (staff) |
| GET | `/purchases/{id}` | AUTH | Purchase detail — accessible to buyer who made it and staff |
| POST | `/purchases/{id}/refund` | STAFF | Issue 90% refund on failed stud-side arrangement |

### Nomination Bindings
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/bindings/{id}` | AUTH | Binding detail, status, PDF link — accessible to buyer, stud farm, staff |
| POST | `/bindings/{id}/acknowledge` | FARM | Stud farm confirms receipt of nomination |
| POST | `/bindings/{id}/sign` | AUTH | Electronically sign the binding — role determines which signature is recorded |
| POST | `/bindings/{id}/dispute` | STAFF | Flag a binding as disputed |

### Enquiries
| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/listings/{id}/enquiries` | AUTH | Open an enquiry thread on a listing |
| GET | `/enquiries` | AUTH | Own enquiries (buyer/farm) or all enquiries (staff) |
| GET | `/enquiries/{id}` | AUTH | Full message thread — parties + staff only |
| POST | `/enquiries/{id}/messages` | AUTH | Send a message in the thread |
| POST | `/enquiries/{id}/close` | FARM | Close the enquiry thread |

### Admin
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/admin/dashboard` | STAFF | Platform summary — active listings, recent purchases, revenue |
| GET | `/admin/transactions` | STAFF | All transactions with full GST breakdown for BAS reporting |
| GET | `/admin/invoices` | STAFF | Stud farm invoices summarising sales and fee deductions |
| PUT | `/admin/listings/{id}/fee` | STAFF | Set platform fee % on a listing — never auto-calculated |

---

## Architecture Notes

### Repository pattern
All database access goes through repositories in `src/Server/Data/Repositories/`. Controllers call services; services call repositories. No raw SQL or EF Core `DbContext` in controllers.

### EF Core TPT inheritance
```csharp
// In DbContext:
modelBuilder.Entity<Listing>().ToTable("Listings");
modelBuilder.Entity<AuctionListing>().ToTable("AuctionListings");
modelBuilder.Entity<FixedPriceListing>().ToTable("FixedPriceListings");
```
EF Core handles the JOIN automatically when querying through the base type.

### Platform fee enforcement
The `PlatformFeePercent` field on `Listings` is only writable via `PUT /admin/listings/{id}/fee` (Staff role). The general `PUT /listings/{id}` endpoint explicitly ignores any fee field in the request body, even if the caller is authenticated. This is enforced in the service layer, not just the controller.

### Payment webhook security
`POST /purchases/{id}/complete` must validate the payment provider's webhook signature (e.g. Stripe-Signature header) before processing. It does not require a user token — it is authenticated by the shared webhook secret stored in Key Vault.

### Auction closure
Auction closing is handled by an Azure Function on a timer trigger (every 5 minutes). It queries for `AuctionListings` where `EndDateTime <= now` and `Listings.Status = Active`, then processes each: determines winner (or no-winner), updates statuses, creates `Purchase` (if winner), and sends notifications.

### Soft deletes
Entities are never hard-deleted. `IsActive` flags on `Users`, `StudFarms`, and `Stallions` handle deactivation. Listings use their `Status` field. This preserves the audit trail for a financial platform.

### Indexes to add (beyond PKs/FKs)
- `Listings (Status, SeasonId)` — primary browse query
- `Listings (StudFarmId, Status)` — farm's own listings
- `Bids (AuctionListingId, AmountIncGst DESC)` — current highest bid
- `AuditLog (EntityType, EntityId)` — audit queries per entity
- `AuditLog (OccurredAt)` — time-range audit queries

---

## Out of Scope for This Spec
- Payment provider SDK integration (provider-agnostic interface only — provider chosen later)
- PDF contract generation implementation (triggers after acknowledgement — format TBD)
- Email notification templates (Azure Functions trigger points defined; content TBD)
- ArionWeb integration
- Automated stud farm payouts / reconciliation
- Multi-admin stud farms (v1 is single user per farm)
