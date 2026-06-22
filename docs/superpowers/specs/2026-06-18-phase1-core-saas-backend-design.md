# Phase 1 — Core SaaS Backend Design
**Project:** FashionSaaS — Multi-Brand Fashion eCommerce SaaS Platform  
**Date:** 2026-06-18  
**Phase:** 1 of 8  
**Status:** Approved  

---

## 1. Overview

Phase 1 builds the foundational SaaS backend: multi-tenant isolation, authentication, role-based access control, tenant management, and subscription billing. No product, order, or storefront features are included — those come in later phases.

---

## 2. Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 |
| Framework | ASP.NET Core 10 Web API |
| ORM | Entity Framework Core (latest) |
| Database | SQL Server |
| Auth | JWT (15-minute access token) + Refresh Token (7-day, rotated, HttpOnly cookie) |
| Password hashing | BCrypt.Net-Next, work factor 12 |
| MFA | TOTP via `OtpNet` (Super Admin mandatory) |
| Field encryption | AES-256-GCM via `System.Security.Cryptography` (bank account fields) |
| Secrets management | Environment variables + Azure Key Vault (Phase 8); never in appsettings.json |
| Email | MailKit via SMTP |
| Background jobs | `BackgroundService` with `PeriodicTimer` (no Hangfire) |
| Rate limiting | ASP.NET Core built-in `RateLimiter` |

---

## 3. Solution Structure

```
FashionSaaS/
├── src/
│   ├── FashionSaaS.Domain/
│   ├── FashionSaaS.Application/
│   ├── FashionSaaS.Infrastructure/
│   └── FashionSaaS.API/
├── tests/
│   ├── FashionSaaS.Domain.Tests/
│   ├── FashionSaaS.Application.Tests/
│   └── FashionSaaS.Infrastructure.Tests/
└── docs/
    └── superpowers/specs/
```

### 3.1 Application Layer (Feature-Sliced)

```
Application/
├── Behaviors/          # MediatR pipeline: validation, logging, performance
├── Exceptions/         # NotFoundException, ForbiddenException, ValidationException
├── Interfaces/         # IRepository<T>, IUnitOfWork, IEmailService, ICurrentTenant
├── Specifications/     # Base specification classes
├── Common/             # ResponseData<T>, PagedResult<T>, shared mapping helpers
├── Auth/
│   ├── Commands/       # Login, RefreshToken, Logout, ChangePassword, ForgotPassword, ResetPassword
│   └── DTOs/
├── Tenants/
│   ├── Commands/       # CreateTenant, UpdateTenant, SuspendTenant, ActivateTenant, DeleteTenant
│   ├── Queries/        # GetAllTenants, GetTenantById, GetTenantBySlug
│   └── DTOs/
├── Users/
│   ├── Commands/       # CreateUser, UpdateUser, AssignRole, DeactivateUser, DeleteUser
│   ├── Queries/        # GetUsersByTenant, GetUserById
│   └── DTOs/
└── Subscriptions/
    ├── Commands/       # AssignSubscription, ChangePlan, SuspendSubscription, ReactivateSubscription, ConfirmPayment
    ├── Queries/        # GetSubscriptionByTenant, GetAllSubscriptions, GetAllPayments
    └── DTOs/
```

### 3.2 Infrastructure Layer

```
FashionSaaS.Infrastructure/
├── Persistence/
│   ├── ApplicationDbContext.cs
│   ├── Configurations/        # One IEntityTypeConfiguration<T> file per entity
│   ├── Repositories/          # GenericRepository<T> + entity-specific repositories
│   ├── UnitOfWork.cs
│   └── Migrations/
├── Services/
│   ├── CurrentTenantService.cs
│   ├── JwtService.cs
│   ├── PasswordHasher.cs
│   ├── SmtpEmailService.cs
│   ├── TotpService.cs               # TOTP generation + verification (Super Admin MFA)
│   └── FieldEncryptionService.cs    # AES-256-GCM encrypt/decrypt for bank fields
├── BackgroundJobs/
│   └── SubscriptionExpiryJob.cs
└── DependencyInjection.cs
```

