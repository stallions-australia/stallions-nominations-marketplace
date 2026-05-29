# Azure AD B2C Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace single-tenant Entra ID authentication with Azure AD B2C so buyers can self-register via email/password or Google, while all user types authenticate through one system with roles determined from the application database.

**Architecture:** B2C User Flows (no Custom Policies) issue JWTs that the server validates via `Microsoft.Identity.Web`. Authorization policies query the `Users` table for role/status rather than reading JWT claims. The client fetches the user's role from `GET /api/users/me` after login and caches it in `UserStateService` for UI decisions.

**Tech Stack:** Azure AD B2C, Microsoft.Identity.Web (existing), MSAL for Blazor WASM (existing), ASP.NET Core Authorization Policies, EF Core migration.

---

> ⚠️ **Phase 1 (Tasks 1–5) is manual Azure Portal work** that produces config values (tenant ID, client IDs) required for all subsequent code tasks. Complete Phase 1 entirely before writing any code.

---

## Phase 1 — Azure Portal Setup (manual, one-time)

### Task 1: Create the Azure AD B2C Tenant

**Files:** none — portal steps only

- [ ] **Step 1: Open Azure Portal**

  Go to https://portal.azure.com and sign in as `dbr@badranad.onmicrosoft.com`.

- [ ] **Step 2: Create B2C tenant**

  Search for "Azure AD B2C" → click **Create** → select **Create a new Azure AD B2C Tenant**.

  Fill in:
  - Organization name: `Stallions Australia`
  - Initial domain name: `stallionsaustralia` (this will become `stallionsaustralia.onmicrosoft.com`) — if taken, try `stallionsnoms` or `stallionsnominations`
  - Country/Region: Australia
  - Subscription: your existing subscription
  - Resource group: `rg-stallions-noms-b2c` (new group, just for the B2C resource link)

  Click **Review + Create** → **Create**. Creation takes 1–2 minutes.

- [ ] **Step 3: Note the tenant ID**

  After creation, go to **Azure AD B2C** → **Overview**. Note the **Tenant ID** (a GUID). You'll need it in Task 6 and later.

  ```
  B2C Tenant ID: ________________________________
  B2C Domain:    stallionsaustralia.onmicrosoft.com
  ```

- [ ] **Step 4: Switch to the B2C tenant**

  Click the directory switcher (top right) and switch to `stallionsaustralia.onmicrosoft.com`. All remaining Portal steps in Phase 1 happen inside this B2C tenant.

---

### Task 2: Create App Registrations

**Files:** none — portal steps only

You'll create two app registrations — one for dev, one for prod. Do both now.

- [ ] **Step 1: Create the dev app registration**

  In the B2C tenant: **App registrations** → **New registration**.

  - Name: `stallions-dev`
  - Supported account types: **Accounts in any identity provider or organizational directory** (the B2C default)
  - Redirect URI: Select **Single-page application (SPA)** — enter `https://localhost:7001/authentication/login-callback`
  - Click **Register**

- [ ] **Step 2: Add second redirect URI for dev App Service**

  On the `stallions-dev` registration → **Authentication** → under Redirect URIs add:
  `https://app-stallions-noms-dev.azurewebsites.net/authentication/login-callback`

  Under **Front-channel logout URL** add:
  `https://app-stallions-noms-dev.azurewebsites.net/authentication/logout-callback`

  Under **Implicit grant and hybrid flows** — ensure both checkboxes are **unchecked** (PKCE only).

  Click **Save**.

- [ ] **Step 3: Note the dev client ID**

  On the **Overview** page of `stallions-dev`, note the **Application (client) ID**.

  ```
  Dev Client ID: ________________________________
  ```

- [ ] **Step 4: Expose the API scope for dev**

  On `stallions-dev` → **Expose an API** → **Add** next to Application ID URI.
  Set it to: `https://stallionsaustralia.onmicrosoft.com/stallions-api`
  Click **Save**.

  Then **Add a scope**:
  - Scope name: `API.Access`
  - Admin consent display name: `Access Stallions API`
  - Admin consent description: `Allows the app to call the Stallions Nominations API`
  - State: Enabled
  Click **Add scope**.

- [ ] **Step 5: Grant the dev app permission to its own scope**

  On `stallions-dev` → **API permissions** → **Add a permission** → **My APIs** → select `stallions-dev` → check `API.Access` → **Add permissions**.

  Then click **Grant admin consent for stallionsaustralia** → **Yes**.

- [ ] **Step 6: Create the prod app registration**

  Repeat Steps 1–5 for prod:
  - Name: `stallions-prod`
  - Redirect URIs: `https://app-stallions-noms-prod.azurewebsites.net/authentication/login-callback`
  - Front-channel logout: `https://app-stallions-noms-prod.azurewebsites.net/authentication/logout-callback`
  - Expose API scope identically (same scope URI — `https://stallionsaustralia.onmicrosoft.com/stallions-api/API.Access`)
  - Grant consent

  ```
  Prod Client ID: ________________________________
  ```

---

### Task 3: Configure the Sign-Up/Sign-In User Flow

**Files:** none — portal steps only

- [ ] **Step 1: Open User flows**

  In the B2C tenant: **User flows** → **New user flow**.

- [ ] **Step 2: Create the SUSI flow**

  Select **Sign up and sign in** → Version **Recommended** → **Create**.

  - Name: `susi` (B2C will prefix it to `B2C_1_susi`)
  - Identity providers: check **Email signup**
  - (Google will be added in Task 4 — you can add it after Google is configured)
  - Multifactor authentication: **Off** for now
  - Conditional access: leave off

- [ ] **Step 3: Configure user attributes**

  Under **User attributes** (collected at sign-up), check:
  - ✅ Display Name
  - ✅ Email Address (already required)

- [ ] **Step 4: Configure return claims**

  Under **Application claims** (included in the token), check:
  - ✅ Display Name
  - ✅ Email Addresses
  - ✅ Identity Provider
  - ✅ User's Object ID

  Click **Create**.

- [ ] **Step 5: Verify the flow name**

  After creation, confirm the flow is named `B2C_1_susi` in the list.

---

### Task 4: Set Up Google as an Identity Provider

**Files:** none — portal + Google Console steps

- [ ] **Step 1: Create a Google Cloud project**

  Go to https://console.cloud.google.com → create a new project named `stallions-nominations-marketplace`.

