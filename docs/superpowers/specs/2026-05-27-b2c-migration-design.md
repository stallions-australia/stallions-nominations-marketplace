# Azure AD B2C Migration Design

## Goal

Replace the current single-tenant Entra ID authentication with Azure AD B2C, enabling buyers to self-register via email/password or Google sign-in. All user types (Buyer, StudFarm Admin, Staff) authenticate through B2C. Roles are determined from the application database, not from JWT claims.

## Architecture

A single B2C tenant (`stallionsaustralia.onmicrosoft.com`) serves both dev and prod via separate app registrations. One combined sign-up/sign-in user flow (`B2C_1_susi`) handles all authentication. The server validates B2C JWTs using `Microsoft.Identity.Web`'s B2C support. Role-based authorisation moves from JWT claim inspection to database-backed authorization policies. The client fetches the user's role from the API after login and stores it in a lightweight `UserStateService`.

## Tech Stack

- Azure AD B2C (standard User Flows — no Custom Policies / Identity Experience Framework)
- Microsoft.Identity.Web (existing NuGet, has built-in B2C support)
- Microsoft Authentication Library (MSAL) for Blazor WASM (existing)
- Google OAuth 2.0 as a B2C identity provider

---

## Section 1: B2C Tenant and App Registrations

### Tenant

- **Tenant name:** `stallionsaustralia.onmicrosoft.com` (chosen at creation — verify availability)
- **Subscription:** existing Azure subscription
- One tenant serves both dev and prod environments

### App registrations

Two registrations in the B2C tenant — one per environment:

| Setting | Dev | Prod |
|---|---|---|
| Name | `stallions-dev` | `stallions-prod` |
| Login redirect | `https://localhost:7001/authentication/login-callback` and `https://app-stallions-noms-dev.azurewebsites.net/authentication/login-callback` | `https://app-stallions-noms-prod.azurewebsites.net/authentication/login-callback` |
| Logout redirect | `https://localhost:7001/authentication/logout-callback` and `https://app-stallions-noms-dev.azurewebsites.net/authentication/logout-callback` | `https://app-stallions-noms-prod.azurewebsites.net/authentication/logout-callback` |
| Token grant | Authorization code + PKCE only (implicit grant disabled) | same |
| Exposed scope | `https://stallionsaustralia.onmicrosoft.com/stallions-api/API.Access` | same scope, different client ID |

### User flow

One shared user flow across both environments:

- **Name:** `B2C_1_susi` (combined sign-up and sign-in, v2)
- **Sign-up attributes collected:** Display Name, Email Address
- **Return claims:** `objectId`, `displayName`, `emails`, `identityProvider`
- **Identity providers:** Local account (email + password) + Google

### Google identity provider

- Create a Google Cloud Console project (`stallions-nominations-marketplace`)
- Create an OAuth 2.0 client ID (web application)
- Authorised redirect URI: `https://stallionsaustralia.b2clogin.com/stallionsaustralia.onmicrosoft.com/oauth2/authresp`
- Register the resulting client ID and secret in B2C as the Google identity provider

### Token format

B2C tokens are standard JWTs. Key claims used by the application:

| Claim | Value | Usage |
|---|---|---|
| `oid` | B2C user object ID (GUID) | User identity — stored in `Users.ObjectId` |
| `emails` | Array, `emails[0]` is primary | Email address for user provisioning |
| `name` | Display name string | Display name for user provisioning |
| `identityProvider` | `google.com` or absent (local account) | Informational only |
| `iss` | `https://stallionsaustralia.b2clogin.com/<tenantId>/v2.0/` | Validated by server |
| `aud` | B2C app client ID | Validated by server |

No `roles` claim is present or expected — role authority is the database.

---

## Section 2: Server-side changes

### Auth configuration

`appsettings.json` replaces the `AzureAd` section with `AzureAdB2C`:

```json
"AzureAdB2C": {
  "Instance": "https://stallionsaustralia.b2clogin.com",
  "Domain": "stallionsaustralia.onmicrosoft.com",
  "TenantId": "PLACEHOLDER_SET_IN_ENVIRONMENT",
  "ClientId": "PLACEHOLDER_SET_IN_ENVIRONMENT",
  "SignUpSignInPolicyId": "B2C_1_susi"
}
```

`Program.cs` binds this section to `AddMicrosoftIdentityWebApi` — the same NuGet package, just pointed at the B2C config section. No additional packages required.

### `CurrentUserService` changes

Two changes:

1. `EntraObjectId` renamed to `ObjectId` (holds a B2C OID going forward)
2. Email claim lookup updated — B2C uses `emails` not `preferred_username`:

```csharp
public string? ObjectId =>
    User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    ?? User?.FindFirst("oid")?.Value;

public string? Email =>
    User?.FindFirst("emails")?.Value    // B2C email/password and Google
    ?? User?.FindFirst("email")?.Value; // fallback
```

The `Roles` property is removed. `ICurrentUserService` is updated to match.

### Authorization policies

Three named policies replace all `[Authorize(Roles = "...")]` attributes. Each policy uses a custom `IAuthorizationHandler` that fetches the current user from the database and checks their `UserRole` and `UserStatus`:

| Policy | Passes when |
|---|---|
| `StaffOnly` | `user.Role == Staff && user.Status == Active` |
| `StudFarmAdminOnly` | `user.Role == StudFarmAdmin && user.Status == Active` |
| `StudFarmOrStaff` | `(user.Role == StudFarmAdmin || user.Role == Staff) && user.Status == Active` |

The `Active` status check in the policy means suspended users are blocked at the policy layer across all protected endpoints, not just checkout. Unauthenticated requests still return 401 before the handler runs.

Controller attribute mapping:

| Old | New |
|---|---|
| `[Authorize(Roles = "Staff")]` | `[Authorize(Policy = "StaffOnly")]` |
| `[Authorize(Roles = "StudFarmAdmin,Staff")]` | `[Authorize(Policy = "StudFarmOrStaff")]` |
| `[Authorize]` | `[Authorize]` — unchanged |
| `[AllowAnonymous]` | `[AllowAnonymous]` — unchanged |

### EF Core migration

`Users.EntraObjectId` column renamed to `Users.ObjectId`. One EF migration. Safe to apply on deploy — no data transformation required (the column holds the same GUID format; B2C OIDs are the same shape as Entra OIDs).

**Dev Users table:** After first B2C login, the old Entra-linked row is unreachable (different OID). Truncate the `Users` table in dev after verifying B2C login works.

### `AuthDebugController`

Updated to display B2C-relevant claims (`oid`, `emails`, `name`, `identityProvider`) instead of the Entra-specific set. Still gated behind `[Authorize]` only — any authenticated B2C user can access it for debugging.

---

## Section 3: Client-side changes

### MSAL configuration

`appsettings.json` (client `wwwroot`) updated to B2C authority:

```json
"AzureAdB2C": {
  "Authority": "https://stallionsaustralia.b2clogin.com/stallionsaustralia.onmicrosoft.com/B2C_1_susi",
  "ClientId": "PLACEHOLDER",
  "ValidateAuthority": false
},
"ApiScope": "https://stallionsaustralia.onmicrosoft.com/stallions-api/API.Access",
"ApiBaseUrl": ""
```

`ValidateAuthority: false` is required — B2C authority URLs do not match MSAL's built-in validator pattern for standard Entra authorities.

### `Program.cs`

One change: `builder.Configuration.Bind("AzureAd", ...)` becomes `builder.Configuration.Bind("AzureAdB2C", ...)`. All typed HTTP client registrations and the `CustomAccountClaimsPrincipalFactory` wiring are unchanged.

### `CustomUserAccount` and `CustomAccountClaimsPrincipalFactory`

`CustomUserAccount.Roles` property removed — no role claims in the B2C token.

`CustomAccountClaimsPrincipalFactory.CreateUserAsync` simplified to a pass-through (calls `base` only). The class must remain to satisfy the generic type constraints in `AddMsalAuthentication<RemoteAuthenticationState, CustomUserAccount>`.

### `UserStateService` — new

A scoped service that fetches `/api/users/me` once after authentication and caches the result for the lifetime of the browser session. All role-based UI decisions go through this service.

```csharp
public class UserStateService
{
    private readonly UserApiService _api;
    public UserDto? CurrentUser { get; private set; }
    public string? Role => CurrentUser?.Role;
    public bool IsStaff => Role == "Staff";
    public bool IsStudFarmAdmin => Role == "StudFarmAdmin";
    public bool IsBuyer => Role == "Buyer";

    public async Task LoadAsync()
    {
        if (CurrentUser is null)
            CurrentUser = await _api.GetMeAsync();
    }
}
```

Registered as `Scoped` in `Program.cs`. `MainLayout.OnInitializedAsync` calls `UserState.LoadAsync()` when the user is authenticated, ensuring the role is available to all child components without individual API calls.

### NavBar and role-gated UI

`<AuthorizeView Roles="...">` replaced with `@if (UserState.IsStaff)` / `@if (UserState.IsStudFarmAdmin)` pattern:

```razor
// Before
<AuthorizeView Roles="Staff" Context="_staff">
    <NavLink href="/staff">Staff Admin</NavLink>
</AuthorizeView>

// After
@if (UserState.IsStaff)
{
    <NavLink href="/staff">Staff Admin</NavLink>
}
```

`<AuthorizeView>` without `Roles=` is unchanged everywhere — it continues to gate the "sign in / sign out" display and page-level access for authenticated vs anonymous users.

---

## Section 4: Infrastructure changes

### `appservice.bicep` — app setting rename

`AzureAd__*` settings replaced with `AzureAdB2C__*`:

```
AzureAdB2C__Instance
AzureAdB2C__Domain
AzureAdB2C__TenantId
AzureAdB2C__ClientId
AzureAdB2C__SignUpSignInPolicyId
```

Values passed as Bicep parameters from `main.parameters.dev.json` and `main.parameters.prod.json`, same pattern as the current Entra settings.

### Manual steps (Azure Portal — one-time)

The following cannot be automated via Bicep and must be completed before the code changes are deployed:

1. Create the B2C tenant in Azure Portal
2. Create dev and prod app registrations with correct redirect URIs and exposed scope
3. Configure the `B2C_1_susi` user flow with the specified attributes and claims
4. Set up Google as an identity provider (requires Google Cloud Console OAuth app first)
5. Create Staff and StudFarmAdmin accounts directly in the B2C directory
6. Note all client IDs and the tenant ID for use in parameter files and Key Vault

The implementation plan has exact portal steps for each of these.

### Existing Entra app registration

The existing dev Entra registration (`e168521b-e3b2-4220-912b-00affbacc4d9`) can be left in place during transition and decommissioned after B2C is verified working. It will not receive any traffic once the app settings are updated.

---

## What does NOT change

- All other Azure infrastructure (SQL, Key Vault, Blob Storage, App Service plan, monitoring, Functions)
- `azd deploy` workflow
- CORS configuration
- The buyer verification requirement (`UserStatus.PendingVerification` → Staff verifies → `Active`)
- The `UserService.GetOrCreateCurrentUserAsync` auto-provisioning logic (just uses `ObjectId` instead of `EntraObjectId`)
- All business logic, repositories, and services below the auth layer
- The `[AllowAnonymous]` public endpoints (listings, stallions, stud farms)