### 3.3 API Layer

```
FashionSaaS.API/
├── Controllers/
│   ├── Admin/
│   │   ├── TenantsController.cs
│   │   ├── UsersController.cs
│   │   ├── SubscriptionPlansController.cs
│   │   ├── SubscriptionsController.cs
│   │   └── PaymentsController.cs
│   ├── Auth/
│   │   └── AuthController.cs
│   └── Tenant/
│       ├── TenantProfileController.cs
│       └── TenantUsersController.cs
├── Constants/
│   └── ApiUrl.cs
├── Middleware/
│   ├── TenantResolutionMiddleware.cs
│   ├── ExceptionHandlingMiddleware.cs
│   └── RateLimitingMiddleware.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
└── Program.cs
```

---

## 4. Domain Layer

### 4.1 Base Classes

```
BaseEntity
  Id         : Guid (generated on creation)
  CreatedAt  : DateTime
  UpdatedAt  : DateTime

TenantOwnedEntity : BaseEntity
  TenantId   : Guid
  (EF Core global query filter applied — all queries auto-scoped to current tenant)
```

### 4.2 Entities

**Tenant**
```
Id, Name, Slug (unique), Email, Phone
LogoUrl, CoverImageUrl
IsActive
CreatedAt, UpdatedAt
```

**User**
```
Id, TenantId (nullable — null = Super Admin)
FirstName, LastName, Email, PasswordHash
IsActive, IsEmailVerified
RefreshTokens → ICollection<RefreshToken>
CreatedAt, UpdatedAt
```

**RefreshToken**
```
Id, UserId
Token (BCrypt hash — never stored plain)
ExpiresAt, IsRevoked, RevokedAt
CreatedAt
```

**Role**
```
Id, Name (enum-backed)
Scope : Platform | Tenant
```

**UserRole (join)**
```
UserId, RoleId
```

**SubscriptionPlan**
```
Id, Name (FreeTrial | Monthly | Yearly)
Price (decimal), DurationDays
TrialDays (configurable by Super Admin — FreeTrial only)
ProductLimit, UserLimit, AIUsageLimit (int)
StorageLimitMB (long)
IsActive
CreatedAt, UpdatedAt
```

**TenantSubscription**
```
Id, TenantId, PlanId
StartDate, EndDate
Status : Active | Expired | Suspended | Cancelled
CreatedAt, UpdatedAt
```

**SubscriptionPayment**
```
Id, TenantId, SubscriptionId
Amount (decimal), DueDate, PaidAt (nullable)
Status : Pending | Confirmed | Overdue
ConfirmedByAdminId (nullable)
CreatedAt, UpdatedAt
```

**BankAccount (TenantOwnedEntity)**
```
Id, TenantId (nullable — null = platform account owned by Super Admin)
AccountTitle      : string (AES-256-GCM encrypted at rest)
AccountNumber     : string (AES-256-GCM encrypted at rest)
BankName          : string (AES-256-GCM encrypted at rest)
BranchCode        : string (AES-256-GCM encrypted at rest)
IBAN              : string (AES-256-GCM encrypted at rest)
IsActive
CreatedAt, UpdatedAt

API response masking rule:
  AccountNumber shown as ****{last4} in all responses
  Full AccountNumber returned ONLY on explicit single-record fetch by AdminOwner or SuperAdmin
```

**AuditLog (BaseEntity — never deleted, append-only)**
```
Id, UserId, TenantId (nullable)
Action            : string  (e.g. "TenantCreated", "PaymentConfirmed", "BankAccountUpdated")
EntityName        : string  (e.g. "Tenant", "SubscriptionPayment")
EntityId          : Guid
OldValues         : string (JSON — sensitive fields pre-masked)
NewValues         : string (JSON — sensitive fields pre-masked)
IpAddress         : string
UserAgent         : string
CreatedAt         : DateTime
```