- [ ] **Step 2: Create OAuth credentials**

  In the Google project: **APIs & Services** → **Credentials** → **Create credentials** → **OAuth client ID**.

  - Application type: **Web application**
  - Name: `Stallions B2C`
  - Authorised redirect URIs: `https://stallionsaustralia.b2clogin.com/stallionsaustralia.onmicrosoft.com/oauth2/authresp`
  - Click **Create**

  Note the **Client ID** and **Client secret** shown.

  ```
  Google Client ID:     ________________________________
  Google Client secret: ________________________________
  ```

- [ ] **Step 3: Configure Google IdP in B2C**

  Back in the B2C tenant: **Identity providers** → **Google**.

  - Client ID: paste the Google Client ID
  - Client secret: paste the Google Client secret
  - Click **Save**

- [ ] **Step 4: Add Google to the user flow**

  **User flows** → `B2C_1_susi` → **Identity providers** → check **Google** → **Save**.

---

### Task 5: Create Staff and StudFarm Admin B2C Accounts

**Files:** none — portal steps only

Staff and StudFarm Admin users don't self-register — you create their accounts directly in the B2C directory.

- [ ] **Step 1: Create your own Staff account**

  In the B2C tenant: **Users** → **New user** → **Create user**.

  - Identity provider: **Local account**
  - Email: use your real email (e.g. `david@stallionsaustralia.com.au` or any address you control)
  - Display name: `David Reichard`
  - Password: set a strong password, note it

  Click **Create**.

- [ ] **Step 2: Note the new user's Object ID**

  After creation, open the user → note the **Object ID** (GUID). The application will look this up and assign `Role = Staff` automatically on first login *after* you set it — but because new users default to `Buyer` in the provisioning logic, you'll need to update the DB after first login (see Task 15).

  Alternatively: after first B2C login in Task 15, use the Staff → Users panel to verify yourself, which promotes you to Active. But the role in the DB will still be Buyer until changed. Plan for this in Task 15.

  > **Note:** For now, create the account. Role assignment to Staff happens in Task 15 via a direct DB update after first login, since there's no bootstrapping mechanism yet.

- [ ] **Step 3: Record all config values**

  Collect everything needed for the code tasks:

  ```
  B2C Tenant ID:        ________________________________
  B2C Domain:           stallionsaustralia.onmicrosoft.com
  B2C Instance:         https://stallionsaustralia.b2clogin.com
  Dev Client ID:        ________________________________
  Prod Client ID:       ________________________________
  User Flow:            B2C_1_susi
  API Scope (dev/prod): https://stallionsaustralia.onmicrosoft.com/stallions-api/API.Access
  ```

---

## Phase 2 — Server: Entity & Repository Rename

### Task 6: Rename `EntraObjectId` → `ObjectId` in Entity, Repository, DbContext, and add EF Migration

**Files:**
- Modify: `src/Server/Data/Entities/User.cs`
- Modify: `src/Server/Data/AppDbContext.cs`
- Modify: `src/Server/Data/Repositories/IUserRepository.cs`
- Modify: `src/Server/Data/Repositories/UserRepository.cs`
- Modify: `src/Server/Services/UserService.cs`
- Create (via EF CLI): `src/Server/Data/Migrations/<timestamp>_RenameEntraObjectIdToObjectId.cs`

- [ ] **Step 1: Update `User.cs`**

  File: `src/Server/Data/Entities/User.cs`

  Change `EntraObjectId` to `ObjectId`:

  ```csharp
  using Stallions.Shared.Enums;

  namespace Stallions.Server.Data.Entities;

  public class User
  {
      public Guid Id { get; set; } = Guid.NewGuid();
      public string ObjectId { get; set; } = string.Empty;   // B2C Object ID (was EntraObjectId)
      public string Email { get; set; } = string.Empty;
      public string DisplayName { get; set; } = string.Empty;
      public UserRole Role { get; set; }
      public UserStatus Status { get; set; } = UserStatus.PendingVerification;
      public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
      public DateTime? VerifiedAt { get; set; }
      public Guid? VerifiedByUserId { get; set; }

      // Navigation properties
      public User? VerifiedBy { get; set; }
      public StudFarm? StudFarm { get; set; }
      public ICollection<Bid> Bids { get; set; } = new List<Bid>();
      public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
      public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
      public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
  }
  ```

- [ ] **Step 2: Update `AppDbContext.cs`**

  File: `src/Server/Data/AppDbContext.cs`

  In `OnModelCreating`, change both references from `EntraObjectId` to `ObjectId`:

  ```csharp
  modelBuilder.Entity<User>(e =>
  {
      e.HasKey(u => u.Id);
      e.HasIndex(u => u.ObjectId).IsUnique();          // was EntraObjectId
      e.HasIndex(u => u.Email).IsUnique();
      e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
      e.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
      e.Property(u => u.Email).HasMaxLength(256).IsRequired();
      e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
      e.Property(u => u.ObjectId).HasMaxLength(36).IsRequired();  // was EntraObjectId
      // ... rest of config unchanged
  ```

  > Read the full `AppDbContext.cs` first to make sure you only change the two lines above and leave everything else intact.

- [ ] **Step 3: Update `IUserRepository.cs`**

  File: `src/Server/Data/Repositories/IUserRepository.cs`

  ```csharp
  using Stallions.Server.Data.Entities;
  using Stallions.Shared.Enums;

  namespace Stallions.Server.Data.Repositories;

  public interface IUserRepository
  {
      Task<User?> GetByIdAsync(Guid id);
      Task<User?> GetByObjectIdAsync(string objectId);   // was GetByEntraObjectIdAsync
      Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null);
      Task<User> AddAsync(User user);
      Task UpdateAsync(User user);
  }
  ```

