# Azure Infrastructure Design — Stallions Nominations Marketplace

**Date:** 2026-05-19
**Status:** Approved
**Author:** David Reichard

---

## Overview

Full Azure infrastructure for the Stallions Nominations Marketplace, provisioned via Bicep and managed by the Azure Developer CLI (`azd`). Two environments — dev and prod — each fully isolated in their own resource group. All resources in Australia East (Sydney).

The existing manually-created resource group `stallions-nominations-rg` will be deleted and replaced by IaC-provisioned resources. The `Stallions` resource group (www.stallions.com.au) is out of scope and will not be touched.

---

## Section 1: Resource Groups & Naming

Two resource groups, one per environment, both in Australia East:

| Environment | Resource Group | `azd` environment |
|---|---|---|
| Dev | `rg-stallions-noms-dev` | `dev` |
| Prod | `rg-stallions-noms-prod` | `prod` |

### Resource Naming

| Resource | Dev | Prod |
|---|---|---|
| App Service Plan | `plan-stallions-noms-dev` | `plan-stallions-noms-prod` |
| App Service | `app-stallions-noms-dev` | `app-stallions-noms-prod` |
| SQL Server | `sql-stallions-noms-dev` | `sql-stallions-noms-prod` |
| SQL Database | `sqldb-stallions-noms-dev` | `sqldb-stallions-noms-prod` |
| Storage Account | `ststallionsnomsdev` | `ststallionsnomsprod` |
| Key Vault | `kv-stallions-noms-dev` | `kv-stallions-noms-prod` |
| Function App | `func-stallions-noms-dev` | `func-stallions-noms-prod` |
| Application Insights | `appi-stallions-noms-dev` | `appi-stallions-noms-prod` |
| Log Analytics Workspace | `log-stallions-noms-dev` | `log-stallions-noms-prod` |

Parameter files: `infra/main.parameters.dev.json` and `infra/main.parameters.prod.json`. One `main.bicep` drives both environments.

---

## Section 2: Resource Configuration

| Resource | Dev | Prod |
|---|---|---|
| **App Service Plan** | Basic B2 | Basic B2 |
| **App Service** | .NET 9, always-on off | .NET 9, always-on on |
| **SQL Database** | General Purpose Serverless, 1–4 vCores, auto-pause 60 min | General Purpose Provisioned, 2 vCores |
| **Storage Account** | LRS, Standard | LRS, Standard |
| **Key Vault** | Standard tier, RBAC-based access | Standard tier, RBAC-based access |
| **Function App** | Consumption plan (Y1) | Consumption plan (Y1) |
| **Application Insights** | Connected to Log Analytics | Connected to Log Analytics |

### Key Design Decisions

- **Managed Identity** — App Service and Function App authenticate to SQL, Key Vault, and Storage via system-assigned managed identity. No connection strings stored anywhere.
- **Key Vault RBAC** — Modern RBAC-based access model rather than legacy access policies.
- **Consumption plan for Functions** — Transaction volume is too low to justify a dedicated plan. Cold starts are acceptable for background tasks (notifications, data processing).
- **LRS storage** — Locally redundant storage is sufficient for blob content (images, documents). SQL General Purpose tier provides built-in data durability.

---

## Section 3: Authentication & Entra ID

Two Entra ID app registrations, one per environment:

| | Dev | Prod |
|---|---|---|
| **App Registration** | `stallions-noms-dev` | `stallions-noms-prod` |
| **Redirect URIs** | `https://app-stallions-noms-dev.azurewebsites.net/authentication/login-callback` | `https://app-stallions-noms-prod.azurewebsites.net/authentication/login-callback` (+ custom domain) |
| **Token type** | Access + ID tokens | Access + ID tokens |

### Role Claims

The three authenticated roles from CLAUDE.md are defined as app roles on the Entra ID app registration and assigned to users/groups in the portal (General Public browse access requires no login):

- `buyer`
- `stud-farm-admin`
- `stallions-staff`

The API validates role claims on every request.

### Important

App registrations are **not** provisioned by Bicep — Entra ID resources require elevated permissions and are managed separately via the Azure portal or Entra ID admin centre. The client ID and tenant ID are stored as Key Vault secrets and referenced by the App Service and Function App via managed identity at runtime.

---

## Section 4: Networking & Security

Resources are internet-accessible with security enforced at the application and identity layer. No VNet or private endpoints in this phase — can be added later if required.

### Security Controls (enforced in Bicep)

| Control | Detail |
|---|---|
| **SQL Server firewall** | Azure services only; dev environment adds a rule for developer local IP |
| **Key Vault** | RBAC only; all reads require an Entra ID identity |
| **Storage Account** | Public blob access disabled; all access via managed identity or SAS tokens |
| **App Service** | HTTPS only enforced, minimum TLS 1.2, client affinity off |
| **Function App** | HTTPS only, TLS 1.2+; not publicly exposed — triggered by internal queue or timer |
| **Secrets** | All connection strings, client secrets, and API keys stored in Key Vault; referenced via Key Vault references in App Service app settings |

### Audit Logging

All resources emit diagnostic logs to the Log Analytics workspace via Azure Monitor diagnostic settings. This satisfies the audit log requirement from CLAUDE.md for financial transactions and admin actions.

---

## Section 5: Bicep File Structure

```
infra/
  main.bicep                        # Entry point — orchestrates all modules
  main.parameters.dev.json          # Dev environment parameter values
  main.parameters.prod.json         # Prod environment parameter values
  modules/
    appservice.bicep                # App Service Plan + App Service
    sql.bicep                       # SQL Server + Database
    storage.bicep                   # Storage Account
    keyvault.bicep                  # Key Vault + RBAC role assignments
    functions.bicep                 # Function App + Consumption Plan
    monitoring.bicep                # Log Analytics + Application Insights
```

`main.bicep` accepts `environmentName` and `location` as top-level parameters, passes them into each module, and wires outputs (e.g. Key Vault URI → App Service app settings). Each module is self-contained.

`azure.yaml` is updated to point `azd` at the `infra/` folder:

```yaml
infra:
  provider: bicep
  path: infra
```

### Deployment Commands

```bash
# Provision dev
azd up --environment dev

# Provision prod
azd up --environment prod

# Tear down dev
azd down --environment dev
```

---

## Pre-Implementation Checklist

- [ ] Note any secrets currently stored in `stallions-nominations-kv` before deletion
- [ ] Delete `stallions-nominations-rg` resource group
- [ ] Create Entra ID app registrations for dev and prod manually
- [ ] Write Bicep modules
- [ ] Run `azd up --environment dev` to validate
- [ ] Run `azd up --environment prod`