**UserLoginAttempt (BaseEntity)**
```
Id, Email, IpAddress, UserAgent
IsSuccess         : bool
FailureReason     : string (nullable)
CreatedAt         : DateTime
```

### 4.3 Value Objects

```
Money          → Amount (decimal), Currency (string, default "PKR")
TenantSlug     → validated: lowercase, alphanumeric + hyphens, max 50 chars, unique
```

### 4.4 Roles Enum

```
Platform : SuperAdmin
Tenant   : AdminOwner | StoreManager | InventoryManager | OrderManager | ContentManager
Customer : Customer
```

### 4.5 Domain Events

```
TenantCreatedEvent
TenantSuspendedEvent
SubscriptionAssignedEvent
SubscriptionExpiredEvent
PaymentOverdueEvent
PaymentReminderEvent
PaymentConfirmedEvent
UserCreatedEvent
PasswordResetRequestedEvent
SuperAdminLoginFromNewIpEvent      # triggers security alert email to Super Admin
BankAccountChangedEvent            # triggers audit log + confirmation email to AdminOwner
```

---

## 5. Multi-Tenancy Design

- **Strategy:** Single database, `TenantId` column on every `TenantOwnedEntity`
- **Routing:** Path-based — `platform.com/store/{slug}/...`
- **Tenant resolution:** `TenantResolutionMiddleware` extracts `{slug}` from route, looks up tenant, populates `ICurrentTenantService` (Scoped lifetime)
- **EF Core global filter:** Applied in `OnModelCreating` for all `TenantOwnedEntity` subclasses — queries auto-filtered by `TenantId`
- **Super Admin bypass:** Uses `.IgnoreQueryFilters()` in repository methods that require cross-tenant access
- **Inactive tenant:** If `Tenant.IsActive = false`, middleware returns 403 before reaching controller

---

## 6. Architecture & Request Flow

```
Controller  →  Service  →  Repository  →  DbContext
(receive/      (business    (all DB        (EF Core +
 respond        logic)       queries)       global filter)
 only)
```

**Controller rules:**
- Single responsibility: receive request, call service, return `StatusCode(response.StatusCode, response)`
- Every action decorated with `[HttpVerb(ApiUrl.X)]` and `[ProducesResponseType]` for 200, 400, 500
- No business logic, no conditional branching, no direct repository access

**Service rules:**
- All business logic lives here
- Inherits `GenericService<T>` for CRUD; overrides methods to add domain-specific logic
- Calls only repositories — never writes raw queries
- Returns `ResponseData<T>` on all operations

**Repository rules:**
- All DB queries written here — nowhere else
- Each entity has its own repository inheriting `IGenericRepository<T>`
- Entity-specific queries added as named methods (e.g., `GetBySlugAsync`)

**MediatR usage:**
- Used exclusively for domain events published after write operations complete
- Does NOT sit between controller and service

### 6.1 Generic Repository Interface

```csharp
IGenericRepository<T>
  GetByIdAsync(Guid id)
  GetAllAsync()
  FindAsync(ISpecification<T> spec)
  AddAsync(T entity)
  UpdateAsync(T entity)
  DeleteAsync(T entity)
  CountAsync(ISpecification<T> spec)
```

Entity repositories: `ITenantRepository`, `IUserRepository`,  
`ISubscriptionRepository`, `IPaymentRepository`, `ISubscriptionPlanRepository`

### 6.2 Generic Service Base

```csharp
GenericService<T>
  CreateAsync(T entity)
  UpdateAsync(T entity)
  DeleteAsync(Guid id)
  GetByIdAsync(Guid id)
  GetAllAsync()
```

Entity services: `TenantService`, `UserService`, `SubscriptionService`,  
`PaymentService`, `SubscriptionPlanService`

### 6.3 ResponseData<T>