- [ ] **Step 4: Update `UserRepository.cs`**

  File: `src/Server/Data/Repositories/UserRepository.cs`

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using Stallions.Server.Data.Entities;
  using Stallions.Shared.Enums;

  namespace Stallions.Server.Data.Repositories;

  public class UserRepository : IUserRepository
  {
      private readonly AppDbContext _db;
      public UserRepository(AppDbContext db) => _db = db;

      public async Task<User?> GetByIdAsync(Guid id) =>
          await _db.Users.FindAsync(id);

      public async Task<User?> GetByObjectIdAsync(string objectId) =>
          await _db.Users.FirstOrDefaultAsync(u => u.ObjectId == objectId);

      public async Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null)
      {
          var query = _db.Users.AsQueryable();
          if (role.HasValue) query = query.Where(u => u.Role == role.Value);
          if (status.HasValue) query = query.Where(u => u.Status == status.Value);
          return await query.OrderBy(u => u.DisplayName).ToListAsync();
      }

      public async Task<User> AddAsync(User user)
      {
          _db.Users.Add(user);
          await _db.SaveChangesAsync();
          return user;
      }

      public async Task UpdateAsync(User user)
      {
          _db.Users.Update(user);
          await _db.SaveChangesAsync();
      }
  }
  ```

- [ ] **Step 5: Update `UserService.cs`**

  File: `src/Server/Services/UserService.cs`

  Change the `GetOrCreateCurrentUserAsync` method — two lines change:

  ```csharp
  public async Task<User?> GetOrCreateCurrentUserAsync()
  {
      var objectId = _currentUser.ObjectId;     // was EntraObjectId
      if (objectId == null) return null;

      var user = await _repo.GetByObjectIdAsync(objectId);   // was GetByEntraObjectIdAsync
      if (user != null) return user;

      // First login — provision user from B2C claims
      // New buyers default to PendingVerification; Staff/StudFarmAdmin are set via DB after first login
      var role = UserRole.Buyer;
      var status = UserStatus.PendingVerification;

      user = new User
      {
          ObjectId = objectId,                  // was EntraObjectId = entraOid
          Email = _currentUser.Email ?? string.Empty,
          DisplayName = _currentUser.DisplayName ?? _currentUser.Email ?? string.Empty,
          Role = role,
          Status = status,
          VerifiedAt = null
      };

      try
      {
          return await _repo.AddAsync(user);
      }
      catch (DbUpdateException)
      {
          return await _repo.GetByObjectIdAsync(objectId);   // was GetByEntraObjectIdAsync
      }
  }
  ```

  > Note: Role is always `Buyer` on first login. Staff and StudFarmAdmin accounts are promoted via a direct DB update after their first login (see Task 15).

- [ ] **Step 6: Generate the EF migration**

  Run from the repo root:

  ```bash
  dotnet ef migrations add RenameEntraObjectIdToObjectId \
    --project src/Server/Stallions.Server.csproj \
    --startup-project src/Server/Stallions.Server.csproj
  ```

  Expected output: `Build succeeded. ... Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 7: Verify migration content**

  Open the generated migration file (e.g. `src/Server/Data/Migrations/<timestamp>_RenameEntraObjectIdToObjectId.cs`). It should contain:

  ```csharp
  migrationBuilder.RenameColumn(
      name: "EntraObjectId",
      table: "Users",
      newName: "ObjectId");

  migrationBuilder.RenameIndex(
      name: "IX_Users_EntraObjectId",
      table: "Users",
      newName: "IX_Users_ObjectId");
  ```

  If it shows `DropColumn` + `AddColumn` instead of `RenameColumn`, the rename wasn't detected. Delete the migration and re-run after confirming the entity change is correct.

- [ ] **Step 8: Build to verify no compile errors**

  ```bash
  dotnet build src/Server/Stallions.Server.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 9: Commit**

  ```bash
  git add src/Server/Data/Entities/User.cs \
          src/Server/Data/AppDbContext.cs \
          src/Server/Data/Repositories/IUserRepository.cs \
          src/Server/Data/Repositories/UserRepository.cs \
          src/Server/Services/UserService.cs \
          src/Server/Data/Migrations/
  git commit -m "refactor: rename EntraObjectId to ObjectId for B2C migration"
  ```

---

### Task 7: Update `ICurrentUserService` and `CurrentUserService`

**Files:**
- Modify: `src/Server/Auth/ICurrentUserService.cs`
- Modify: `src/Server/Auth/CurrentUserService.cs`

- [ ] **Step 1: Update `ICurrentUserService.cs`**

  File: `src/Server/Auth/ICurrentUserService.cs`

  Remove `Roles`, rename `EntraObjectId` to `ObjectId`:

  ```csharp
  namespace Stallions.Server.Auth;

  public interface ICurrentUserService
  {
      string? ObjectId { get; }       // B2C object ID (was EntraObjectId)
      string? Email { get; }
      string? DisplayName { get; }
      bool IsAuthenticated { get; }
      // Roles removed — roles come from the database, not the JWT
  }
  ```

- [ ] **Step 2: Update `CurrentUserService.cs`**

  File: `src/Server/Auth/CurrentUserService.cs`

  ```csharp
  using System.Security.Claims;
  using Microsoft.AspNetCore.Http;

  namespace Stallions.Server.Auth;

  public class CurrentUserService : ICurrentUserService
  {
      private readonly IHttpContextAccessor _httpContextAccessor;

      public CurrentUserService(IHttpContextAccessor httpContextAccessor)
          => _httpContextAccessor = httpContextAccessor;

      private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

      public string? ObjectId =>
          User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
          ?? User?.FindFirst("oid")?.Value;

      public string? Email =>
          User?.FindFirst("emails")?.Value        // B2C: email/password + Google
          ?? User?.FindFirst("email")?.Value;     // fallback

      public string? DisplayName =>
          User?.FindFirst("name")?.Value
          ?? User?.FindFirst(ClaimTypes.Name)?.Value;

      public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
  }
  ```

- [ ] **Step 3: Build to verify**

  ```bash
  dotnet build src/Server/Stallions.Server.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

  > If you get errors about `Roles` being missing — search for `_currentUser.Roles` in the codebase. The only place it was used was in the old `UserService.GetOrCreateCurrentUserAsync` — we already removed that in Task 6.

- [ ] **Step 4: Commit**

  ```bash
  git add src/Server/Auth/ICurrentUserService.cs src/Server/Auth/CurrentUserService.cs
  git commit -m "refactor: update CurrentUserService for B2C claims (remove Roles, fix email claim)"
  ```

---

## Phase 3 — Server: Authorization Policies

### Task 8: Add DB-Backed Authorization Handler and Update `Program.cs`

**Files:**
- Create: `src/Server/Auth/DbRoleHandler.cs`
- Modify: `src/Server/Program.cs`
- Modify: `src/Server/appsettings.json`
- Modify: `src/Server/appsettings.Development.json`

