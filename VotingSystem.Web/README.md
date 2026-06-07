# 🗳️ VotingSystem.Web — Deployment Guide

> **Stack:** ASP.NET Core 8 MVC · Entity Framework Core · SignalR · Azure App Service · Azure SQL Database · Azure Active Directory (Microsoft Entra ID)

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Local Development Setup](#2-local-development-setup)
3. [Azure Prerequisites](#3-azure-prerequisites)
4. [Step 1 — Azure SQL Database](#step-1--azure-sql-database)
5. [Step 2 — Azure Active Directory App Registration](#step-2--azure-active-directory-app-registration)
6. [Step 3 — Azure App Service](#step-3--azure-app-service)
7. [Step 4 — Configure App Settings in Azure](#step-4--configure-app-settings-in-azure)
8. [Step 5 — Run EF Core Migrations Against Azure SQL](#step-5--run-ef-core-migrations-against-azure-sql)
9. [Step 6 — Deploy the Application](#step-6--deploy-the-application)
10. [Step 7 — Verify & Post-Deployment Checklist](#step-7--verify--post-deployment-checklist)
11. [Architecture Diagram](#architecture-diagram)
12. [Troubleshooting](#troubleshooting)

---

## 1. Project Overview

| Feature | Details |
|---|---|
| Framework | ASP.NET Core 8 MVC |
| Authentication | Dual mode — local cookie auth **or** Azure AD (OpenID Connect) |
| Real-time | SignalR (live vote result updates) |
| Database | SQLite (local dev) / Azure SQL (production) |
| ORM | Entity Framework Core 8 |

### Authentication Modes

- **Local (dev):** Cookie-based. Voters register/login with email + BCrypt password. Admin login via `LocalAdmin` credentials in `appsettings.json`.
- **Azure AD (production):** OpenID Connect via `Microsoft.Identity.Web`. Users are auto-assigned the `Admin` role if their email matches the `AdminUsers` list.

---

## 2. Local Development Setup

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (ASP.NET workload) **or** VS Code + C# Dev Kit

### Run Locally (SQLite — no extra setup needed)

```powershell
# From the repo root
cd VotingSystem.Web

# Restore packages
dotnet restore

# Apply SQLite migrations
dotnet ef database update

# Run
dotnet run
```

Browse to `http://localhost:5002`

Default local admin credentials (set in `appsettings.json`):
- **Email:** `admin@local.test`
- **Password:** `Admin123!`

---

## 3. Azure Prerequisites

You will need:

| Requirement | Notes |
|---|---|
| Azure Subscription | Free tier works for testing |
| Azure CLI | `winget install Microsoft.AzureCLI` |
| .NET 8 SDK | For publish + migration commands |
| EF Core CLI tools | `dotnet tool install --global dotnet-ef` |

### Login to Azure CLI

```powershell
az login
az account set --subscription "<YOUR_SUBSCRIPTION_ID>"
```

### Create a Resource Group

```powershell
$RG       = "voting-system-rg"
$LOCATION = "eastus"

az group create --name $RG --location $LOCATION
```

---

## Step 1 — Azure SQL Database

### 1.1 Create SQL Server and Database

```powershell
$SQL_SERVER = "votingsystem-sql"       # must be globally unique
$SQL_DB     = "VotingSystemDb"
$SQL_ADMIN  = "sqladmin"
$SQL_PASS   = "Str0ngP@ssword!"        # change this

# Create the logical SQL server
az sql server create `
  --name $SQL_SERVER `
  --resource-group $RG `
  --location $LOCATION `
  --admin-user $SQL_ADMIN `
  --admin-password $SQL_PASS

# Create the database (Basic tier — cheapest, upgrade as needed)
az sql db create `
  --resource-group $RG `
  --server $SQL_SERVER `
  --name $SQL_DB `
  --service-objective Basic

# Allow Azure services to access the SQL server
az sql server firewall-rule create `
  --resource-group $RG `
  --server $SQL_SERVER `
  --name AllowAzureServices `
  --start-ip-address 0.0.0.0 `
  --end-ip-address 0.0.0.0
```

### 1.2 Get Your Connection String

```powershell
az sql db show-connection-string `
  --server $SQL_SERVER `
  --name $SQL_DB `
  --client ado.net
```

Your connection string will look like:

```
Server=tcp:votingsystem-sql.database.windows.net,1433;
Initial Catalog=VotingSystemDb;
Persist Security Info=False;
User ID=sqladmin;
Password=Str0ngP@ssword!;
MultipleActiveResultSets=False;
Encrypt=True;
TrustServerCertificate=False;
Connection Timeout=30;
```

> **Save this** — you will paste it into App Service configuration in Step 4.

---

## Step 2 — Azure Active Directory App Registration

### 2.1 Register the Application

1. Open [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App registrations** → **New registration**

2. Fill in:
   | Field | Value |
   |---|---|
   | Name | `VotingSystem` |
   | Supported account types | **Accounts in this organizational directory only** (Single tenant) |
   | Redirect URI | Web → `https://<your-app>.azurewebsites.net/signin-oidc` |

3. Click **Register**.

### 2.2 Collect Required Values

After registration, note the following from the **Overview** page:

| Setting | Where to find it |
|---|---|
| `TenantId` | **Directory (tenant) ID** |
| `ClientId` | **Application (client) ID** |
| `Domain` | Your tenant domain e.g. `contoso.onmicrosoft.com` |

### 2.3 Configure Authentication Settings

1. In the app registration → **Authentication**
2. Under **Implicit grant and hybrid flows**, enable:
   - ✅ **ID tokens**
3. Add **Logout URL:** `https://<your-app>.azurewebsites.net/signout-oidc`
4. Click **Save**

### 2.4 (Optional) Create a Client Secret

If you need client-side tokens:

1. **Certificates & secrets** → **New client secret**
2. Set expiry (e.g., 24 months)
3. Copy the **Value** (shown only once) → store securely in Azure Key Vault or App Service config

### 2.5 Grant API Permissions

1. **API permissions** → **Add a permission** → **Microsoft Graph**
2. Add delegated permissions:
   - `User.Read`
   - `email`
   - `openid`
   - `profile`
3. Click **Grant admin consent**

---

## Step 3 — Azure App Service

### 3.1 Create the App Service Plan and Web App

```powershell
$APP_NAME = "votingsystem-web"          # must be globally unique
$PLAN     = "voting-plan"

# Create App Service Plan (B1 = Basic, cheapest paid tier with custom domain support)
az appservice plan create `
  --name $PLAN `
  --resource-group $RG `
  --sku B1 `
  --is-linux false

# Create the Web App targeting .NET 8
az webapp create `
  --resource-group $RG `
  --plan $PLAN `
  --name $APP_NAME `
  --runtime "DOTNET|8.0"
```

### 3.2 Enable Managed Identity (Recommended)

```powershell
az webapp identity assign `
  --resource-group $RG `
  --name $APP_NAME
```

> This allows passwordless auth to Azure SQL via Managed Identity (advanced — skip for simple username/password SQL auth).

---

## Step 4 — Configure App Settings in Azure

Set all secrets as **Application Settings** (environment variables) in App Service — **do NOT commit secrets to `appsettings.json`**.

```powershell
az webapp config appsettings set `
  --resource-group $RG `
  --name $APP_NAME `
  --settings `
    ASPNETCORE_ENVIRONMENT="Production" `
    "ConnectionStrings__AzureSqlConnection=Server=tcp:<your-server>.database.windows.net,1433;Initial Catalog=VotingSystemDb;Persist Security Info=False;User ID=sqladmin;Password=<password>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" `
    "AzureAd__Instance=https://login.microsoftonline.com/" `
    "AzureAd__Domain=<your-tenant-domain>" `
    "AzureAd__TenantId=<your-tenant-id>" `
    "AzureAd__ClientId=<your-client-id>" `
    "AzureAd__CallbackPath=/signin-oidc" `
    "AdminUsers__0=admin@yourdomain.com"
```

> **Note:** Azure App Service uses `__` (double underscore) as the hierarchy separator for nested JSON keys (e.g., `AzureAd:TenantId` → `AzureAd__TenantId`).

### Setting Reference Table

| App Setting Key | Where to get value |
|---|---|
| `ConnectionStrings__AzureSqlConnection` | Step 1.2 |
| `AzureAd__TenantId` | Entra ID → App Registration → Overview |
| `AzureAd__ClientId` | Entra ID → App Registration → Overview |
| `AzureAd__Domain` | Your Entra tenant domain |
| `AzureAd__CallbackPath` | `/signin-oidc` (fixed) |
| `AdminUsers__0` | Email address of the first admin user |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

---

## Step 5 — Run EF Core Migrations Against Azure SQL

Run this from your **local machine** (with `AzureSqlConnection` temporarily in your env or `appsettings.Production.json`):

```powershell
# Option A: Pass connection string via environment variable
$env:ConnectionStrings__AzureSqlConnection = "Server=tcp:<your-server>.database.windows.net,1433;..."
$env:ASPNETCORE_ENVIRONMENT = "Production"

cd VotingSystem.Web
dotnet ef database update
```

```powershell
# Option B: Add your IP to SQL firewall first, then run
az sql server firewall-rule create `
  --resource-group $RG `
  --server $SQL_SERVER `
  --name MyDevMachine `
  --start-ip-address <YOUR_PUBLIC_IP> `
  --end-ip-address <YOUR_PUBLIC_IP>

dotnet ef database update
```

> ✅ This creates all the tables (Elections, Candidates, Voters, Votes) in Azure SQL.

---

## Step 6 — Deploy the Application

### Option A: Deploy via ZIP (Recommended for Project Submission)

```powershell
# Publish the app to a local folder
dotnet publish VotingSystem.Web/VotingSystem.Web.csproj `
  -c Release -o ./publish

# Zip the publish output
Compress-Archive -Path ./publish/* -DestinationPath deploy.zip -Force

# Deploy to Azure App Service
az webapp deploy `
  --resource-group $RG `
  --name $APP_NAME `
  --src-path deploy.zip `
  --type zip
```

### Option B: Deploy via Visual Studio

1. Right-click project → **Publish**
2. Select **Azure** → **Azure App Service (Windows)**
3. Select the App Service created in Step 3
4. Click **Publish**

### Option C: GitHub Actions CI/CD

1. In Azure Portal → App Service → **Deployment Center**
2. Select **GitHub** as source
3. Authorize and select your repository/branch
4. Azure auto-generates a GitHub Actions workflow file
5. Push to your branch → auto-deploys

---

## Step 7 — Verify & Post-Deployment Checklist

After deploying, browse to `https://<your-app>.azurewebsites.net` and verify:

- [ ] Home page loads with dark navy theme
- [ ] `/Admin/Login` — Local admin login works (if `LocalAdmin` is set in App Settings)
- [ ] `/Voter/Login` — Voter registration and login works
- [ ] `/Account/SignIn` — Azure AD login redirects to Microsoft login page
- [ ] After Azure AD login, user with email in `AdminUsers` gets admin access
- [ ] Casting a vote on an active election triggers real-time result update via SignalR
- [ ] Election creation, candidate management work in the Admin panel

### View Application Logs

```powershell
# Stream live logs
az webapp log tail `
  --resource-group $RG `
  --name $APP_NAME

# Enable logging first if not already enabled
az webapp log config `
  --resource-group $RG `
  --name $APP_NAME `
  --application-logging filesystem `
  --level information
```

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                     Azure Cloud                          │
│                                                         │
│  ┌──────────────────┐    ┌─────────────────────────┐   │
│  │  Azure Active    │    │   Azure App Service      │   │
│  │  Directory       │◄───│   (VotingSystem.Web)     │   │
│  │  (Entra ID)      │    │   .NET 8 / ASP.NET MVC   │   │
│  │                  │    │   + SignalR Hub           │   │
│  │  App Registration│    └────────────┬────────────┘   │
│  │  OpenID Connect  │                 │                 │
│  └──────────────────┘                 │ EF Core         │
│                                       ▼                 │
│                          ┌─────────────────────────┐   │
│                          │   Azure SQL Database     │   │
│                          │                         │   │
│                          │   Elections             │   │
│                          │   Candidates            │   │
│                          │   Voters                │   │
│                          │   Votes                 │   │
│                          └─────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
           ▲
           │ HTTPS + OpenID Connect
           │
    [Browser / Voter]
```

---

## Troubleshooting

| Issue | Likely Cause | Fix |
|---|---|---|
| App shows 500 error | Missing App Setting | Check all required `AzureAd__*` and `ConnectionStrings__*` are set |
| Azure AD login fails | Redirect URI mismatch | Ensure `https://<app>.azurewebsites.net/signin-oidc` is in Entra ID app registration |
| DB connection fails | IP not whitelisted | Add App Service outbound IPs to SQL firewall |
| Admin role not assigned | Email not in `AdminUsers` | Add email to `AdminUsers__0` App Setting |
| SignalR not working | WebSockets disabled | Azure Portal → App Service → Configuration → **General settings** → WebSockets: **On** |
| Migrations fail locally | Wrong env or connection string | Set `ASPNETCORE_ENVIRONMENT=Production` and verify connection string |

### Enable WebSockets for SignalR

```powershell
az webapp config set `
  --resource-group $RG `
  --name $APP_NAME `
  --web-sockets-enabled true
```

---

## Quick Reference — All Resources Created

| Azure Resource | Name Variable | Purpose |
|---|---|---|
| Resource Group | `voting-system-rg` | Container for all resources |
| SQL Server | `votingsystem-sql` | Hosts the database |
| SQL Database | `VotingSystemDb` | Application data |
| App Service Plan | `voting-plan` | Hosting plan (B1) |
| Web App | `votingsystem-web` | The running application |
| Entra ID App Reg | `VotingSystem` | Azure AD authentication |

---

*Generated for project submission — Azure App Service · Azure SQL Database · Azure Active Directory*