```csharp
ResponseData<T>
  IsSuccess   : bool
  StatusCode  : int
  Message     : string
  Data        : T?
  Errors      : IEnumerable<string>?

  static Success(T data, string message, int statusCode = 200)
  static Failure(string message, int statusCode = 400, errors?)
```

---

## 7. Security

### 7.1 JWT Tokens

```
Access Token
  Claims   : sub (UserId), email, role, tenant_id, tenant_slug, jti, mfa_verified
  Expiry   : 15 minutes (SuperAdmin: 10 minutes)
  Signing  : HS256 with secret from environment variable / Azure Key Vault

Refresh Token
  Storage  : BCrypt hash in DB (raw token sent to client once only)
  Transport: HttpOnly Secure SameSite=Strict cookie (never in response body)
  Expiry   : 7 days (SuperAdmin: 24 hours)
  Policy   : one active token per user; rotated on every use
  Revoked  : on logout, password change, MFA change, suspicious login detection
```

### 7.2 Password Security

```
Hashing          : BCrypt work factor 12
Complexity rules : minimum 8 characters, at least 1 uppercase, 1 lowercase,
                   1 digit, 1 special character (!@#$%^&*)
History          : last 5 passwords stored as hashes — cannot reuse
Reset token      : cryptographically random 64-byte token, SHA-256 hash stored in DB,
                   expires in 1 hour, single-use, invalidated on use
On password change: ALL active refresh tokens for that user are immediately revoked
```

### 7.3 Multi-Factor Authentication (MFA)

```
Super Admin — MANDATORY
  Method     : TOTP (RFC 6238) — Google Authenticator / Authy compatible
  Setup      : on first login, Super Admin must enrol MFA before accessing any endpoint
  Recovery   : 8 single-use backup codes generated at enrolment (BCrypt hashed in DB)
  Login flow : password → TOTP code → JWT issued with claim mfa_verified=true
  Enforcement: any Super Admin endpoint without mfa_verified=true → 403

Tenant Users — Optional (Phase 1 infrastructure ready, enforcement in Phase 3)
```

### 7.4 Account Lockout

```
Policy   : 5 consecutive failed login attempts → account locked for 15 minutes
           10 consecutive failures → locked until Super Admin unlocks manually
Tracking : UserLoginAttempt table records every attempt (success + failure) with IP + UserAgent
Reset    : successful login resets the failure counter
Alert    : on lockout, email sent to user and (if tenant user) to AdminOwner
```

### 7.5 RBAC — Three Layers

```
Layer 1 — Role (JWT claim)       : [Authorize(Roles = "...")]
Layer 2 — Tenant isolation       : TenantAuthorizationMiddleware
                                   user.TenantId must match resource.TenantId
                                   SuperAdmin bypasses
Layer 3 — Ownership (service)    : resource.OwnerId == currentUser.Id
                                   for self-service operations
```

**Role Permission Matrix:**

| Action | SuperAdmin | AdminOwner | StoreManager | InventoryMgr | OrderMgr | ContentMgr |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| Create / suspend tenants | ✓ | | | | | |
| Manage subscriptions | ✓ | | | | | |
| Manage platform users | ✓ | | | | | |
| View / manage platform bank account | ✓ | | | | | |
| Manage store users | | ✓ | | | | |
| View / manage own bank account | | ✓ | | | | |
| Manage products | | ✓ | ✓ | | | ✓ |
| Manage inventory | | ✓ | | ✓ | | |
| Manage orders | | ✓ | | | ✓ | |
| View store analytics | | ✓ | ✓ | | | |

### 7.6 Banking Information Security