- [ ] **Step 1: Create `DbRoleHandler.cs`**

  File: `src/Server/Auth/DbRoleHandler.cs`

  This file defines the requirement and handler that backs all four policies. Each request to a policy-protected endpoint calls `IUserService.GetOrCreateCurrentUserAsync()` to fetch (or provision) the user from the DB, then checks their role and status.

  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Stallions.Server.Services;
  using Stallions.Shared.Enums;

  namespace Stallions.Server.Auth;

  /// <summary>
  /// Represents a requirement that the authenticated user must have a specific role
  /// (or one of several roles) in the application database, with Active status.
  /// </summary>
  public class DbRoleRequirement : IAuthorizationRequirement
  {
      public IReadOnlyList<UserRole> AllowedRoles { get; }

      public DbRoleRequirement(params UserRole[] roles) =>
          AllowedRoles = roles;
  }

  /// <summary>
  /// Resolves the current user from the database and checks their role and status.
  /// Called for every request that hits a policy-protected endpoint.
  /// </summary>
  public class DbRoleHandler : AuthorizationHandler<DbRoleRequirement>
  {
      private readonly IUserService _users;

      public DbRoleHandler(IUserService users) => _users = users;

      protected override async Task HandleRequirementAsync(
          AuthorizationHandlerContext context,
          DbRoleRequirement requirement)
      {
          // The JWT must already be authenticated before this handler runs.
          if (context.User.Identity?.IsAuthenticated != true)
              return;

          var user = await _users.GetOrCreateCurrentUserAsync();

          if (user is null)
              return;

          if (user.Status == UserStatus.Active &&
              requirement.AllowedRoles.Contains(user.Role))
          {
              context.Succeed(requirement);
          }
      }
  }
  ```

- [ ] **Step 2: Update `Program.cs` — auth section**

  File: `src/Server/Program.cs`

  Replace the existing auth + authorization block with the B2C version. The only change to auth is the config section name; the policies are new additions.

  Replace:
  ```csharp
  // Auth — Entra ID JWT validation via Microsoft.Identity.Web
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
  builder.Services.AddAuthorization();
  ```

  With:
  ```csharp
  // Auth — Azure AD B2C JWT validation via Microsoft.Identity.Web
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
  builder.Services.AddAuthorization(options =>
  {
      // DB-backed role policies — role and Active status checked against the Users table.
      // JWT only proves identity; the database is the source of truth for roles.
      options.AddPolicy("StaffOnly",
          p => p.AddRequirements(new DbRoleRequirement(UserRole.Staff)));
      options.AddPolicy("StudFarmAdminOnly",
          p => p.AddRequirements(new DbRoleRequirement(UserRole.StudFarmAdmin)));
      options.AddPolicy("StudFarmOrStaff",
          p => p.AddRequirements(new DbRoleRequirement(UserRole.StudFarmAdmin, UserRole.Staff)));
      options.AddPolicy("BuyerOnly",
          p => p.AddRequirements(new DbRoleRequirement(UserRole.Buyer)));
  });

  // Register the handler that evaluates DbRoleRequirement
  builder.Services.AddScoped<IAuthorizationHandler, DbRoleHandler>();
  ```

  Also add the using at the top of the file if not already present:
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  ```

- [ ] **Step 3: Update `appsettings.json`**

  File: `src/Server/appsettings.json`

  Replace the `AzureAd` section with `AzureAdB2C`:

  ```json
  {
    "Logging": {
      "LogLevel": {
        "Default": "Information",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
      }
    },
    "AllowedHosts": "*",
    "AzureAdB2C": {
      "Instance": "https://stallionsaustralia.b2clogin.com",
      "Domain": "stallionsaustralia.onmicrosoft.com",
      "TenantId": "PLACEHOLDER_SET_IN_ENVIRONMENT",
      "ClientId": "PLACEHOLDER_SET_IN_ENVIRONMENT",
      "SignUpSignInPolicyId": "B2C_1_susi"
    },
    "Cors": {
      "AllowedOrigins": [ "https://localhost:7001" ]
    },
    "Checkout": {
      "WebhookSecret": "PLACEHOLDER_SET_IN_KEYVAULT",
      "StudFarmBalanceArrangement": "The stud farm will contact you separately to arrange payment of the remaining balance. The balance amount, payment terms, and any foal guarantee conditions are a matter between you and the stud farm only. Stallions Australia is not party to that arrangement.",
      "RefundPolicy": "If the stud farm arrangement falls through entirely, you will receive a refund of 90% of the platform fee paid at time of purchase. Stallions Australia retains 10% to cover administrative costs. Please refer to the full Terms and Conditions for details."
    }
  }
  ```

- [ ] **Step 4: Update `appsettings.Development.json`**

  File: `src/Server/appsettings.Development.json`

  Replace the `AzureAd` section with real B2C values from Task 5 notes. Replace `YOUR_B2C_TENANT_ID` and `YOUR_DEV_CLIENT_ID` with the actual GUIDs you noted:

  ```json
  {
    "Logging": {
      "LogLevel": {
        "Default": "Information",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
      }
    },
    "ConnectionStrings": {
      "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=StallionsNomsDev;Trusted_Connection=True;MultipleActiveResultSets=true"
    },
    "AzureAdB2C": {
      "Instance": "https://stallionsaustralia.b2clogin.com",
      "Domain": "stallionsaustralia.onmicrosoft.com",
      "TenantId": "YOUR_B2C_TENANT_ID",
      "ClientId": "YOUR_DEV_CLIENT_ID",
      "SignUpSignInPolicyId": "B2C_1_susi"
    },
    "Cors": {
      "AllowedOrigins": [
        "https://localhost:7124",
        "http://localhost:5279"
      ]
    },
    "Checkout": {
      "WebhookSecret": "dev-webhook-secret-change-in-prod"
    }
  }
  ```

- [ ] **Step 5: Build to verify**

  ```bash
  dotnet build src/Server/Stallions.Server.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Commit**

  ```bash
  git add src/Server/Auth/DbRoleHandler.cs \
          src/Server/Program.cs \
          src/Server/appsettings.json \
          src/Server/appsettings.Development.json
  git commit -m "feat: add B2C auth config and DB-backed authorization policies"
  ```

---

### Task 9: Update All Controller `[Authorize]` Attributes

**Files:**
- Modify: `src/Server/Controllers/AdminController.cs`
- Modify: `src/Server/Controllers/BidsController.cs`
- Modify: `src/Server/Controllers/BindingsController.cs`
- Modify: `src/Server/Controllers/EnquiriesController.cs`
- Modify: `src/Server/Controllers/ListingsController.cs`
- Modify: `src/Server/Controllers/PurchasesController.cs`
- Modify: `src/Server/Controllers/SeasonsController.cs`
- Modify: `src/Server/Controllers/StallionsController.cs`
- Modify: `src/Server/Controllers/UsersController.cs`

Replace every `[Authorize(Roles = "...")]` according to this mapping. Read each file before editing.

| Old attribute | New attribute |
|---|---|
| `[Authorize(Roles = "Staff")]` | `[Authorize(Policy = "StaffOnly")]` |
| `[Authorize(Roles = "StudFarmAdmin")]` | `[Authorize(Policy = "StudFarmAdminOnly")]` |
| `[Authorize(Roles = "Buyer")]` | `[Authorize(Policy = "BuyerOnly")]` |

- [ ] **Step 1: Update `AdminController.cs`**

  Line 10: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

  This is a class-level attribute, so all endpoints in the controller become Staff-only.

- [ ] **Step 2: Update `BidsController.cs`**

  - Line 26: `[Authorize(Roles = "Buyer")]` → `[Authorize(Policy = "BuyerOnly")]`
  - Line 35: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`
  - Line 43: `[Authorize(Roles = "Buyer")]` → `[Authorize(Policy = "BuyerOnly")]`

- [ ] **Step 3: Update `BindingsController.cs`**

  - Line 24: `[Authorize(Roles = "StudFarmAdmin")]` → `[Authorize(Policy = "StudFarmAdminOnly")]`
  - Line 39: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

- [ ] **Step 4: Update `EnquiriesController.cs`**

  - Line 16: `[Authorize(Roles = "Buyer")]` → `[Authorize(Policy = "BuyerOnly")]`

- [ ] **Step 5: Update `ListingsController.cs`**

  - Lines 38, 46, 54, 62, 70, 78, 86, 102: `[Authorize(Roles = "StudFarmAdmin")]` → `[Authorize(Policy = "StudFarmAdminOnly")]`
  - Line 94: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

- [ ] **Step 6: Update `PurchasesController.cs`**

  - Line 17: `[Authorize(Roles = "Buyer")]` → `[Authorize(Policy = "BuyerOnly")]`
  - Line 50: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

- [ ] **Step 7: Update `SeasonsController.cs`**

  - Lines 32, 40, 48, 56: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

- [ ] **Step 8: Update `StallionsController.cs`**

  - Lines 24, 40, 48, 56, 66, 74: `[Authorize(Roles = "StudFarmAdmin")]` → `[Authorize(Policy = "StudFarmAdminOnly")]`

- [ ] **Step 9: Update `UsersController.cs`**

  - Lines 33, 41, 49, 57: `[Authorize(Roles = "Staff")]` → `[Authorize(Policy = "StaffOnly")]`

- [ ] **Step 10: Verify no `[Authorize(Roles` remain**

  Run:
  ```bash
  grep -rn "Authorize(Roles" src/Server/Controllers/
  ```

  Expected output: nothing (empty — no matches).