```
Storage
  All sensitive fields (AccountTitle, AccountNumber, IBAN, BankName, BranchCode)
  encrypted with AES-256-GCM before writing to DB
  Encryption key stored in environment variable / Azure Key Vault — never in DB or code
  Each field encrypted independently with a unique nonce (IV)

API response masking
  AccountNumber always returned as ****{last4} in list and summary responses
  Full AccountNumber visible only on explicit single-fetch by AdminOwner (own account)
  or SuperAdmin — every such fetch is written to AuditLog

Access control
  Platform bank account : SuperAdmin only (read + write)
  Brand bank account    : AdminOwner of that tenant only (read + write)
  No other role may read or write bank account fields — enforced at service layer

Change protection
  Any change to bank account fields:
    → writes before/after (masked) to AuditLog
    → sends confirmation email to AdminOwner
    → requires current password re-entry (additional auth step)
```

### 7.7 Audit Logging

```
Append-only AuditLog table — no UPDATE or DELETE ever issued against it
Captures every state-changing action across:
  - Tenant lifecycle (create, suspend, activate, delete)
  - Subscription changes (assign, confirm payment, change plan)
  - Bank account changes (any field modification)
  - User management (create, role change, deactivate)
  - Super Admin login events (success, failure, new IP)

Sensitive field masking in AuditLog:
  Passwords     → never logged
  Tokens        → never logged
  AccountNumber → last 4 digits only
  IBAN          → last 4 characters only
```

### 7.8 Secrets Management

```
Never stored in appsettings.json or source control:
  JwtSettings:Secret
  SmtpSettings:Password
  ConnectionStrings:DefaultConnection
  EncryptionSettings:BankFieldKey
  MfaSettings:IssuerKey

Phase 1  : environment variables on the host machine
Phase 8  : Azure Key Vault with Managed Identity (no credentials in code)

appsettings.json contains only non-sensitive defaults (SMTP host, JWT issuer, etc.)
```

### 7.9 Transport Security

```
HTTPS enforced   : HTTP requests redirected to HTTPS (UseHttpsRedirection)
HSTS enabled     : Strict-Transport-Security: max-age=31536000; includeSubDomains
CORS policy      : allowed origins restricted to Angular frontend domain
                   configured per environment (dev: localhost:4200, prod: platform domain)
```

### 7.10 Rate Limiting (.NET 10 built-in)

```
Public endpoints (login, forgot-password)
  Fixed window : 10 requests / minute per IP

Authenticated endpoints
  Sliding window : 300 requests / minute per TenantId

Super Admin endpoints
  Token bucket : 60 requests / minute per UserId

On breach: 429 Too Many Requests with Retry-After header
```

### 7.11 Security Headers (global middleware)

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'self'
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

### 7.12 Sensitive Data Logging Policy

```
The following MUST NEVER appear in any log output:
  - Passwords (plain or hashed)
  - JWT tokens (access or refresh)
  - Bank account numbers (full)
  - IBAN (full)
  - SMTP credentials
  - Encryption keys

ILogger enrichment: structured logs include UserId, TenantId, CorrelationId
Serilog destructuring policies configured to mask [Password], [Token], [AccountNumber]
```

### 7.13 Super Admin Anomaly Detection

```
On every Super Admin login:
  Compare request IP against last known IPs (stored in UserLoginAttempt)
  If new IP detected:
    → send security alert email to Super Admin's registered email
    → log SuperAdminLoginFromNewIpEvent to AuditLog
    → require TOTP re-verification even if session exists
```

---

## 8. Subscription System

### 8.1 Plan Defaults (all configurable by Super Admin from CMS)

| Plan | Price | Trial Days | Product Limit | User Limit | AI Requests | Storage |
|---|---|---|---|---|---|---|
| FreeTrial | 0 | 30 | 10 | 3 | 50 | 500 MB |
| Monthly | Admin set | N/A | Admin set | Admin set | Admin set | Admin set |
| Yearly | Admin set | N/A | Admin set | Admin set | Admin set | Admin set |

### 8.2 Subscription Lifecycle