- [ ] **Step 11: Build to verify**

  ```bash
  dotnet build src/Server/Stallions.Server.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 12: Commit**

  ```bash
  git add src/Server/Controllers/
  git commit -m "refactor: replace Authorize(Roles) with DB-backed policy attributes on all controllers"
  ```

---

## Phase 4 — Infrastructure: Bicep Update

### Task 10: Update Bicep to Use B2C App Settings

**Files:**
- Modify: `infra/modules/appservice.bicep`
- Modify: `infra/main.bicep`

- [ ] **Step 1: Update `appservice.bicep` parameters and app settings**

  File: `infra/modules/appservice.bicep`

  Replace the Entra parameters with B2C parameters. The full updated file:

  ```bicep
  param environmentName string
  param location string
  param tags object
  param appInsightsConnectionString string
  param keyVaultUri string
  param b2cTenantId string
  param b2cDomain string
  param b2cClientId string
  param storageAccountName string

  var isProduction = environmentName == 'prod'

  // Merge the azd service-name tag so azd deploy can locate this App Service
  var appServiceTags = union(tags, { 'azd-service-name': 'api' })

  resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
    name: 'plan-stallions-noms-${environmentName}'
    location: location
    tags: tags
    sku: {
      name: 'B2'
      tier: 'Basic'
    }
    properties: {}
  }

  resource appService 'Microsoft.Web/sites@2023-01-01' = {
    name: 'app-stallions-noms-${environmentName}'
    location: location
    tags: appServiceTags
    identity: {
      type: 'SystemAssigned'
    }
    properties: {
      serverFarmId: appServicePlan.id
      httpsOnly: true
      clientAffinityEnabled: false
      siteConfig: {
        alwaysOn: isProduction
        minTlsVersion: '1.2'
        ftpsState: 'Disabled'
        netFrameworkVersion: 'v9.0'
        appSettings: [
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: appInsightsConnectionString
          }
          {
            name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
            value: '~3'
          }
          {
            name: 'KeyVaultUri'
            value: keyVaultUri
          }
          {
            name: 'ASPNETCORE_ENVIRONMENT'
            value: 'Production'
          }
          {
            name: 'ConnectionStrings__DefaultConnection'
            value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/SqlConnectionString/)'
          }
          {
            name: 'AzureAdB2C__Instance'
            value: 'https://stallionsaustralia.b2clogin.com'
          }
          {
            name: 'AzureAdB2C__Domain'
            value: b2cDomain
          }
          {
            name: 'AzureAdB2C__TenantId'
            value: b2cTenantId
          }
          {
            name: 'AzureAdB2C__ClientId'
            value: b2cClientId
          }
          {
            name: 'AzureAdB2C__SignUpSignInPolicyId'
            value: 'B2C_1_susi'
          }
          {
            name: 'AZURE_STORAGE_ACCOUNT_NAME'
            value: storageAccountName
          }
        ]
      }
    }
  }

  output principalId string = appService.identity.principalId
  output appServiceName string = appService.name
  output appServiceHostname string = appService.properties.defaultHostName
  ```

- [ ] **Step 2: Update `main.bicep`**

  File: `infra/main.bicep`

  Replace the Entra var block with B2C vars. Fill in real values from Task 5 notes. Replace `YOUR_B2C_TENANT_ID`, `YOUR_DEV_CLIENT_ID`, `YOUR_PROD_CLIENT_ID` with the actual GUIDs:

  ```bicep
  // Azure AD B2C — one tenant serves both environments.
  // Client IDs are not secrets; they are embedded in the public MSAL config.
  var b2cTenantId = 'YOUR_B2C_TENANT_ID'
  var b2cDomain   = 'stallionsaustralia.onmicrosoft.com'
  var b2cClientId = environmentName == 'prod'
    ? 'YOUR_PROD_CLIENT_ID'
    : 'YOUR_DEV_CLIENT_ID'
  ```

  And update the `appservice` module call to pass the new params:

  ```bicep
  module appservice './modules/appservice.bicep' = {
    name: 'appservice'
    scope: rg
    params: {
      environmentName: environmentName
      location: location
      tags: tags
      appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
      keyVaultUri: keyvault.outputs.keyVaultUri
      b2cTenantId: b2cTenantId
      b2cDomain: b2cDomain
      b2cClientId: b2cClientId
      storageAccountName: storage.outputs.storageAccountName
    }
  }
  ```

  Also remove the now-unused `entraTenantId` and `entraClientId` var declarations from `main.bicep`.

- [ ] **Step 3: Validate Bicep**

  ```bash
  az bicep build --file infra/main.bicep
  ```

  Expected: no errors.

- [ ] **Step 4: Commit**

  ```bash
  git add infra/modules/appservice.bicep infra/main.bicep
  git commit -m "infra: replace Entra app settings with Azure AD B2C in appservice Bicep"
  ```

---

## Phase 5 — Client: MSAL and UserStateService

### Task 11: Update Client MSAL Configuration

**Files:**
- Modify: `src/Client/wwwroot/appsettings.json`
- Modify: `src/Client/Program.cs`

- [ ] **Step 1: Update `appsettings.json` (client)**

  File: `src/Client/wwwroot/appsettings.json`

  Replace the `AzureAd` section. Use the dev client ID from Task 5 here — this file is the default used locally. Replace `YOUR_DEV_CLIENT_ID`:

  ```json
  {
    "AzureAdB2C": {
      "Authority": "https://stallionsaustralia.b2clogin.com/stallionsaustralia.onmicrosoft.com/B2C_1_susi",
      "ClientId": "YOUR_DEV_CLIENT_ID",
      "ValidateAuthority": false
    },
    "ApiScope": "https://stallionsaustralia.onmicrosoft.com/stallions-api/API.Access",
    "ApiBaseUrl": ""
  }
  ```

  `ValidateAuthority: false` is required — B2C authority URLs do not pass MSAL's standard Entra authority validation.

- [ ] **Step 2: Update `appsettings.Production.json` (client)**

  File: `src/Client/wwwroot/appsettings.Production.json`

  The production client overrides only `ApiBaseUrl` and `ClientId`. Replace `YOUR_PROD_CLIENT_ID`:

  ```json
  {
    "AzureAdB2C": {
      "ClientId": "YOUR_PROD_CLIENT_ID"
    },
    "ApiBaseUrl": ""
  }
  ```

- [ ] **Step 3: Update `Program.cs` (client)**

  File: `src/Client/Program.cs`

  Change the config binding from `"AzureAd"` to `"AzureAdB2C"`:

  ```csharp
  builder.Services
      .AddMsalAuthentication<RemoteAuthenticationState, CustomUserAccount>(options =>
      {
          builder.Configuration.Bind("AzureAdB2C", options.ProviderOptions.Authentication);  // was "AzureAd"
          options.ProviderOptions.DefaultAccessTokenScopes.Add(builder.Configuration["ApiScope"]!);
      })
      .AddAccountClaimsPrincipalFactory<
          RemoteAuthenticationState,
          CustomUserAccount,
          CustomAccountClaimsPrincipalFactory>();
  ```

  Everything else in `Program.cs` stays the same.

- [ ] **Step 4: Build to verify**

  ```bash
  dotnet build src/Client/Stallions.Client.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit**

  ```bash
  git add src/Client/wwwroot/appsettings.json \
          src/Client/wwwroot/appsettings.Production.json \
          src/Client/Program.cs
  git commit -m "feat: switch client MSAL config to Azure AD B2C authority"
  ```

---

### Task 12: Simplify `CustomUserAccount` and `CustomAccountClaimsPrincipalFactory`

**Files:**
- Modify: `src/Client/Auth/CustomUserAccount.cs`
- Modify: `src/Client/Auth/CustomAccountClaimsPrincipalFactory.cs`

- [ ] **Step 1: Update `CustomUserAccount.cs`**

  File: `src/Client/Auth/CustomUserAccount.cs`

  Remove the `Roles` property — B2C tokens have no role claims. The class still needs to exist to satisfy the generic constraint in `AddMsalAuthentication<RemoteAuthenticationState, CustomUserAccount>`:

  ```csharp
  using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

  namespace Stallions.Client.Auth;

  /// <summary>
  /// Extends RemoteUserAccount for B2C. No role claims are present in B2C tokens —
  /// roles are determined from the database and accessed via UserStateService.
  /// This class exists to satisfy the MSAL generic type constraint.
  /// </summary>
  public class CustomUserAccount : RemoteUserAccount
  {
  }
  ```

- [ ] **Step 2: Update `CustomAccountClaimsPrincipalFactory.cs`**

  File: `src/Client/Auth/CustomAccountClaimsPrincipalFactory.cs`

  Simplify to a pass-through — no role mapping needed:

  ```csharp
  using System.Security.Claims;
  using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
  using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

  namespace Stallions.Client.Auth;

  /// <summary>
  /// Pass-through factory required by the MSAL pipeline. No role mapping is performed —
  /// roles are DB-backed and accessed via UserStateService after login.
  /// </summary>
  public class CustomAccountClaimsPrincipalFactory
      : AccountClaimsPrincipalFactory<CustomUserAccount>
  {
      public CustomAccountClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor)
          : base(accessor) { }

      public override async ValueTask<ClaimsPrincipal> CreateUserAsync(
          CustomUserAccount account,
          RemoteAuthenticationUserOptions options)
      {
          return await base.CreateUserAsync(account, options);
      }
  }
  ```