```
SuperAdmin assigns plan → TenantSubscription created (Status = Active)

FreeTrial   → activates immediately, no payment
Monthly/Yearly → SubscriptionPayment created (Status = Pending, DueDate = today + 7 days)
                 Brand admin notified by email with platform bank account details

Brand pays → SuperAdmin confirms in dashboard
           → SubscriptionPayment.Status = Confirmed
           → email confirmation sent to brand admin

7 days before EndDate → next SubscriptionPayment created
                      → reminder email sent

EndDate passed, payment still Pending → Status = Overdue → overdue email sent

EndDate + 3 days, still unpaid → TenantSubscription.Status = Expired
                               → Tenant.IsActive = false (store goes offline)
                               → suspension email sent
```

### 8.3 Payment Status Flow

```
Pending → Confirmed  (SuperAdmin confirms receipt)
Pending → Overdue    (DueDate passed — auto by BackgroundService)
Overdue → Confirmed  (SuperAdmin confirms late payment → tenant reactivated)
```

### 8.4 BackgroundService (runs every 24 hours)

```
Task 1 — Expiry    : subscriptions where EndDate < today AND Active → Expire + suspend tenant
Task 2 — Overdue   : payments where DueDate < today AND Pending → mark Overdue + email
Task 3 — Reminder  : payments where DueDate = today + 7 days AND Pending → reminder email
```

### 8.5 Email Triggers

| Event | Recipient | Purpose |
|---|---|---|
| SubscriptionAssignedEvent | Brand Admin | Subscription activated |
| UserCreatedEvent | New User | Account credentials |
| PaymentReminderEvent | Brand Admin | Payment due in 7 days |
| PaymentOverdueEvent | Brand Admin | Payment overdue |
| PaymentConfirmedEvent | Brand Admin | Payment confirmed + reactivation |
| TenantSuspendedEvent | Brand Admin | Store suspended |
| PasswordResetRequestedEvent | Any User | Password reset link |

---

## 9. API Endpoints

### Auth
```
POST   api/auth/login                        # step 1: password → returns mfa_required if SuperAdmin
POST   api/auth/login/mfa                    # step 2: TOTP code → returns tokens (SuperAdmin only)
POST   api/auth/refresh                      # reads refresh token from HttpOnly cookie
POST   api/auth/logout
POST   api/auth/forgot-password
POST   api/auth/reset-password
PUT    api/auth/change-password

Super Admin — MFA Management
GET    api/admin/mfa/setup                   # returns TOTP QR code + secret
POST   api/admin/mfa/verify-setup            # confirms enrolment with first TOTP code
GET    api/admin/mfa/backup-codes            # returns remaining backup codes count
POST   api/admin/mfa/regenerate-backup-codes # invalidates old codes, issues 8 new ones
```

### Super Admin — Tenants
```
GET    api/admin/tenants
GET    api/admin/tenants/{id}
POST   api/admin/tenants
PUT    api/admin/tenants/{id}
PUT    api/admin/tenants/{id}/suspend
PUT    api/admin/tenants/{id}/activate
DELETE api/admin/tenants/{id}
```

### Super Admin — Subscription Plans (CMS)
```
GET    api/admin/subscription-plans
GET    api/admin/subscription-plans/{id}
POST   api/admin/subscription-plans
PUT    api/admin/subscription-plans/{id}
DELETE api/admin/subscription-plans/{id}
```

### Super Admin — Tenant Subscriptions
```
GET    api/admin/subscriptions
GET    api/admin/subscriptions/{id}
POST   api/admin/subscriptions
PUT    api/admin/subscriptions/{id}/confirm-payment
PUT    api/admin/subscriptions/{id}/change-plan
PUT    api/admin/subscriptions/{id}/suspend
PUT    api/admin/subscriptions/{id}/reactivate
```

### Super Admin — Payments
```
GET    api/admin/payments
GET    api/admin/payments/{id}
PUT    api/admin/payments/{id}/confirm
```

### Super Admin — Platform Users
```
GET    api/admin/users
GET    api/admin/users/{id}
POST   api/admin/users
PUT    api/admin/users/{id}
DELETE api/admin/users/{id}
```