- [ ] **Step 3: Build to verify**

  ```bash
  dotnet build src/Client/Stallions.Client.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

  ```bash
  git add src/Client/Auth/CustomUserAccount.cs src/Client/Auth/CustomAccountClaimsPrincipalFactory.cs
  git commit -m "refactor: simplify MSAL account factory — roles now DB-backed via UserStateService"
  ```

---

### Task 13: Add `UserStateService` and Wire into `MainLayout`

**Files:**
- Create: `src/Client/Services/UserStateService.cs`
- Modify: `src/Client/Program.cs`
- Modify: `src/Client/Layout/MainLayout.razor`

- [ ] **Step 1: Create `UserStateService.cs`**

  File: `src/Client/Services/UserStateService.cs`

  ```csharp
  using Stallions.Shared.DTOs.Users;

  namespace Stallions.Client.Services;

  /// <summary>
  /// Fetches the current user's profile (including DB role) from the API once after
  /// authentication and caches it for the session. Components use this instead of
  /// JWT role claims, since B2C tokens carry no role information.
  /// </summary>
  public class UserStateService
  {
      private readonly UserApiService _api;

      public UserStateService(UserApiService api) => _api = api;

      public UserDto? CurrentUser { get; private set; }

      public string? Role => CurrentUser?.Role;
      public bool IsStaff        => Role == "Staff";
      public bool IsStudFarmAdmin => Role == "StudFarmAdmin";
      public bool IsBuyer        => Role == "Buyer";
      public bool IsLoaded       => CurrentUser is not null;

      /// <summary>
      /// Fetches the user profile from the API if not already loaded.
      /// Safe to call multiple times — only hits the network once per session.
      /// </summary>
      public async Task LoadAsync()
      {
          if (CurrentUser is null)
              CurrentUser = await _api.GetMeAsync();
      }

      /// <summary>
      /// Clears the cached user — call on sign-out.
      /// </summary>
      public void Clear() => CurrentUser = null;
  }
  ```

- [ ] **Step 2: Register `UserStateService` in `Program.cs`**

  File: `src/Client/Program.cs`

  Add after the existing service registrations (before `await builder.Build().RunAsync()`):

  ```csharp
  builder.Services.AddScoped<UserStateService>();
  ```

- [ ] **Step 3: Update `MainLayout.razor`**

  File: `src/Client/Layout/MainLayout.razor`

  Inject `AuthenticationStateProvider` and `UserStateService`, then load the user state on first authenticated render:

  ```razor
  @inherits LayoutComponentBase
  @inject AuthenticationStateProvider AuthProvider
  @inject UserStateService UserState

  <div class="page-wrapper">
      <NavBar />
      <main class="content-area">
          <ErrorBoundary>
              <ChildContent>
                  @Body
              </ChildContent>
              <ErrorContent Context="ex">
                  <div class="container" style="padding-top: var(--space-12)">
                      <div class="error-boundary">
                          <h2>Something went wrong</h2>
                          <p>An unexpected error occurred. Please <a href="/">return to listings</a> or try again.</p>
                      </div>
                  </div>
              </ErrorContent>
          </ErrorBoundary>
      </main>
      <Footer />
  </div>

  @code {
      protected override async Task OnInitializedAsync()
      {
          var authState = await AuthProvider.GetAuthenticationStateAsync();
          if (authState.User.Identity?.IsAuthenticated == true)
              await UserState.LoadAsync();
      }
  }
  ```

- [ ] **Step 4: Build to verify**

  ```bash
  dotnet build src/Client/Stallions.Client.csproj --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit**

  ```bash
  git add src/Client/Services/UserStateService.cs \
          src/Client/Program.cs \
          src/Client/Layout/MainLayout.razor
  git commit -m "feat: add UserStateService for DB-backed role checks in Blazor WASM client"
  ```

---

### Task 14: Update NavBar to Use `UserStateService`

**Files:**
- Modify: `src/Client/Layout/NavBar.razor`

- [ ] **Step 1: Update `NavBar.razor`**

  File: `src/Client/Layout/NavBar.razor`

  Replace the role-based `<AuthorizeView Roles="...">` wrappers with `@if (UserState.IsStaff)` / `@if (UserState.IsStudFarmAdmin)` checks. The outer `<AuthorizeView>` (no Roles attribute) stays — it gates the entire authenticated nav section:

  ```razor
  @inject NavigationManager Nav
  @inject UserStateService UserState

  <nav class="navbar">
      <div class="container navbar-inner">
          <a href="/" class="navbar-brand">
              <img src="images/logo.png" alt="Stallions Nominations Marketplace" class="navbar-logo" />
          </a>

          <div class="navbar-links desktop-only">
              <NavLink href="/" Match="NavLinkMatch.All" class="nav-link">Browse</NavLink>
          </div>

          <div class="navbar-auth desktop-only">
              <AuthorizeView>
                  <Authorized>
                      @if (UserState.IsStaff)
                      {
                          <NavLink href="/staff/dashboard" class="nav-link">Staff Admin</NavLink>
                      }
                      @if (UserState.IsStudFarmAdmin)
                      {
                          <NavLink href="/admin/stallions" class="nav-link">My Stud Farm</NavLink>
                      }
                      <NavLink href="/my-bids"      class="nav-link">My Bids</NavLink>
                      <NavLink href="/my-purchases" class="nav-link">My Purchases</NavLink>
                      <NavLink href="/enquiries"    class="nav-link">Messages</NavLink>
                      <button class="btn btn-outline btn-sm" @onclick="SignOut">Sign out</button>
                  </Authorized>
                  <NotAuthorized>
                      <a href="authentication/login"    class="btn btn-outline-white btn-sm">Sign in</a>
                      <a href="authentication/register" class="btn btn-gold btn-sm">Register to Buy</a>
                      <a href="/list-with-us"           class="btn btn-outline-white btn-sm">List Your Stallions</a>
                  </NotAuthorized>
              </AuthorizeView>
          </div>

          <button class="navbar-hamburger mobile-only" type="button" @onclick="ToggleDrawer" aria-label="Menu" aria-expanded="@_drawerOpen">
              <span></span>
              <span></span>
              <span></span>
          </button>
      </div>

      @if (_drawerOpen)
      {
          <div class="navbar-backdrop" @onclick="CloseDrawer"></div>
          <div class="navbar-drawer">
              <NavLink href="/" Match="NavLinkMatch.All" class="drawer-link" @onclick="CloseDrawer">Browse</NavLink>
              <AuthorizeView>
                  <Authorized>
                      @if (UserState.IsStaff)
                      {
                          <NavLink href="/staff/dashboard" class="drawer-link" @onclick="CloseDrawer">Staff Admin</NavLink>
                      }
                      @if (UserState.IsStudFarmAdmin)
                      {
                          <NavLink href="/admin/stallions" class="drawer-link" @onclick="CloseDrawer">My Stud Farm</NavLink>
                      }
                      <NavLink href="/my-bids"      class="drawer-link" @onclick="CloseDrawer">My Bids</NavLink>
                      <NavLink href="/my-purchases" class="drawer-link" @onclick="CloseDrawer">My Purchases</NavLink>
                      <NavLink href="/enquiries"    class="drawer-link" @onclick="CloseDrawer">Messages</NavLink>
                      <button class="btn btn-outline btn-full" @onclick="SignOut">Sign out</button>
                  </Authorized>
                  <NotAuthorized>
                      <a href="authentication/login"    class="btn btn-outline btn-full" @onclick="CloseDrawer">Sign in</a>
                      <a href="authentication/register" class="btn btn-gold btn-full"    @onclick="CloseDrawer">Register to Buy</a>
                      <a href="/list-with-us"           class="btn btn-outline btn-full" @onclick="CloseDrawer">List Your Stallions</a>
                  </NotAuthorized>
              </AuthorizeView>
          </div>
      }
  </nav>

  @code {
      private bool _drawerOpen;
      private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
      private void CloseDrawer()  => _drawerOpen = false;
      private void SignOut()
      {
          UserState.Clear();
          CloseDrawer();
          Nav.NavigateToLogout("authentication/logout");
      }
  }
  ```

  Note: `UserState.Clear()` is called on sign-out so the cached role is wiped before the next user logs in on the same browser session.