### Tenant — Store Profile
```
GET    api/tenant/profile
PUT    api/tenant/profile
```

### Tenant — User Management
```
GET    api/tenant/users
GET    api/tenant/users/{id}
POST   api/tenant/users
PUT    api/tenant/users/{id}
PUT    api/tenant/users/{id}/assign-role
DELETE api/tenant/users/{id}
```

### Tenant — Subscription (read-only)
```
GET    api/tenant/subscription
GET    api/tenant/subscription/payments
```

### Tenant — Bank Account
```
GET    api/tenant/bank-account               # returns masked account (****1234)
POST   api/tenant/bank-account               # create (AdminOwner only, requires password re-entry)
PUT    api/tenant/bank-account               # update (AdminOwner only, requires password re-entry)
```

### Super Admin — Platform Bank Account
```
GET    api/admin/bank-account                # returns masked account
POST   api/admin/bank-account               # create (requires TOTP re-verification)
PUT    api/admin/bank-account               # update (requires TOTP re-verification)
```

### Super Admin — Audit Logs
```
GET    api/admin/audit-logs                  # paged, filter by action/entity/user/date
GET    api/admin/audit-logs/{id}
```

### Super Admin — Login Attempts
```
GET    api/admin/login-attempts              # filter by email/IP/date
PUT    api/admin/users/{id}/unlock           # manually unlock a locked account
```

---

## 10. Middleware Pipeline Order

```
HttpsRedirectionMiddleware       → redirect HTTP → HTTPS
HstsMiddleware                   → add Strict-Transport-Security header
SecurityHeadersMiddleware        → add X-Content-Type-Options, X-Frame-Options, CSP, etc.
ExceptionHandlingMiddleware      → catch all unhandled exceptions → ResponseData<string>
CorsPolicyMiddleware             → enforce allowed origins
RateLimitingMiddleware           → per-IP and per-tenant rate limits
TenantResolutionMiddleware       → resolve ICurrentTenantService from route slug / JWT
Authentication                   → JWT validation (checks mfa_verified claim for SuperAdmin)
Authorization                    → role + tenant + MFA checks
AuditLoggingMiddleware           → write AuditLog entries for state-changing operations
→ Controllers
```

---

## 11. NuGet Packages

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | EF Core SQL Server provider |
| `Microsoft.EntityFrameworkCore.Tools` | Migrations |
| `MediatR` | Domain event dispatching |
| `FluentValidation.AspNetCore` | Request validation |
| `BCrypt.Net-Next` | Password hashing (work factor 12) |
| `OtpNet` | TOTP generation + verification (Super Admin MFA) |
| `MailKit` | SMTP email |
| `AutoMapper` | DTO mapping |
| `Swashbuckle.AspNetCore` | Swagger / OpenAPI |
| `Serilog.AspNetCore` | Structured logging with field masking |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT middleware |

---

## 12. Out of Scope for Phase 1

The following are explicitly deferred to later phases:

- Products, inventory, categories (Phase 2)
- Customer storefront, cart, checkout (Phase 3)
- Angular 20 frontend (Phase 3)
- Admin analytics dashboard (Phase 4)
- AI virtual try-on, size recommendation, chatbot (Phases 5–6)
- SignalR real-time notifications (Phase 7)
- Cloudinary image storage (Phase 2)
- Docker / Azure deployment (Phase 8)
- Customer registration and authentication
- Payment gateway integration for customer orders

---

## 13. Phase Roadmap

| Phase | Scope |
|---|---|
| **1** | **Core SaaS backend (this document)** |
| 2 | Product / Inventory / Order system |
| 3 | Customer storefront — Angular 20 |
| 4 | Admin analytics dashboard |
| 5 | AI Virtual Try-On microservice |
| 6 | AI Body Measurement + Fashion Chatbot |
| 7 | SignalR real-time + Notifications |
| 8 | Docker + Azure deployment |