- [ ] **Step 2: Build the full solution**

  ```bash
  dotnet build --nologo
  ```

  Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Verify no `AuthorizeView Roles=` remain in the client**

  ```bash
  grep -rn "AuthorizeView Roles" src/Client/
  ```

  Expected output: nothing (empty).

- [ ] **Step 4: Commit**

  ```bash
  git add src/Client/Layout/NavBar.razor
  git commit -m "feat: replace AuthorizeView Roles= with UserStateService in NavBar"
  ```

---

## Phase 6 — Deploy and Verify

### Task 15: Deploy to Dev, Bootstrap Staff Account, Smoke Test

**Files:** none (deploy + DB commands)

- [ ] **Step 1: Update App Service settings via `azd provision`**

  With the Bicep updated in Task 10, run:

  ```bash
  azd provision --environment dev
  ```

  This updates the App Service app settings to `AzureAdB2C__*`. Confirm in Azure Portal → App Service → Configuration that the new settings are present and the old `AzureAd__*` settings are gone.

- [ ] **Step 2: Deploy the application**

  ```bash
  azd deploy --environment dev
  ```

  Expected: `SUCCESS: Your application was deployed to Azure`

- [ ] **Step 3: Hard refresh the browser**

  Navigate to `https://app-stallions-noms-dev.azurewebsites.net` and do a hard refresh (Ctrl+Shift+R). This clears the cached WASM bundle and picks up the new MSAL config.

- [ ] **Step 4: Sign in with your new B2C account**

  Click **Sign in**. You should be redirected to the B2C login page (`stallionsaustralia.b2clogin.com`), not the Entra login page. Sign in with the Staff account you created in Task 5.

  On first login, your B2C account's `objectId` is provisioned in the `Users` table as `Role = Buyer, Status = PendingVerification`.

- [ ] **Step 5: Truncate the old Entra user row**

  Connect to the dev database (Azure Portal → SQL Database → Query editor, or via SSMS with your local IP whitelisted).

  The old Entra-linked user row has a different `ObjectId` than your new B2C account. Delete it:

  ```sql
  -- Verify first — you should see the old Entra OID row and your new B2C OID row
  SELECT Id, ObjectId, Email, Role, Status FROM Users;

  -- Delete the old Entra row (the one whose ObjectId matches your old Entra OID: a0f0f37a-...)
  DELETE FROM Users WHERE ObjectId = 'a0f0f37a-6cc4-416c-a5e2-03dcc2a0409b';
  ```

- [ ] **Step 6: Promote your account to Staff**

  Your B2C account was provisioned as `Buyer`. Promote it directly in the DB:

  ```sql
  -- Get your new B2C user's ID
  SELECT Id, ObjectId, Email, Role, Status FROM Users;

  -- Promote to Staff and set Active
  UPDATE Users
  SET Role = 'Staff', Status = 'Active', VerifiedAt = GETUTCDATE()
  WHERE Email = 'your-email@example.com';  -- replace with your actual email
  ```

- [ ] **Step 7: Sign out and sign back in**

  Sign out of the app, then sign back in with your B2C account. `UserState.LoadAsync()` fetches your updated profile. You should now see **Staff Admin** in the NavBar.

- [ ] **Step 8: Smoke test — Staff endpoints**

  - ✅ Navigate to `/staff/dashboard` — loads without redirect
  - ✅ Navigate to `/auth-debug` — shows B2C claims (`oid`, `emails`, `name`) with HTTP 200
  - ✅ Staff Admin nav link is visible in desktop and mobile drawer

- [ ] **Step 9: Smoke test — Buyer self-registration**

  Open a private/incognito browser tab and navigate to the app.

  - ✅ Click **Register to Buy** → redirected to B2C login page
  - ✅ Click **Sign up now** → B2C sign-up form appears with Display Name and Email fields
  - ✅ Complete registration → redirected back to the app, logged in
  - ✅ New user row appears in `Users` table with `Role = Buyer, Status = PendingVerification`
  - ✅ NavBar shows My Bids, My Purchases, Messages (but no Staff Admin or My Stud Farm)

- [ ] **Step 10: Smoke test — Google sign-in**

  In the same incognito tab, sign out and try **Register to Buy** → click **Google** on the B2C login page.

  - ✅ Google OAuth consent screen appears
  - ✅ After consent, redirected back to app and logged in
  - ✅ New user row in `Users` table with Google email, `Role = Buyer`

- [ ] **Step 11: Smoke test — suspended user**

  In the SQL query editor, suspend the test buyer account:

  ```sql
  UPDATE Users SET Status = 'Suspended' WHERE Email = 'test-buyer@example.com';
  ```

  Have the test buyer try to access `/my-bids` — they should get 403 (the `BuyerOnly` policy checks `Status == Active`).

- [ ] **Step 12: Final commit — push everything**

  ```bash
  git push
  ```

- [ ] **Step 13: Clean up (optional)**

  The old Entra app registration (`e168521b-e3b2-4220-912b-00affbacc4d9`) in the `badranad.onmicrosoft.com` tenant is no longer used. You can leave it or delete it from the Azure Portal — it will not affect anything.
