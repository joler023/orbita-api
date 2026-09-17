# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

`Orbita` is the backend API for Órbita: a multi-tenant conversational CRM (WhatsApp + Instagram, TikTok in phase 1) with AI agents, built on ASP.NET Core with a Clean Architecture layering (Domain / Application / Infrastructure / Api). The `Tenant` vertical slice (create + get) is the reference implementation of the full pattern — every new aggregate should follow the same shape across the four projects rather than inventing a new one. The `Identity` slice (`User`, `Membership`, `InvitationToken`, organization registration under `POST /api/organizations`, login/session under `POST /api/auth/*`, team invitations under `POST /api/tenants/{tenantId}/invitations` and `POST /api/invitations/accept`) is the second one, and the one to copy for anything tenant-scoped: it is what actually enforces the isolation rule below (EF query filter + Postgres RLS + tenant-first index), not just declares it.

The authoritative data model lives one level up at `../docs/orbita-schema.dbml` — read it before creating or changing any entity. Do not infer schema from guesswork; the DBML file's inline notes encode real product/business rules (see Architecture below).

## Commands

- Build: `dotnet build Orbita.slnx`
- Run: `dotnet run --project Orbita.Api`
- Run with hot reload: `dotnet watch run --project Orbita.Api`
- Format: `dotnet format Orbita.slnx`
- Test: `dotnet test Orbita.slnx` (unit tests run standalone; integration tests spin up a real Postgres via Testcontainers — Docker must be running)
  - Single test: `dotnet test --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`
- Local database: `docker compose up -d` starts Postgres 17 with pgvector/pg_trgm/citext/pgcrypto pre-installed (`docker-compose.yml`, `scripts/init-extensions.sql`).
- **Two roles, two connection strings.** `appsettings.Development.json`'s `ConnectionStrings:Postgres` is `orbita_app` — a least-privileged, non-superuser role the app runs as at runtime, deliberately *not* the table owner, because PostgreSQL never applies Row Level Security to a superuser or a table's owner (see `ORB-A09` in `Orbita.Infrastructure/Persistence/Migrations/*_AddUsersAndMemberships.cs`). `orbita_app` cannot run DDL, so EF Core migrations always need the admin/owner connection (`orbita`/`orbita` locally) passed explicitly via `--connection`.
- EF Core migrations (tool is pinned via the local manifest — run `dotnet tool restore` once after cloning):
  - Add: `dotnet tool run dotnet-ef migrations add <Name> --project Orbita.Infrastructure --startup-project Orbita.Api --output-dir Persistence/Migrations`
  - Apply: `dotnet tool run dotnet-ef database update --project Orbita.Infrastructure --startup-project Orbita.Api --connection "Host=localhost;Port=5432;Database=orbita_dev;Username=orbita;Password=orbita"`
  - A migration that creates a new tenant-scoped table must enable RLS on it (`ALTER TABLE ... ENABLE ROW LEVEL SECURITY` + a `tenant_isolation` policy using `current_setting('app.tenant_id', true)`, `NULLIF`-guarded against the empty string) — `orbita_app` already has the default-privilege grants to read/write it (see the same migration). A non-tenant-scoped table (like `refresh_tokens`) needs neither RLS nor a query filter; `ALTER DEFAULT PRIVILEGES` still grants `orbita_app` access to it automatically since it's created by `orbita`.
- API docs (Development only): OpenAPI document is mapped via `MapOpenApi()`, browsable through Scalar's UI mapped by `MapScalarApiReference()` (default route `/scalar/v1`).

## Architecture

- Solution format is `.slnx` (`Orbita.slnx`), with five projects: `Orbita.Domain`, `Orbita.Application`, `Orbita.Infrastructure`, `Orbita.Api`, plus `Orbita.UnitTests` and `Orbita.IntegrationTests`.
- Target framework: `net10.0` everywhere. `Nullable` and `ImplicitUsings` are enabled in every csproj — never disable them.
- **Layering and dependency direction** (each layer only depends on the ones to its left):
  - `Orbita.Domain` — entities (e.g. `Tenant`, `User`, `Membership`) and the repository/port *interfaces* they need (e.g. `ITenantRepository`, `ITenantContext`, `IUnitOfWork`). No framework dependencies. Entities are rich: private setters, invariants enforced in a static `Create` factory and behavior methods, never anemic DTOs with public setters.
  - `Orbita.Application` — one service per use case (e.g. `ITenantService`/`TenantService`, `IOrganizationRegistrationService`) orchestrating domain + repository calls, plus its DTOs/requests, ports it needs but that need infrastructure to implement (e.g. `IPasswordHasher`), and its own `AddOrbitaApplication(IServiceCollection)` DI extension. Application-level exceptions (e.g. `TenantSlugAlreadyExistsException`, `EmailAlreadyRegisteredException`) signal business-rule violations to the Api layer.
  - `Orbita.Infrastructure` — EF Core (`OrbitaDbContext`, `IEntityTypeConfiguration<T>` per entity under `Persistence/Configurations`, migrations under `Persistence/Migrations`), repository implementations, `UnitOfWork` (see below), and its own `AddOrbitaInfrastructure(IServiceCollection)` DI extension — it resolves the connection string from `IConfiguration` *lazily*, inside the `AddDbContext` options delegate via DI, not by reading `IConfiguration` eagerly at registration time, because `WebApplicationFactory`-based tests only finish layering their overrides onto configuration once the host is built. Postgres via `Npgsql.EntityFrameworkCore.PostgreSQL`.
  - `Orbita.Api` — controllers only (`AddControllers()` / `MapControllers()`, not Minimal APIs) plus the composition root (`Program.cs`) and cross-cutting concerns like `ErrorHandling/GlobalExceptionHandler` (an `IExceptionHandler` mapping domain/application exceptions to `ProblemDetails` — add new exception mappings there instead of `try/catch` in controllers).
- EF Core, Npgsql, and `dotnet-ef` package versions are pinned together (currently `10.0.4`, one point release behind the very latest `Microsoft.EntityFrameworkCore.Design`) because `Npgsql.EntityFrameworkCore.PostgreSQL` lags the core EF Core release train — if you bump one, check `dotnet list package --include-transitive` for version-conflict (MSB3277) warnings and re-pin all EFCore-family packages to match.
- `Program.cs` ends with `public partial class Program;` so `Orbita.IntegrationTests` can boot the real app via `WebApplicationFactory<Program>` — keep that declaration when editing `Program.cs`.

### Domain rules from `orbita-schema.dbml` (binding on all backend code, not just DB migrations)

1. **Tenant isolation.** Every business entity carries `tenant_id`, and it must be enforced at three layers: EF Core global query filter, PostgreSQL Row-Level Security (`current_setting('app.tenant_id')`), and `tenant_id`-first composite indexes. A new entity that skips any of these three is a bug. Concretely, for a new tenant-scoped entity:
   - Add a `HasQueryFilter` for it in `OrbitaDbContext.OnModelCreating`, keyed off the injected `ITenantContext` (a null ambient tenant does not filter — that escape hatch is for use cases that establish the tenant themselves, like registration, not a general-purpose bypass).
   - Enable RLS and add a `tenant_isolation` policy on it in its migration (copy the one on `memberships`), and remember `orbita_app` — the app's *own* runtime role, never the migration/owner role — is what actually gets bound by RLS: Postgres exempts superusers and table owners from it unconditionally.
   - Any write goes through `IUnitOfWork.SaveChangesAsync`, which opens an explicit transaction and sets `app.tenant_id` with `SET LOCAL` semantics inside it before saving — never call `DbContext.SaveChangesAsync` directly for a tenant-scoped write, and never set that session value outside of a transaction that also does the save (a pooled connection does not reliably carry session-level state between separate calls). A tenant-scoped *read* that isn't immediately followed by a save (e.g. checking a caller's role before authorizing something — see `TeamInvitationService`) needs the same treatment via `IUnitOfWork.QueryInTenantScopeAsync`, not a plain repository call.
   - Give it a composite index with `tenant_id` first.
2. **Events over ad-hoc state.** `events` is an append-only log of everything that happens in the product. The test for whether something needs its own table: *can it be reconstructed from the event log?* If yes, it's derived (a `*_metrics`/`*_daily` table at most, regenerable by delete + recompute). If no (e.g. `ai_feedback`, `message_annotations` — a human judgment that only exists once), it gets a first-class table. Default new features to emitting events rather than inventing new tables.
3. **Transactional Outbox.** `outbox_events` is written in the *same transaction* as the domain change it describes, then a dispatcher publishes it. This is the integration seam for anything reacting to a domain event (analytics, webhooks, integrations) — don't bolt side effects directly onto command handlers.
4. **Append-only tables** (`events`, `outbox_events`, `audit_log`): no `UPDATE`/`DELETE` ever. A wrong fact is corrected with a compensating row, never mutated in place.
5. **Partitioned-by-design tables.** `messages` and `events` are partitioned by date range from day one (`PARTITION BY RANGE`), and their primary key includes the partition column (`(id, created_at)` / `(id, occurred_at)`). Any EF model for these must match that composite key shape.
6. **Meta/WhatsApp business rules are explicit domain concepts, not infra details**: `conversations.window_expires_at` (24h free-form service window) and `message_templates.status = approved` (required to message outside that window) must be modeled and enforced in application logic, not left implicit.
7. **Secrets and media never live in Postgres.** Channel credentials are referenced by `credentials_ref` (Secrets Manager ARN) with tokens encrypted via KMS; media is referenced by `media_key`/`avatar_key` (R2 object key) and served only via signed URLs.
8. **No PII in `events.properties`.** Message bodies, phone numbers, and emails live in `messages`/`contacts`, which have access and deletion controls; the event log only carries identifiers and measures.

### Authentication (ORB-A06)

- The access token is a short-lived JWT carried in an `httpOnly`/`SameSite=Lax` cookie (`access_token`), not an `Authorization` header — so it also works for the SignalR WebSocket handshake later, which cannot set custom headers. `AddJwtBearer`'s `OnMessageReceived` event reads it from the cookie instead. `Secure` is only forced on outside `IsDevelopment()`, since local dev and the integration test server are plain HTTP.
- The JWT carries only the user's identity (`sub`) — no tenant claim. Resolving which organization a session acts as (setting `ITenantContextSetter` from a claim) is deliberately left to whichever historia builds the first authenticated, tenant-scoped endpoint; don't invent that wiring speculatively.
- Refresh tokens rotate: `AuthenticationService` never reuses a refresh token record, it issues a new one in the same `FamilyId` and marks the old one `RevokedAt`/`ReplacedByTokenId`. Presenting an already-revoked token is treated as theft and revokes the *entire* family (`IRefreshTokenRepository.RevokeFamilyAsync`, a bulk `ExecuteUpdateAsync` that commits immediately — it does not go through `IUnitOfWork`, because `refresh_tokens` isn't tenant-scoped and there's nothing else to save on that path). Only the SHA-256 hash of a refresh token is ever persisted.
- Login lockout (`User.RegisterFailedLogin`/`IsLockedOut`) and `InvalidCredentialsException` are deliberately generic: an unknown email, a wrong password, and a locked-out account all produce the same 401 with the same message, so the API never becomes an oracle for which case it was.
- The same "resolve configuration lazily through DI, not by capturing `builder.Configuration` into a variable before `.Build()`" rule from `AddOrbitaInfrastructure` applies to anything else configured in `Program.cs` from config (see `JwtBearerOptions`/`CorsOptions` there, both wired via `services.AddOptions<T>().Configure<IConfiguration>((options, configuration) => ...)` instead of a plain lambda that closes over a config value read too early).

### Team invitations (ORB-A07)

- `POST /api/tenants/{tenantId}/invitations`, its `resend`/`DELETE` (revoke) counterparts, and `POST /api/invitations/accept` are the second tenant-scoped, *authenticated* endpoints (after registration, which is pre-auth). The tenant is taken explicitly from the route, not from the JWT — `TeamInvitationService` sets `ITenantContextSetter` from the `tenantId` route parameter and checks the caller's own membership *within that tenant* before doing anything else. This is deliberate: it sidesteps ever needing a cross-tenant "list all of this user's memberships" read, which would otherwise fight Row Level Security the same way the login-time "which tenant is this JWT for" idea would have — see the JWT's missing tenant claim above.
- "Owner or Admin of the tenant" is checked inline in `TeamInvitationService`, not through a general authorization policy — ORB-A08 (roles and permissions) is what generalizes this across the API. Don't build that framework early to support this one feature.
- An invited person who doesn't have an account yet gets a real `User` row immediately, with an **unusable placeholder password hash** (`User.CreateInvited`) — never a null/missing password, since `password_hash` is `NOT NULL` in orbita-schema.dbml and login must still work correctly (a placeholder hash simply never verifies). `User.PasswordSetAt` (also not in the DBML, same class of addition as the ORB-A06 lockout columns) is null until they actually choose a password, and is what `POST /api/invitations/accept` uses to decide whether the submitted password *becomes* their password (placeholder case) or must *match* their existing one (already-registered case, since an email link alone must not be enough to act as an arbitrary existing account).
- `InvitationToken` (like `RefreshToken`) is not in the DBML and not tenant-scoped/RLS'd — it denormalizes `TenantId` from the `Membership` it belongs to for exactly one reason: redeeming it starts with nothing but a raw token, so learning the tenant from the token itself is what lets the rest of `AcceptAsync` establish tenant scope before touching `Memberships`.

### Roles and permissions (ORB-A08)

- `RolePermissions` (`Orbita.Domain/Identity`) is the single source of truth for "can this role do X": a `FrozenDictionary<MemberRole, FrozenSet<Permission>>` that any application service checks through `ITenantAuthorizationService.EnsurePermissionAsync(tenantId, callerUserId, permission, ct)` instead of comparing `MemberRole` values inline. This is the generalization ORB-A07 deferred: `TeamInvitationService`'s old private `EnsureCallerIsOwnerOrAdminAsync` is gone, replaced by a call into this shared service. Adding a new backend-enforced capability means adding a `Permission` enum member and its entries in `RolePermissions`, not a new bespoke check.
- Fifteen permissions exist so far — `ViewTeam`/`ManageTeam` (ORB-A08), `ManageSettings` (ORB-A11, Owner/Admin), `ManageBilling` (ORB-A12, **Owner-only**), `ViewAuditLog` (ORB-A15, Owner/Admin), `ManageAiAgents` (ORB-C02/C10, Owner/Admin), `ViewChannels` (every role) / `ManageChannels` (ORB-B01, Owner/Admin), `ViewPipeline` (every role) / `ManagePipeline` (ORB-D04, Owner/Admin), `ManageOpportunities` (ORB-D05, everyone but Viewer), `ViewContacts` (every role) / `ManageContacts` (ORB-D02, everyone but Viewer), `SendMessages` (ORB-B05, everyone but Viewer), and `ManageTemplates` (ORB-B07, Owner/Admin) — because those are the only areas with role-varying actions so far. Don't pre-create permissions for features that don't exist yet.
- `TeamMembersService` (list/change-role/remove) is the ORB-A08 counterpart to ORB-A07's `TeamInvitationService`: the latter only ever creates pending memberships and accepts them, this administers memberships that already exist. Both depend on the same `ITenantAuthorizationService`.
- **A tenant always keeps at least one Owner.** `TeamMembersService` counts active Owners (`IMembershipRepository.CountActiveByTenantAndRoleAsync`) before demoting or removing one, and throws `CannotRemoveLastOwnerException` (409) if that would leave zero — checked only when the *target* of the change is currently an Owner, not on every call.
- Enforcement is entirely backend-side, per the historia's explicit acceptance criterion ("ocultar un botón no es control de acceso") — hiding actions a role can't use in the dashboard is `orbita-front`'s job once it exists, not a substitute for the 403s here.

### Password reset (ORB-A10)

- `POST /api/auth/forgot-password` always responds `202 Accepted`, whether or not the email belongs to an account — `PasswordResetService.RequestAsync` is a silent no-op for an unknown email instead of throwing, so the endpoint can never be used to enumerate registered accounts. Don't add a different status code or error body for the "email not found" case.
- `PasswordResetToken` mirrors `InvitationToken`'s shape (global, not tenant-scoped, only a SHA-256 hash persisted) but with a much shorter lifetime (`PasswordResetService.ResetLifetime`, one hour vs. seven days) — it is a "prove you control this inbox right now" credential, not an onboarding link.
- Requesting a new reset link invalidates any previous one for that user (`IPasswordResetTokenRepository.InvalidateForUserAsync`), same pattern as `TeamInvitationService`'s resend.
- `POST /api/auth/reset-password` revokes every active refresh token for the user across every family (`IRefreshTokenRepository.RevokeAllForUserAsync`), not just the family behind the session that requested the reset — "se invalidan todas las sesiones activas" means all of them. This is a separate bulk update from `RevokeFamilyAsync` (ORB-A06's reuse-detection response), which only revokes one family.

### Audit log (ORB-A15)

- `AuditLogEntry` is the **first entity in this codebase with a non-`Guid` id** — orbita-schema.dbml specifies `id bigint [pk, increment]` for `audit_log` specifically (every other table uses `uuid`), so it deliberately does not inherit `Orbita.Domain.Common.Entity` (which hardcodes `Guid Id`). If a future table also needs a DB-assigned identity column, follow this one's shape, not `Entity`'s.
- It has **no mutator methods at all** — "sin UPDATE ni DELETE" means genuinely immutable after construction, not just unused setters. `orbita_app`'s `UPDATE`/`DELETE` privileges are explicitly revoked on this one table in its migration (`REVOKE UPDATE, DELETE ON audit_log FROM orbita_app;`), on top of the blanket grant every other table gets — the append-only invariant is enforced at the database level, not only by convention in application code.
- Unlike `Subscription`/`InvitationToken`/`PasswordResetToken`, `audit_log` **is** RLS'd/query-filtered the normal way (domain rule 1) — it's always read with a known tenant (an Owner/Admin viewing their own trail), never looked up by an external opaque id before a tenant is known.
- `IAuditLogger.RecordAsync`/`RecordSystemActionAsync` only stage the entry via the repository — they never call `IUnitOfWork.SaveChangesAsync` themselves. Call them from inside the same application-service method whose own `SaveChangesAsync` call persists the change being audited, so the audit entry and the change it describes land in the same transaction (and the same RLS-scoped `app.tenant_id`). `AuditLogQueryService`, which only reads, needs its own `IUnitOfWork.QueryInTenantScopeAsync` wrapper instead — the tenant-scoped transaction `EnsurePermissionAsync` opens for its own permission check has already committed and closed by the time you'd otherwise query, and `SET LOCAL app.tenant_id` doesn't outlive it.
- `IRequestContext` (Application) / `HttpRequestContext` (Infrastructure) is a new small ambient-context port that reads IP/user-agent off the current `HttpContext` via `IHttpContextAccessor`, when there is one — both are null outside a real request (a background job, most unit tests), which the nullable `ip`/`user_agent` columns already allow for.
- This historia wires the pattern into `TeamMembersService.ChangeRoleAsync`/`RemoveAsync` as its reference implementation (a "membership.role_changed"/"membership.removed" entry per call) — it does **not** retrofit every existing mutation across the app. Wiring `IAuditLogger.RecordAsync` into a new mutation worth auditing is a one-line addition once permission/tenant scope is already established in that method; do that as each area actually needs it, per the DBML's own "Base para SOC 2 en la etapa 3" framing, rather than as a separate sweep.
- Query filters accepted so far (`AuditLogQuery`): `entityType`, `entityId`, `actorId`, `from`/`to`, capped at a `limit` (default 50). No cursor-based pagination yet — add it if a real admin panel needs to page past the first batch, don't build it speculatively.

### Billing: plans and subscriptions (ORB-A12)

Both payment integrations are built against the real Stripe and Wompi APIs and are structurally complete, but **neither has real credentials connected** — this section is what's needed to actually turn one on, plus the design decisions behind the code.

- `Orbita.Api/appsettings*.json` has empty `Billing:Stripe:{SecretKey,WebhookSecret}` and `Billing:Wompi:{PublicKey,PrivateKey,EventsSecret}` sections. Filling these in (with test-mode/sandbox values first) is the actual "connect the account" step — nothing else in code needs to change to start hitting a real sandbox.
- **Stripe, to go live:**
  1. Create a Product + a recurring Price in the Stripe dashboard/API for each row in the `plans` table, then call `Plan.SetStripePriceId` with the resulting `price_...` id (there's no admin endpoint for this yet — do it via a one-off script or direct SQL `UPDATE plans SET stripe_price_id = ...`). `StripePaymentProvider.CreateSubscriptionAsync`/`ChangeSubscriptionAsync` throw `InvalidOperationException` for any plan missing one.
  2. Register `https://<your-domain>/api/billing/webhooks/stripe` as a webhook endpoint in the Stripe dashboard, subscribed at least to `customer.subscription.*` events, and copy its signing secret into `Billing:Stripe:WebhookSecret`.
  3. The frontend needs Stripe.js/Elements to collect card details and produce a `PaymentMethod` id client-side — that id is `SubscribeRequest.PaymentMethodToken`. Raw card data must never reach this API; `StripePaymentProvider` only ever receives that opaque token.
- **Wompi, to go live — read this before enabling it, its capabilities are narrower than Stripe's:** Wompi has no subscription/price object at all, only one-off transactions against a reusable tokenized `payment_source`. `WompiPaymentProvider`'s class-level doc comment explains exactly how each `IPaymentProvider` method maps onto that reality; the short version:
  - `ChangeSubscriptionAsync` charges the new plan's full price immediately — there is no proration, because there is no billing cycle on Wompi's side to prorate against.
  - `CancelSubscriptionAsync` is a no-op. Cancellation only means something once a recurring-billing scheduler exists (see next point).
  - **A recurring-billing scheduler does not exist yet and is required before Wompi billing can go live** — something has to call `WompiPaymentProvider`'s transaction-creation path once per tenant per billing period; nothing does that automatically the way Stripe's own billing engine does. Build that (a scheduled job iterating active Wompi `Subscription` rows) before connecting a real Wompi account, not as an afterthought.
  - `ListInvoicesAsync` returns an empty list — Wompi's API has no "list transactions for a customer" endpoint, only `GET /transactions/{id}` for an already-known id. A real implementation needs to persist each transaction id locally as it's created (there is no such table yet) and look them up individually.
  - The frontend needs Wompi's own widget to tokenize a card into a `payment_source_id` — same "raw card data never reaches this API" rule as Stripe, via `SubscribeRequest.PaymentMethodToken`.
- **Provider selection is automatic, not user-facing**: `SubscriptionService` picks Wompi for `Tenant.CountryCode == "CO"` and Stripe for everything else. There is no endpoint to choose a provider explicitly — don't add one without a product reason, since "pick your own payment rail" isn't a requirement anywhere in the backlog.
- `Subscription` is deliberately **not tenant query-filtered/RLS'd**, unlike almost every other tenant-scoped entity — see its class-level doc comment. Short version: a webhook arrives with only a `ProviderSubscriptionId`, no ambient tenant, and Postgres RLS returns zero rows for every query when no tenant is set (same root cause as the JWT's missing tenant claim). `SubscriptionRepository` filters by `TenantId` explicitly instead, and `SubscriptionService` re-checks `subscription.TenantId == tenantId` as defense in depth, the same pattern already used for `InvitationToken` lookups.
- `ManageBilling` is **Owner-only**, unlike `ManageTeam`/`ManageSettings` (Owner+Admin) — money matters don't extend to Admins by default. Don't widen this without an explicit product decision.
- Webhook signature verification is real and tested (`StripePaymentProviderTests`, `WompiPaymentProviderTests` in `Orbita.IntegrationTests` — they live there, not `Orbita.UnitTests`, because only that project references `Orbita.Infrastructure`) — it needs a configured secret, not a live account, since it's just keyed hashing against a payload. `TenantsApiFixture` replaces both `IPaymentProvider` registrations with `FakePaymentProvider` for every other integration test, so the subscribe/change-plan/cancel/list-invoices HTTP flows are covered without ever calling a real payment API.

### Two-factor authentication (ORB-A11)

- TOTP (RFC 6238) via `Otp.NET`, kept behind `ITotpProvider` the same way password hashing sits behind `IPasswordHasher` — Application code never imports `OtpNet` directly. `User.TwoFactorSecretCiphertext` is set as soon as setup begins (`BeginSetupAsync`), before `TwoFactorEnabledAt` exists — starting setup twice before confirming just overwrites the pending secret, there is no separate "pending secret" table.
- The QR code itself is never generated server-side: `TwoFactorSetupResult.ProvisioningUri` is a plain `otpauth://` URI, and rendering it as a scannable QR code is `orbita-front`'s job (every authenticator app reads the same URI format regardless of what drew the QR). The raw base32 secret is also returned, for the "can't scan a code" manual-entry path every authenticator app supports.
- **The TOTP secret is encrypted at rest, never stored raw** — `IUserSecretProtector`, implemented in Infrastructure via ASP.NET Core's Data Protection API (`DataProtectionUserSecretProtector`). This is a stand-in for real KMS-backed encryption, the same "swap the Infrastructure implementation later" pattern as `LoggingInvitationEmailSender`/`LoggingPasswordResetEmailSender` — Data Protection's default local key ring is fine for one dev instance, not for a multi-instance production deployment.
- Backup codes (`TwoFactorBackupCode`, 10 issued per batch) follow the exact same shape as every other token in Identity: only a SHA-256 hash persisted, single-use, invalidated in bulk on disable or regeneration.
- **Login is a single endpoint, not two.** `LoginRequest.TwoFactorCode` is optional; `AuthenticationService.LoginAsync` checks the password first, and only *then* — if `User.HasTwoFactorEnabled` — requires a code. No code submitted throws `TwoFactorRequiredException` (401, distinguishable from a wrong password on purpose: the caller already proved they know the password, so "we need a second factor" isn't a fact worth hiding the way account existence is). A *wrong* code, though, folds back into the generic `InvalidCredentialsException` and the same failed-login lockout counter as a wrong password — brute-forcing the second factor must cost exactly as much as brute-forcing the first. `ITwoFactorService.VerifyLoginCodeAsync` tries the TOTP code first, then falls back to a backup code.
- **Org-wide MFA enforcement is not implemented, on purpose.** `Tenant.RequireMfaForMembers` (settable via `PATCH /api/tenants/{tenantId}/settings/mfa-policy`, gated by the new `ManageSettings` permission) records the Owner's policy choice, but nothing blocks login or access when it's set. Enforcing it correctly needs to know, *before* tokens are issued, which tenant(s) the logging-in user belongs to and whether any of them require MFA — and every read across tenants is deliberately blocked today: the RLS policy on `memberships` (`tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`) returns zero rows whenever no tenant is set in the session, so a "does this user belong to any tenant that requires MFA" query would silently return nothing against the real database even though EF's own query filter would let it through. This is the same category of deferred decision as the missing JWT tenant claim (see Authentication above) — solve that first, then wire enforcement here; don't bypass RLS to hack around it.

### Channels: connecting WhatsApp (ORB-B01)

First slice of Track B (Canales y Bandeja). The `Channels` folder in each layer (`Orbita.Domain/Channels`, `Orbita.Application/Channels`, `Orbita.Infrastructure/Channels`) is where every channel-related type goes; the same folder gets Instagram (ORB-B09) later, behind the same ports.

- **The Meta app does not exist yet.** `Channels:Meta:{AppId,AppSecret,VerifyToken}` and `Channels:Webhooks:PublicBaseUrl` are empty placeholders in `appsettings*.json`, exactly like `Billing:Stripe`. Filling them in is the "connect a real app" step; nothing in code changes. To go live: create a Meta app with the WhatsApp product, configure its app-level webhook callback as `https://<PublicBaseUrl>/api/webhooks/whatsapp` with `Channels:Meta:VerifyToken` as the verify token, subscribe it to the `messages` field, and copy the app id/secret into config. The dashboard side runs Meta's Embedded Signup (Facebook JS SDK) and posts `{code, wabaId, phoneNumberId}` to `POST /api/tenants/{tenantId}/channels/whatsapp` — raw tokens never come from the frontend.
- **Two webhook routes, one service.** Meta requires an app-level callback before it lets you save the WhatsApp product configuration (`GET /api/webhooks/whatsapp`, verified against the global token). On top of that, connecting an account registers a **per-account** callback through `POST /{waba_id}/subscribed_apps` with `override_callback_uri` (`GET /api/webhooks/whatsapp/{channelAccountId}`), verified against `ChannelAccount.WebhookSecret` — the DBML's `webhook_secret` column is that per-account `hub.verify_token`, *not* the HMAC key. Payload signatures (`X-Hub-Signature-256`, ORB-B02) are always computed by Meta with the app secret, which is platform config. Both GETs go through `IChannelWebhookVerificationService`; the per-account one flips the account from `PendingVerification` to `Connected`.
- **Order of operations in `WhatsAppChannelService.ConnectAsync` is load-bearing**: the account row is committed *before* `SubscribeWebhookAsync` is called, because Meta performs the verification GET synchronously inside that call and our handler looks the account up by id. A failed subscription leaves the account pending (retry via `POST .../channels/{id}/verify`), it does not fail the connect. Without a `PublicBaseUrl` (local dev) no override is registered and only the app-level route exists.
- **`channel_accounts` is deliberately not RLS'd/query-filtered** — same exception as `Subscription`: an inbound webhook has only `(kind, external_id)` (the DBML's `ux_channel_external` index exists for exactly this lookup) and no tenant in session, and RLS would return zero rows. `IChannelAccountRepository` filters by tenant explicitly and `WhatsAppChannelService.RequireAccountAsync` re-checks `TenantId`. `waba_id` is an addition to the DBML (webhook subscription and template sync are WABA-level calls).
- **The access token never lives in `channel_accounts` or in logs.** `IChannelCredentialStore` is the port (Secrets Manager + KMS in production, `credentials_ref` = ARN); `DataProtectionChannelCredentialStore` is the local stand-in, keeping a Data Protection-encrypted ciphertext in the infra-only `channel_credentials` table under an opaque `local://{guid}` reference — same "swap the Infrastructure implementation later" pattern as `IUserSecretProtector`. It commits through raw SQL so it behaves like the external store it replaces and never flushes the caller's pending change-tracker state. The two Graph API `HttpClient`s are registered with `RemoveAllLoggers()` because HttpClientFactory's default logging writes the request URI at Information level and Meta's OAuth endpoints carry the app secret and tokens in the query string.
- **Background workers** start here: `Orbita.Infrastructure/Workers/PollingWorker` is the base every "every N, do one unit of work" service extends (a tick's failure is logged, never fatal). `ChannelTokenExpiryWorker` sweeps `Connected` accounts past `TokenExpiresAt` into `TokenExpired` (cadence `Channels:Workers:TokenExpiryIntervalMinutes`); the "expires soon" warning is computed per read (`ChannelAccountDto.ExpiresSoon`, 7 days). Workers run inside `WebApplicationFactory` too — lower their intervals in the fixture when a test needs to observe one.
- `ITenantAuthorizationService` is what establishes the ambient tenant for the connect/list/disconnect requests; the anonymous verification GET sets it explicitly from the account (`ITenantContextSetter.SetTenant(account.TenantId)`) so the RLS'd `audit_log` insert (`channel.webhook_verified`, a system action) lands.
- Integration tests replace `IMetaAuthClient`/`IWhatsAppCloudApiClient` with `FakeMetaAuthClient`/`FakeWhatsAppCloudApiClient` (`TenantsApiFixture`). The WhatsApp fake records every subscription it is asked for — that is how a test learns the per-account verify token and plays Meta's side of the handshake.

### Ingestión de webhooks (ORB-B02)

The no-loss boundary every inbound WhatsApp/Instagram POST crosses before anything interprets it: verify signature → resolve `ChannelAccount` → de-dup → durably enqueue. `IWebhookIngestionService` is deliberately free of EF/heavy DI — only ports, `TimeProvider` and `ILogger` — so it can be hosted in a Lambda later without change; `WhatsAppPayloadInspector`/`InstagramPayloadInspector` pull just the routing id out of the raw payload with a forward-only `Utf8JsonReader` scan instead of a full parse, since the payload isn't trusted yet at that point.

- **`inbound_webhook_events` is the idempotency boundary, not the in-memory dedup.** `PostgresInboundWebhookQueue.EnqueueAsync` is a raw `INSERT ... ON CONFLICT (payload_hash) DO NOTHING` — a duplicate payload is a silent no-op, never a unique-constraint exception, because Meta's own retries are expected traffic. `MemoryCacheWebhookDeduplicator` is only a single-instance fast path in front of that (10-minute window); it is deliberately racy under concurrency, and that's fine because the unique index is what's actually correct across instances/after expiry. `payload_hash` is the lowercase hex SHA-256 of the raw body (`InboundWebhookEvent.Hash`).
- **Account resolution differs by route.** The per-account route (`.../whatsapp/{channelAccountId}`) still cross-checks the payload's own `phone_number_id` against `ChannelAccount.ExternalId` before trusting the route id — otherwise a stale or forged id would attribute someone else's messages to the wrong tenant. The app-level route resolves the account from the payload alone: WhatsApp's `entry[].changes[].value.metadata.phone_number_id`, Instagram's `entry[].id` (the IG business account id, not a username — Instagram has no per-account route yet, that arrives with the adapter in ORB-B09). An unresolvable account is `Unroutable`, logged as a warning, and still answered with **200** — Meta retries forever on anything else, and redelivering the same unroutable payload won't make an account exist. A `Disconnected` account's webhook is still enqueued; ORB-B03 decides what an inbound message means for a stopped channel, ingestion doesn't gatekeep on status.
- **`inbound_webhook_events` has no RLS/query filter**, same exception as `channel_accounts`: the row is what establishes which tenant a webhook belongs to, so there is no ambient tenant to filter by at insert time. It's mapped by `InboundWebhookEventConfiguration` for migrations/reads only — nothing ever calls `DbContext.Add` on it, all writes go through the raw-SQL queue above.
- **`InvalidWebhookSignatureException` moved from 400 to 401** in `GlobalExceptionHandler` (it's the same type Stripe/Wompi webhook verification already threw — one concept, one exception, one status): a signature mismatch is "prove you're Meta", the same category as any other failed authentication, not a malformed request.
- **The Instagram GET verify callback reuses `IChannelWebhookVerificationService.VerifyWhatsAppAsync(null, …)`** rather than a new method — with `channelAccountId` null that call was already exactly "check against the platform-wide `Channels:Meta:VerifyToken`", true for either product since one Meta app covers both. Don't rename it without checking both controllers.
- **Known gap:** the load/throughput test described in the original story plan (many concurrent distinct + duplicate payloads asserting exactly-once persistence) was not written — the ON CONFLICT DO NOTHING behavior is covered functionally (`WhatsAppWebhooksControllerTests.Post_SamePayloadTwice_PersistsOneRow`) but not under concurrency. Add it before relying on this under real Meta traffic volume.

### Bandeja: mensajes entrantes (ORB-B03)

`InboundMessageWorker` drains `inbound_webhook_events` (`IInboundWebhookQueue.DequeueBatchAsync`, `SKIP LOCKED`) and hands each row to `IInboundMessageProcessor`, which turns a raw payload into a `Contact`/`Conversation`/`Message` via `IChannelAdapter.ParseInbound` (`WhatsAppChannelAdapter` + `WhatsAppPayloadParser` for now — Instagram's adapter arrives with ORB-B09).

- **`messages` is partitioned by `created_at` from day one, empty.** The migration replaces EF's normal `CreateTable` with hand-written SQL (`PARTITION BY RANGE`, monthly partitions from 2026-09 through 2028-12 via a `DO $$` loop) — converting an already-large table to partitioned later is one of the most expensive reprocesses in Postgres; doing it now costs a few lines. `orbita_app` has no DDL privileges, so extending the partition range past 2028-12 is a future migration, not something runtime code can do. RLS is enabled once on the parent and inherited by every partition automatically when queried through it.
- **`Message` does not inherit `Entity`** — a third exception (after `AuditLogEntry` and `InboundWebhookEvent`), and for yet another reason: `Id` is still a client-generated `Guid`, but Postgres requires a partitioned table's primary key to include the partition column, so the real key is the composite `(Id, CreatedAt)`, which `Entity`'s single-Id equality doesn't model. See `MessageConfiguration`'s remarks.
- **Idempotency is layered, not one mechanism.** A `UNIQUE` index on a partitioned table must include the partition key, so `ux_messages_external (external_id, created_at)` can't by itself guarantee one row per `external_id` globally. What actually guarantees it: `IMessageRepository.FindByExternalIdAsync` is checked before every insert, and only one worker instance ever processes a given tenant's events at a time in practice (single consumer per channel account). The partial unique index is the cheap backstop, not the source of truth.
- **`Contact.NormalizePhone` now strips the leading `+`** (digits only) to match Meta's `wa_id`, which never has one — keeping it would have silently duplicated every WhatsApp contact on their first inbound message. This is a real behavior change to Track D's `Contact` (touches `ContactServiceTests`/`ContactsControllerTests`, and the migration includes a one-time `UPDATE contacts SET phone = ltrim(phone, '+')` data fix for anything created under the old behavior). `Contact.InstagramUserId` (column `ig_user_id`, matching the DBML) is new and distinct from the existing `InstagramUsername` — the former is Instagram's stable business-scoped id, the latter a handle that can change.
- **`Conversation` carries every column from the DBML's `conversations` table now** (`assignee_id`, `ai_agent_id`, `human_agent_expires_at` included), even though assignment (ORB-B15), AI agents, and the human_agent extension (ORB-B11) aren't implemented yet — adding a nullable column to a live table later is cheap, but the story plan already indexes on `assignee_id` from this migration onward, so the column has to exist first. `LastMessagePreview` (140 chars) is the one addition beyond the DBML: it avoids a LATERAL join against the partitioned `messages` table on the hottest inbox query.
- **One contact+account pair keeps a single lifetime `Conversation`.** `IConversationRepository.FindOpenByContactAndAccountAsync` looks up the thread regardless of status; `Conversation.RegisterInbound` is what actually reopens it (Closed/Pending/Snoozed → Open, window refreshed) — a closed conversation is never replaced with a new row.
- **`InboundMessageProcessor` does one `SaveChangesAsync` per webhook event, not per message** — a batch of several messages in one WhatsApp payload lands together or not at all. `InboundStatusUpdate` items are parsed starting now (`WhatsAppPayloadParser` reads `statuses[]`) but silently skipped by the processor until ORB-B08 acts on them.
- **The worker gives every item its own DI scope and sets the ambient tenant explicitly** (`ITenantContextSetter.SetTenant`) before resolving `IInboundMessageProcessor` — `Contact`/`Conversation`/`Message` all carry the normal RLS + EF query-filter pattern, which is per-`DbContext`-instance ambient state, so a shared scope across tenants in one batch would silently query/write the wrong tenant's rows. The batch's own dequeue-and-mark-processed bookkeeping stays on the outer scope's `DbContext`; only the actual domain writes happen per-item.
- **`InboundMessageWorker.StartAsync` requeues anything left `Processing`** before the polling loop starts — protects against a crash mid-batch leaving events permanently stuck (they'd never be picked up again otherwise, since the dequeue query only looks at `Pending`).
- **Known gaps:** `TenantIsolationTests` was not extended to cover conversations/messages (the RLS mechanism itself is already proven by other tables; this would only add coverage of the specific query filters). `InboundMessageFlowTests` polls with a fixed `Eventually` helper rather than a `WithWebHostBuilder`-injected fake adapter for deterministic failure-path testing (e.g. "worker failure three times marks the event Dead") — add that if `InboundMessageWorker`'s retry path needs to be trusted before going live.

### Outbox (ORB-B04)

The transactional-outbox pattern: `IOutboxWriter.StageAsync` only adds an `OutboxEvent` through the repository — it never calls `SaveChangesAsync` itself (same "stage now, the caller's own unit of work persists it" contract as `IAuditLogger`) — so an event and the domain change it describes always land in one transaction, or neither does. `OutboxDispatcherWorker` (500ms tick, matching the latency ORB-B14 will document for realtime updates) then calls `OutboxDispatchService.DispatchOnceAsync`, which claims a batch and fans each one out through `IIntegrationEventPublisher` — `InProcessIntegrationEventPublisher` today (in-process handler fan-out; a real broker later without touching the dispatcher).

- **No PII in the payload, ever.** `InboundMessageProcessor` stages `message.received {messageId, conversationId, contactId, channelAccountId, direction}` for every message and `conversation.opened {conversationId, contactId, channelAccountId}` only the first time a conversation is created — ids and enums only, never the message body, a name, a phone number. `outbox_events` has no RLS (the dispatcher reads across every tenant) and a row can sit unpublished for a while, so this isn't optional hardening, it's the actual privacy boundary. `OutboxTests.InboundMessage_ProducesPublishedOutboxRow` asserts the raw PII values never appear in the persisted payload, not just that the event exists.
- **A publish failure is that event's problem, not the batch's.** `OutboxDispatchService` catches per-event: one `PublishAsync` throwing calls `RegisterFailure()` (bumps `Attempts`, stays unpublished) and moves on to the next event in the batch; `InProcessIntegrationEventPublisher` goes a level further and isolates per-*handler* failures too, so one broken handler never blocks another or fails the publish itself. Backoff before the next retry is computed in SQL with no extra column: `occurred_at + 30s * 2^attempts <= now()`.
- **`outbox_events` has no RLS, same category as `channel_accounts`/`inbound_webhook_events`** — but the privilege story differs: `UPDATE` stays granted (every dispatch needs to set `published_at`/bump `attempts`), only `DELETE` is revoked. There's no retention/cleanup job yet, so published rows just accumulate; add one before this matters at real volume.
- **`DequeuePendingAsync`'s `FOR UPDATE SKIP LOCKED` only protects against this same dispatcher instance racing itself across ticks**, not yet against two horizontally-scaled dispatcher instances claiming the same row at the same instant — that needs the claiming `SELECT` and the later `SaveChangesAsync` to share one explicit transaction, which nothing here sets up. Fine for the single dispatcher instance this runs as today; revisit before running more than one.
- **`OutboxEvent` is the fourth entity that doesn't inherit `Entity`** (after `AuditLogEntry`, `InboundWebhookEvent`, and `Message`) — same reason as the first two: `Id` is a DB-assigned bigint identity.

### Mensajes salientes (ORB-B05)

`POST /api/tenants/{tenantId}/conversations/{conversationId}/messages` is persist-first: `OutboundMessageService.SendTextAsync` creates the `Message` (`Queued`), registers it on the `Conversation`, and enqueues an `OutboundMessageJob` — all in the same `SaveChangesAsync` — before anything has actually tried to talk to Meta. The message exists and is visible even if the process crashes one line later. `OutboundMessageWorker` (1s tick) then claims due jobs and calls `OutboundMessageDispatchService.DispatchAsync`, which is where the real send happens.

- **`OutboundMessageJob` is deliberately a separate table from `messages`, not a status column on it.** The dispatcher needs to poll by `next_attempt_at` without ever touching the partitioned, RLS'd `messages` table with no ambient tenant — same reasoning as `outbox_events` being separate from the aggregates it describes. `MessageCreatedAt` on the job exists only because `Message`'s real key is the composite `(Id, CreatedAt)`; the job needs both halves to join back to it later (ORB-B08's status handling will).
- **`IChannelAdapter` grew `SendTextAsync` only** — media (ORB-B06) and templates (ORB-B07) get their own methods once those stories know the real shape, same "don't guess the interface ahead of the story that needs it" call made when `IChannelAdapter` first landed in ORB-B03. This turned `WhatsAppChannelAdapter` from stateless (`Singleton`) into a consumer of `IChannelCredentialStore`/`IWhatsAppCloudApiClient` (both `Scoped`) — its own DI registration had to move to `Scoped` too, or it would be a captive dependency.
- **Where the "is this worth retrying" decision lives**: `MetaGraphResponseReader` (unchanged since ORB-B01) throws `MetaApiException` with Meta's numeric `error.code`; `WhatsAppChannelAdapter.SendTextAsync` catches that and translates it into `ChannelSendException(errorCode, isTransient, messageEs)` using `MetaErrorCatalog.Describe` — the classification happens once, at the adapter boundary, not scattered through the dispatcher. `130429`/`131056` (rate limit) are the only transient codes; everything else (window closed, invalid recipient, bad param, expired token, unregistered account, media problem) is permanent. `190`/`131001` additionally call `account.MarkTokenExpired()` — same state B01's `ChannelTokenExpiryWorker` sets on a schedule, now also set eagerly on the first send that actually proves the token is dead.
- **Error translation happens at read time, no new column** — same pattern as `MessageTemplateStatus`/other lookups elsewhere in this codebase: `MessageDto.From` calls `MetaErrorCatalog.Describe(message.ErrorCode)` to fill `ErrorMessage` only when mapping to the DTO. The stored `ErrorCode` is Meta's raw code; the Spanish sentence is never persisted.
- **`TokenBucketRateLimiter` (80/s per channel account) lives entirely in memory**, one bucket per account created lazily and kept for the process's lifetime — there's no cross-instance coordination, so this only actually caps throughput for a single running instance. Fine today (one instance); revisit alongside the outbox dispatcher's own single-instance caveat before scaling out.
- **`outbound_message_jobs` has no RLS** — same category as `outbox_events`, the worker scans across every tenant's due jobs with no ambient tenant set. It only ever holds send-attempt bookkeeping (ids, status, timestamps), never message content, so this isn't a PII concern the way `outbox_events`' payload is.
- **Two-tier `SaveChangesAsync`, same shape as ORB-B03's inbound worker**: `OutboundMessageDispatchService` runs in its own per-job, tenant-scoped DI scope and saves its own domain writes (`Message`, `Conversation`'s implicit state via the loaded entities, `ChannelAccount.MarkTokenExpired`); `OutboundMessageWorker`'s outer scope — the one that actually dequeued the job — saves the job's own status/attempts/next-attempt bookkeeping separately, since that entity was never tracked by the inner scope's `DbContext`.

### Multimedia (ORB-B06)

`LocalFileMediaStorage` is the local deviation from orbita-schema.dbml's R2 design: bytes live on disk under `Media:LocalStoragePath` instead of an R2 bucket, addressed by the exact same key shape (`tenants/{tenantId}/conversations/{conversationId}/{mediaId}.{ext}`) a presigned R2 URL would use — swapping in the real store later is an `IMediaStorage` implementation change, nothing else. `MediaUrlSigner` is the equivalent local stand-in for R2's own presigned URLs: a Data Protection-protected `"operation|key|expiresAt"` token is the *entire* authorization check on `GET/PUT /api/media/{token}` — those two routes are `[AllowAnonymous]` on purpose, same trust model as a real presigned URL.

- **The tenant prefix in the media key is a real isolation boundary, not decoration.** `MediaKeyBuilder.BelongsTo` is what `OutboundMessageService.SendMediaAsync` checks before ever creating a `Message` from a client-supplied `mediaKey` — a key namespaced under a different tenant, or one attempting `..` traversal, is rejected as `MediaKeyNotFoundException` (404), not trusted just because it parses as a string.
- **Upload keys aren't tied to a real `Message` until the message is actually sent.** `CreateUploadUrlAsync` generates a fresh `Guid` to stand in for the eventual message id — the upload happens before `SendMediaAsync` creates the `Message` row, so there's no message id yet to build the key from. The key format still reads as "this message's media" once the send actually happens.
- **A failed inbound media download never dead-letters the whole webhook event.** `InboundMessageProcessor` still creates and saves the `Message` (without `MediaKey`) if `IChannelAdapter.DownloadMediaAsync` throws `ChannelSendException` — it stages `message.media_failed` (ids only, no PII) instead of losing the message entirely. The text/caption half of a message is always worth keeping even when the binary isn't available.
- **`IChannelAdapter` gained `SendMediaAsync`/`DownloadMediaAsync`, not `IWhatsAppCloudApiClient` alone** — the adapter is still the only place that translates a raw `MetaApiException` into `ChannelSendException` via `MetaErrorCatalog`, exactly like `SendTextAsync`. `OutboundMessageDispatchService` opens the local file via `IMediaStorage.OpenReadAsync` and hands the adapter a plain `Stream`; the adapter never touches `IMediaStorage` itself, keeping "talk to Meta" and "talk to local storage" as separate concerns.
- **`MediaMimeCatalog`'s size limits mirror Meta's own** (image 5 MB, audio/video 16 MB, document 100 MB) — `CreateUploadUrlAsync` enforces them before issuing a token, so an oversized upload never even reaches the PUT endpoint.
- **Known gap:** no test exercises an actually-expired signed token (`Get_WithExpiredToken_403` from the original story plan) — there's no time-travel test double for `MediaUrlSigner`'s `TimeProvider` yet, only the DI-registered real one. Add a `MutableTimeProvider` (ORB-B07 needs one anyway, for the 24h service window) before relying on expiry being correct.

### Ventana de servicio y plantillas (ORB-B07)

`Conversation.CanSendFreeForm(now)` (= `IsWindowOpen`, added back in ORB-B03) is now actually enforced: `OutboundMessageService.RequireSendableConversationAsync` takes a `requireOpenWindow` flag — `true` for `SendTextAsync`/`SendMediaAsync` (throws `ServiceWindowClosedException`, 409, if the window's closed), `false` for `SendTemplateAsync` (templates exist specifically to write outside the window, so it would be self-defeating to gate them on it).

- **A template send needs Meta's *raw* variables at dispatch time, not just the already-rendered text.** `Message` gained a `TemplateVariablesJson` column (a JSON string array) purely for this: `OutboundMessageService.SendTemplateAsync` renders the body locally for display (`Message.Body`) but also serializes the exact `variables` list used, because `OutboundMessageDispatchService` has to call Meta's actual `type: "template"` send API with the template name/language/parameters — sending the rendered text as a plain `type: "text"` message would defeat the entire point of templates (Meta rejects free-form text outside the window with 131047 regardless of what the text says) and simply wouldn't work outside the window. This is the one place in the outbound pipeline where "the rendered result" and "what Meta actually needs to be sent" are two different things.
- **Alta de plantillas es local-only, a propósito.** `IMessageTemplateService.CreateAsync` only registers a `MessageTemplate` row (`Draft`) — actually creating/submitting the template to Meta happens in Meta Business Manager, a manual step outside this codebase. `SyncFromMetaAsync` is the one-way sync back: it calls `ListTemplatesAsync`, maps each remote status through `MetaTemplateStatusMapper` (`PAUSED`/`DISABLED` both fold into our own `Disabled` — we don't distinguish them locally), and creates any template Meta has that we don't yet know about with a placeholder body (`"[sincronizado desde Meta]"`) since Meta's template-list response doesn't include a renderable body in a form this sync call reads — a future template-detail call would fill that in properly.
- **`ManageTemplates` is a new Owner/Admin-only permission** — registering/syncing templates is channel configuration, same tier as `ManageChannels`; actually *sending* a template (`SendTemplateAsync`) still only needs the existing `SendMessages` (Owner/Admin/Agent), since sending is a day-to-day action, not configuration.
- **`message_templates` is a normal RLS'd, query-filtered table** — unlike `channel_accounts`/`inbound_webhook_events`/`outbox_events`/`outbound_message_jobs`, templates are always read/written with the tenant already known (an Owner/Admin managing their own catalog), so there's no "resolve the tenant from this row" concern here.
- **Known gap:** no integration test exercises the window actually closing over time (the original plan's `ServiceWindowTests`, e.g. `SendText_25HoursAfterLastInbound_Returns409`) — that needs a `MutableTimeProvider` swapped into the test host's DI container, which doesn't exist yet (same gap ORB-B06 flagged for signed-URL expiry). Unit tests cover the window logic directly via `FixedTimeProvider` instead. Add the mutable provider before trusting either expiry path in a real deployment.

### Estados de entrega y reintentos (ORB-B08)

`InboundMessageProcessor` now branches on the item type parsed from a webhook payload: an `InboundMessage` goes through the existing ORB-B03 path, an `InboundStatusUpdate` (delivery receipts, parsed since ORB-B03 but ignored until now) resolves the local `Message` by `ExternalId` and moves its status forward.

- **Status transitions are monotonic, not "last write wins."** `Message.MarkDelivered`/`MarkRead` never downgrade: a `delivered` receipt arriving after `read` (Meta doesn't guarantee delivery order) is a no-op on `Status`, and `MarkRead` backfills `DeliveredAt` if Meta reports `read` without a preceding `delivered`. `Status.Sent` echoes (Meta re-sends a status the dispatcher already recorded via `MarkSent`) are matched by the `switch`'s `default` arm and dropped — there's no case where re-applying "sent" does anything useful.
- **An unresolvable external id is not an error.** `ProcessStatusUpdateAsync` logs a warning and returns when `FindByExternalIdAsync` misses — Meta can report on message ids this instance never enqueued (e.g. sent from a since-rotated integration), and dead-lettering the whole webhook batch over that would be worse than dropping one status line.
- **Retry only when it's worth retrying.** `OutboundMessageService.RetryAsync` throws `MessageNotRetryableException` (409) unless the message is `Failed` *and* `MetaErrorCatalog.Describe(message.ErrorCode)` says the error is transient — retrying a `131047` (window closed) or any other permanent failure would just fail again for the same reason. A successful retry calls `Message.ResetForRetry()` (`Failed` → `Queued`, clears `ErrorCode`) and re-enqueues a fresh `OutboundMessageJob`, exactly like a first-time send.
- **Retry re-resolves the channel account through `RequireSendableConversationAsync`, not `Conversation.ChannelAccountId` directly** — same connectivity check (`ChannelNotConnectedException`) `SendTextAsync`/`SendMediaAsync` already do, called with `requireOpenWindow: false` since a retry must still work outside the 24h window (the original send may itself have been a template, or the failure may be unrelated to the window). This also means a message can go from `Failed` back to sendable only if its channel is still `Connected` at retry time.
- **Route has no `conversationId`.** `POST /api/tenants/{tenantId}/messages/{messageId}/retry` mirrors the existing `GET .../messages/{messageId}/media-url` shape (ORB-B06) rather than nesting under `/conversations/{conversationId}/messages/...` like the three send endpoints — a retry only ever needs the message id, and the conversation is resolved from it.
- **Known gap:** the new `MessageStatusTests` (status webhook round-trip, transient retry succeeding, permanent-error retry returning 409) are integration tests and can't run without Docker on this machine (`Orbita.IntegrationTests` needs Testcontainers/`pgvector`) — they compile and are wired the same way every other integration test in this suite is, but are unverified end-to-end until Docker is available.

### Reads of RLS'd tables must run inside a tenant scope

The rule was already stated under domain rule 1, but it was being broken in the two hottest paths in the product, so it is worth stating as its own failure mode: **`SET LOCAL app.tenant_id` dies with its transaction.** A repository call made outside one runs with no tenant set, and an RLS'd table answers "no rows" — not an error, not an empty-ish result you'd notice in a debugger, just nothing.

The EF query filter hides this during development: it passes, the SQL looks right, and Postgres returns zero rows anyway. It also hides it in *any* test that does not run against a database with RLS actually applying — which is why ORB-B03's and ORB-B05's integration tests were written but, never having been executed against a real Postgres, never caught it.

What it looked like in practice:

- `OutboundMessageService.RequireSendableConversationAsync` read `conversations` plainly, so **every** send — human or agent — answered `404 Conversation not found` on a conversation that was right there.
- `InboundMessageProcessor` read `contacts`, `conversations` and `messages` plainly, so the contact lookup always missed: the *second* message a customer ever sent created a duplicate contact, violated `ix_contacts_tenant_phone`, and dead-lettered the webhook event. The `FindByExternalIdAsync` idempotency check was silently inert for the same reason.

The fix, and the rule for anything new:

- A read that decides something, followed later by a write, goes in `IUnitOfWork.QueryInTenantScopeAsync`.
- A read whose result the write *depends on having found* goes in **`IUnitOfWork.ExecuteAndSaveInTenantScopeAsync`** (new): one transaction, tenant set once, reads and writes and the save all inside it. Splitting those two across separate scopes is what produced the duplicate contact.
- `ExecuteInTenantScopeAsync` remains for bulk operations that save themselves (`ExecuteUpdate`/`ExecuteDelete`).

`IUnitOfWork` gaining a member breaks every hand-written implementation of it in the test projects, and git does not mark that as a conflict — `Orbita.UnitTests/TestSupport/PassThroughUnitOfWork` is now the one shared implementation, so there is a single place to update.
### Borrar un asistente que ya trabajó (corrección de ORB-C10)

`DELETE /api/tenants/{tenantId}/ai-agents/{agentId}` devolvía **500** para cualquier asistente que hubiera corrido alguna vez: `ai_runs.agent_id` es `RESTRICT`, así que el borrado llegaba a Postgres y volvía como violación de clave foránea sin manejar. Alcanzaba con que el asistente hubiera indexado un documento (ORB-C02 registra un run por llamada de embedding), no hacía falta que respondiera nada.

La FK es `RESTRICT` a propósito y no se cambia: `ai_runs` es el libro de consumo del que salen la medición de ORB-A13 y la facturación de ORB-A12, así que la historia de un asistente le sobrevive.

Ahora `AiAgentService.DeleteAsync` pregunta primero (`IAiRunRepository.ExistsForAgentAsync`) y lanza `AgentHasHistoryException` → **409**, con un mensaje en español que el dashboard puede mostrar tal cual. Borrar sigue disponible para el caso que de verdad sirve —un asistente creado por error, que nunca atendió a nadie—; para todo lo demás ORB-C10 ya tenía el verbo correcto, `PATCH .../enabled`.

`conversations.ai_agent_id` **ahora sí tiene clave foránea** (`AddConversationAgentForeignKey`, `RESTRICT`). El DBML la declaraba desde siempre (`Ref: conversations.ai_agent_id > ai_agents.id`) y faltaba en el modelo de EF; hasta ORB-C04 daba igual, porque nadie escribía esa columna. La guarda de 409 y la clave foránea dicen lo mismo a propósito: la primera da un error que se puede mostrar, la segunda lo hace cumplir para cualquier otro camino que asigne un asistente a una conversación —el enrutador de ORB-C08, cuando exista— sin depender de que ese código se acuerde.

**El registro del español importa y se revisa.** Todo texto en español que produce el backend está en tuteo, incluidas las reglas que ORB-C04 le manda al modelo. No es cosmético: un prompt escrito en voseo le enseña al modelo a contestar en voseo, a los clientes de todos los tenants — y Órbita atiende Colombia, México y España, donde tú, vos y usted no son intercambiables. La regla que se le da al modelo es **espejar al cliente**, no elegir un trato por él.

### El agente responde (ORB-C04)

First slice of Track C's Épica C2, and the first thing in this codebase that reacts to an outbox event: `AgentReplyIntegrationHandler` is the first `IIntegrationEventHandler` implementation — the seam ORB-B04 built had been empty until now.

- **The assistant hangs off `message.received`, not off `InboundMessageProcessor`.** That's the rule domain rule 3 already stated ("don't bolt side effects directly onto command handlers"), and here it buys something concrete: ingesting a webhook must be fast (Meta retries anything it doesn't get a prompt 200 for) and answering is slow (a model call, six seconds at p95). Reacting to the committed event puts them on separate clocks, so a model outage delays replies instead of losing messages.
- **`AgentPromptBuilder` is shared with ORB-C11's test bench, deliberately.** The test bench exists so an owner sees how their assistant will answer *before* a customer does; the moment the two build their prompt differently it stops predicting the product. Both get `AgentPromptBuilder.ConversationRules` — the acceptance criteria (answer in the customer's language, never invent, offer a person when you can't answer) written for the model instead of for us.
- **`IKnowledgeSearchService` has two entry points.** `SearchAsync` takes a caller and checks `ManageAiAgents`; `SearchForAgentAsync` takes none, because the assistant consulting its own documents has no human caller — the tenant was established by the inbound message, not by a request. Same split, for the same reason, as `IAuditLogger.RecordAsync` vs `RecordSystemActionAsync`: "nobody authorized this" should be something you write down, not something you get by passing null.
- **`conversations.ai_agent_id` is finally written.** The column has existed since ORB-B03 with nothing filling it. `Conversation.AssignAgent` is idempotent and never reassigns: whichever assistant took the conversation keeps it, because moving a live conversation to a different assistant is ORB-C08's decision, not a side effect of answering. A conversation with none gets `IAiAgentRepository.FindEnabledByTenantAsync` — the tenant's enabled assistant, oldest first when there's more than one, which is a placeholder for C08's routing, not a rule worth defending.
- **Every reason to stay quiet is a value, not an exception.** `AgentReplyDecision` enumerates them (no assistant, disabled, human assigned, window closed, nothing to answer, not an inbound message, model produced nothing). They're ordinary product states — throwing would fill the dispatcher's log with errors for the most common case. A model or channel failure *does* throw: that one is worth retrying and worth seeing.
- **`IOutboundMessageService.SendAgentReplyAsync` is how the reply leaves**, not a private copy of the send path: the acceptance criterion is explicit that it goes out "por la cola de salida normal, con su control de tasa", so it inherits ORB-B05's persist-first guarantee, retry policy and per-account rate limit. It takes no caller id and runs no permission check — the sender isn't a person; what stands in for authorization is that the conversation already has this assistant assigned. `Message.OutboundAgentText` enforces the pairing that makes that legible: `sent_by_user_id` null, `ai_run_id` always set.
- **`IAiRunRecorder.Record` now returns the staged run's id** so the reply can carry it. Like `IAuditLogger`, it still only stages — `SendAgentReplyAsync`'s own `SaveChangesAsync` commits the run, the reply, the queued job and the agent assignment in one transaction. The one exception: a model that answers with nothing still gets its run saved, because the call was made and it cost money.
- **A message with no text is not answered at all.** An image with no caption, a sticker, a location: replying "no entendí" to every photo a customer sends is worse than letting a person look at it.
- **Known gaps:** the `HumanIsHandlingIt` guard's `AssigneeId` half can't be exercised by a test yet — nothing sets `Conversation.AssigneeId` until ORB-B15. Its ORB-C07 half (`IsWaitingForHuman`) is proven end to end by `HandoffApiTests`. The "latencia percibida por debajo de 6 segundos en el percentil 95" criterion is not measured anywhere; `ai_runs.latency_ms` is where the data to measure it lands.

### Guardrails (ORB-C06)

`AgentGuardrails` (Domain, pure) decides when the assistant must not answer and when what it produced must not be sent. `AgentConversationResponder` asks it twice: before the model call (`InspectIncoming`: blocked topic, too many replies in the 24h window, a loop) and after (`InspectReply`: empty, longer than WhatsApp's 4096, transcribed its own scaffolding, identical to its previous reply).

- **Blocked topics are not part of the draft.** `PUT /api/tenants/{tenantId}/ai-agents/{agentId}/guardrails` applies immediately, like `PATCH .../enabled`. A setting whose purpose is to make the assistant *stop* talking about something cannot wait for Publicar. `SaveAiAgentBody` does not carry it.
- **Whole-word matching, not substring**, case- and accent-insensitive. Substring would fire "talla" on "pantalla", "precio" on "apreciamos", "cita" on "felicitaciones" — and the failure is silence, which nobody can diagnose from outside. Accepted cost: "precio" does not catch "precios"; the owner adds the plural. Pinned by `AgentGuardrailsTests` so nobody "fixes" it back.
- **The out-of-scope reply is the owner's, not ours** (`AiAgent.OutOfScopeReply`, editable, 500 chars). It never passes through the model, so it cannot mirror the customer's language — known and accepted: generating it would put the model next to the very subject being blocked. The default promises nothing the product cannot do: no "ya les aviso". Since ORB-C07 an out-of-scope subject also puts the conversation in the human queue, but the default still does not promise a reply — nobody is assigned until ORB-B15.
- **An out-of-scope subject gets that sentence; a loop or an exhausted window gets silence.** The last two mean the assistant already said too much.
- **Every block stages `agent.reply_blocked`** `{conversationId, agentId, reason, topic}`. `topic` is a *deliberate exception* to the outbox's ids-and-enums rule: it is free text the owner wrote (their configuration, not customer data), and it is the only thing that answers "why did my assistant go quiet?".
- The out-of-scope reply is sent through `IOutboundMessageService.SendSystemReplyAsync` and lands as `authorKind: "System"` — the first producer of that value.
- "Ninguna respuesta contiene datos de otro cliente" is not a filter here, deliberately: retrieval is scoped by tenant and agent under RLS, so it is enforced by construction and proven by the isolation tests (which carry a positive control — see HANDOFF).
- Limits: 50 topics, 120 chars each, trimmed, blanks dropped (an empty topic would match every message), duplicates dropped case-insensitively.

### El agente ejecuta acciones (ORB-C05)

`AgentToolExecutor` is the only place a model's arguments become a change in the CRM. `AgentConversationResponder` offers the agent's enabled tools, runs what the model asks for, sends the results back, and asks again — at most `MaxToolRounds` (3) rounds with tools, then one final call with **no tools offered**, so the customer always gets text and one reply costs at most four model calls.

- **Tools act only on what the conversation fixes, never on ids the model supplies.** `AgentToolContext` carries tenant, agent, contact and conversation; `crear_oportunidad` always uses the tenant's default pipeline and first stage for that contact, `mover_etapa` moves that contact's most recent *open* opportunity (not on a won/lost stage) to a stage **by name**. The model knows the words on the board, not the ids — and an id it supplied would be the one path to another tenant's record. `An_assistant_cannot_create_anything_in_another_tenant` names the victim tenant in the arguments and asserts nothing lands there (with a positive control first).
- **`IOpportunityService.CreateForAgentAsync` / `MoveContactOpportunityForAgentAsync`** are the agent entry points: no permission check (the actor isn't a person — what authorizes it is the tool being enabled on the agent), audited through `RecordSystemActionAsync` with `actor_type = AiAgent` and the `agentId` in the diff.
- **A failing tool never breaks the reply.** Bad arguments, a missing stage, or an exception all become `AgentToolResult(Succeeded: false)` with a Spanish sentence the model can relay; internal error text never reaches the model or the customer.
- **`LlmMessage.AssistantToolCalls` + `tool_calls` on the OpenAI wire** were missing from ORB-C01: the chat format rejects a `tool` message that doesn't answer a `tool_calls` entry in the turn before it, so the second round of any tool loop would have failed against a real provider while passing every fake. Known gap: the Ollama adapter does not echo `tool_calls` yet (it has no configured `BaseUrl` in this deployment).
- **`AiTool.ResultsIn`** names the module where a tool's result shows up (`"pipeline"` for both opportunity tools, null for retrieval). It exists because the frontend asked to tell "available" from "available, but you cannot see what it did yet" — this answers the half the backend actually knows. Whether a given screen has shipped is the frontend's own fact; hardcoding it here would be wrong the day it does.
- **Availability:** `crear_oportunidad` and `mover_etapa` are `IsAvailable: true`. `agendar_cita` stays unavailable (there is no agenda module to write to). `escalar_a_humano` was unavailable for the matching reason until ORB-C07 built the queue it hands over to. `consultar_conocimiento` is not a callable tool: it runs as retrieval before the first call, gated by the same switch.
- Tools are part of the **draft** (ORB-C10); guardrails are not (ORB-C06). An agent needs a publish to start using a newly enabled tool.

### Registro de consumo de IA (ORB-C09, parte que no depende de A13)

`ai_runs` gains two orbita-schema.dbml columns: `tools_called` (jsonb, tool names) and `retrieved_chunk_ids` (uuid[]). One run per model call, so in a tool loop the round that asked for tools carries them and the follow-up round carries none — that is literally what happened, and what billing per action needs. Retrieved fragments are recorded on the first round only: they went into every round's prompt but were retrieved once, and a sum over runs must not double-count them. Existing rows get `[]` / `'{}'`.

**Not done, and not doable yet:** "alimenta directamente la medición de facturación" is ORB-A13 (usage metering), which does not exist. `prompt_version`, `temperature_used` and `turn_number` from the DBML are not added — they have no consumer (`was_handoff` was deferred too, and ORB-C07 added it), and this repo does not pre-create columns for features that do not exist.

### Enrutador (ORB-C08)

`RoutingPolicy.Decide` (pure) picks who handles an inbound message; `AgentConversationResponder` asks it before resolving the assistant.

- **Rules** (`routing_rules`, RLS'd, tenant-first index) are an ordered list: `{position, name, channel?, keyword?, agentId?}`. First match by position wins; `agentId` null means "leave it for the team". No match falls back to the tenant's enabled assistant — the pre-routing behaviour, so a tenant that never configures rules sees nothing change. `PUT /api/tenants/{tenantId}/routing/rules` replaces the whole list and **the array order is the evaluation order** ("visible y configurable"). A rule pointing at another tenant's assistant is refused (404); the FK to `ai_agents` is `RESTRICT`.
- **`PUT` answers with the saved list, not 204**: the same flat array `GET` returns, with `position` recalculated 0-based from the array order — so a screen repaints with what was stored rather than with what it believed. The wrapper (`{ rules: [...] }`) is on the request only; the frontend correctly keeps that asymmetry in one file.
- **Rule ids do not survive a save.** `ReplaceAsync` removes and re-creates, so every `PUT` mints new ids even for rules that did not change. Harmless today — nothing stores a rule id, and the array order is the source of truth — but it makes `RoutingDecision.MatchedRuleId` useless for any future "which rule matched, historically" question. Fixing it means accepting ids on the request and matching on them, which nothing needs yet; don't build it speculatively, do know it before wiring rule ids into analytics.
- **A rule with neither channel nor keyword matches everything**, which silently strands every rule below it. Accepted as legitimate (it is the "catch-all, last" case) and deliberately not refused by the API; the dashboard warns when such a rule is not last, which is the right layer for a judgement call that has a valid use.
- **Not in orbita-schema.dbml**, which has no routing table — an addition like `tenant_model_preferences`. The story also names *etiqueta* as a condition; there is no `tags` table yet, so that condition does not exist rather than existing and never matching.
- **Sticky**: rules choose who takes a *new* conversation; one that already has an assistant keeps it (ORB-C04's invariant). They never bounce a live exchange between assistants.
- **Keywords match whole words**, accent- and case-insensitive, via the same `WholeWordText` ORB-C06's blocked topics use.
- **Rules decide *who*, hours decide *whether that one answers now*** — they are not two switches racing each other, and the frontend was right to ask which wins. `RoutingPolicy.Decide` runs first and picks the assistant (or leaves it for the team); only then are *that* assistant's hours checked. So a rule sending WhatsApp to assistant X at 11pm, with X set to `LeaveForTeam`, leaves the conversation for the team — and the assistant is not assigned to it, so it is not claimed by someone who will never answer. A rule never overrides hours and hours never re-route.
- **Business hours live on the assistant** (`ai_agents.business_hours`, jsonb, as the DBML specifies; null = always on), evaluated in `tenants.timezone`. `OutsideHours` is `AssistantAnswers` or `LeaveForTeam` — both halves of "fuera de horario puede contestar el agente o dejarse en cola". Checked *before* the assignment, so a message at 2am is not claimed by an assistant that then never answers. Overnight shifts are two slots; a slot that closes before it opens is refused. Unknown time zone → UTC, not a silenced assistant. `PUT .../ai-agents/{agentId}/business-hours`; not part of the draft.
- Mapped with a value converter, not an owned JSON type: EF binds owned types through constructors it cannot satisfy for a positional record.

### Caché semántica de respuestas (ORB-C12)

`agent_answer_cache` keeps answers an assistant already gave so a near-identical question
can be answered without a model call. Off by default, through `PUT/GET
/api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache`.

- **The API takes a named level, never the number.** `SemanticCacheLevel` is `Off` |
  `Conservative` | `Balanced` | `Aggressive`; `SemanticCacheLevels` is the one place each
  becomes a cosine threshold (0.97 / 0.93 / 0.88), stored in
  `ai_agents.semantic_cache_threshold`. This is the same call ORB-C10 made for
  `temperature`, and the frontend was right to push back on the first draft of this
  endpoint, which exposed the float: "is 0,92 a lot?" has no answer for the owner of a
  bakery, and guessing wrong costs a customer the answer to a question they did not ask.
  Retuning a level is a backend change with no frontend release; the way back from a stored
  number is nearest-match, so retuning never makes an assistant's level unreadable.

- **Only a conversation's first answer is ever stored or reused, and only if no tool ran.**
  A later turn was shaped by the turns before it — it answers that exchange, not the
  question — and an answer that created an opportunity would, replayed, claim an action
  that never happened for the new customer. Both refusals are pinned by tests.
- **Invalidation is a fingerprint, not a delete.** `AgentAnswerFingerprint.Compute` hashes
  the assistant's system prompt, temperature, max tokens, tools and blocked topics together
  with a summary of its documents (`IKnowledgeDocumentRepository.SummarizeForAgentAsync`:
  count plus the latest `created_at`/`indexed_at`). An entry whose fingerprint no longer
  matches is simply never returned, so uploading, reindexing or deleting a document — or
  publishing new instructions — retires every earlier answer without a `DELETE` that would
  have to run inside the right tenant scope to do anything at all. Stale rows are dead
  weight, not a correctness risk; there is no cleanup job yet.
- **The hit rate is read off `ai_runs`, not a counter.** Every lookup embeds the question
  and records that embedding run with `finish_reason` `cache_hit` or `cache_miss`. A hit's
  reply carries that run as its `ai_run_id`, so every message still has a run explaining
  it. `GET .../semantic-cache` returns hits, misses and the rate; null rate means nobody has
  asked yet, which is not the same as 0%.
- **The table is RLS'd and tenant-first indexed** like any other assistant-facing table, and
  the lookup narrows by `(tenant_id, agent_id, fingerprint)` before comparing vectors.
  `SemanticCacheApiTests` proves one tenant's answers are never offered to another, with a
  positive control so a cache that never hits cannot pass by doing nothing.
- **Cost:** a miss pays one embedding on top of the model call (retrieval embeds the same
  question again — accepted, it is ~USD 0.0000002); a hit pays only that embedding.

### Casos de prueba guardados (ORB-C11, la mitad que faltaba)

`agent_test_cases` (RLS'd, tenant-first index, not in orbita-schema.dbml — same class of
addition as `routing_rules`) closes the acceptance criterion "se pueden guardar casos de
prueba y volver a ejecutarlos tras un cambio", which had been open through three rounds of
frontend coordination because nobody had decided where the cases live.

- **Server, not the browser.** The value of a saved case is re-running it *after* a change,
  and `localStorage` loses it exactly then — on another machine, in another browser, after
  clearing site data. It is also the owner's work, not a preference of their browser.
- **There is no "run" endpoint.** The screen reads a case and posts its turns to
  `POST .../test-chat`. A second entry point would be a second place the prompt gets built,
  which is the one thing the test bench must never have (it shares `AgentPromptBuilder` with
  ORB-C04 so that what the owner tests is what the customer gets).
- **`AgentTestTurn`/`AgentTestRole` moved from Application to Domain** so the saved case and
  the exchange posted to the bench are literally the same type — the frontend asked for one
  shape of the history, not two that happen to match today.
- `GET|POST /api/tenants/{t}/ai-agents/{a}/test-cases`, `DELETE .../test-cases/{id}`.
  `ManageAiAgents`, like the rest of ORB-C10/C11. Limits: 20 cases per assistant (409 past
  it, counted inside the insert's own transaction so two saves cannot both see nineteen),
  40 turns, an 80-char name the owner writes — never derived from the first message, because
  a case called "hola" tells nobody what it is for six weeks later.
- Deleting a case through **another assistant's route is a no-op**, not a success: the
  tenant check alone would leave the `agentId` in the URL meaningless. Deleting one that is
  already gone is not an error.
- The FK to `ai_agents` is **CASCADE**, unlike `ai_runs`' RESTRICT: scratch work must never
  be the reason a deletable assistant cannot be deleted.

### Traspaso a humano (ORB-C07)

Last story of Track C, and it was **mis-labelled as blocked by ORB-B15** through three
handoff documents. B15 asks "which of my teammates is handling this conversation"; C07
asks "is this conversation still the assistant's". Those are different questions, and only
the second one is needed to stop lying to a customer who asks for a person. The queue —
conversations waiting for *anybody* — is Track C's to build; picking a person from it is
still B15's.

- **`conversations` gains three columns** (`handoff_requested_at`, `handoff_reason`,
  `handoff_summary`), not in orbita-schema.dbml — same class of addition as
  `last_message_preview`. The timestamp is its own column rather than inferred from
  `status = 'pending'` because status changes for ordinary reasons: `RegisterInbound`
  reopens a Pending thread on the next message, which is right for a thread nobody got to
  and would silently hand a handed-over conversation back to the assistant. That one
  `if (!IsWaitingForHuman)` in `Conversation.RegisterInbound` is the whole last acceptance
  criterion ("el agente no vuelve a intervenir salvo que un humano lo reactive").
- **Four triggers, and the split between them is deliberate.** `HandoffTriggers.Detect`
  (Domain, pure) reads the customer's own words before any model call — an explicit request
  and plain frustration — with the same whole-word matching as C06's blocked topics.
  `escalar_a_humano` covers the half a phrase list never will (sarcasm, politeness masking
  anger), which is why it is the model's tool and not a keyword. A blocked topic is the
  third. The fourth is the assistant failing: a loop, an exhausted window, an unsendable
  answer, or no text at all.
- **The phrase list errs toward *not* handing over**, the opposite direction from a normal
  guardrail, because the costs are asymmetric: a false positive takes the conversation away
  from the assistant until a person gives it back, so an ordinary message gets no instant
  answer it could have had. `"ya te dije"` and friends are deliberately absent — repetition
  is judged objectively instead (the same message three times), which needs no guess about
  tone.
- **Every reason the assistant goes quiet now ends in the queue.** Before C07, C06's three
  outcomes left the customer with the assistant that had just declined to help them, which
  is the "círculo" the story is written against. What did *not* change is what the customer
  hears: an out-of-scope subject still gets the owner's sentence, a loop or an exhausted
  window still gets silence, and a garbled model answer still gets silence — an apology for
  a failure they have not seen is one more message from an assistant with nothing to say.
- **Routing and business-hours decisions never hand over.** `LeftForTeamByRule` and
  `OutsideBusinessHours` are per-message decisions; a handoff is sticky, so marking them
  would permanently disable the assistant on that thread because one message arrived at 2am.
- **The summary is written *after* the handoff, reacting to its outbox event — measured, not
  assumed.** The first version wrote it inside `RequestAsync`, and against Neon with the real
  models a customer who asked for a person waited **13.8 s** for the sentence telling them
  so, against **6.6 s** for an ordinary answer: a four-second model call for a note meant for
  somebody else sat in front of them. Now `RequestAsync` is one transaction with no model
  call, and `HandoffSummaryIntegrationHandler` calls `WriteSummaryAsync` on
  `conversation.handoff_requested` — domain rule 3, doing exactly what it says. It also fixed
  resilience in passing: the inline version had to swallow a provider failure and lose the
  note for good, while the handler lets it propagate so the dispatcher **retries with
  backoff**. The queue therefore shows `summary: null` for a few seconds after a handoff;
  the dashboard has to treat it as "todavía no" and not as "no hay". One cheap call per
  handoff (`LlmTask.Classify` — a three-sentence internal note has the economics of a
  classification, and a task of its own would mean a config entry per provider for a
  distinction nobody would act on). When the model hands over through the tool it writes the
  note itself (`resumen`), and the handler skips the call; `AttachHandoffSummary` never
  overwrites a note and drops one that arrives after a person gave the conversation back.
- **The summary is customer data and never leaves the RLS'd table.** The outbox event
  (`conversation.handoff_requested`) carries ids and the reason enum only — not the summary,
  unlike C06's `topic`, which was the *owner's* configuration and therefore safe to put
  there.
- **`ai_runs.was_handoff`** is finally written: it is in the DBML and ORB-C09 left it out
  because nothing could set it before this story.
- **The queue pages with a cursor and reports a total.** The frontend asked for both and was
  right about why: a queue is precisely the list that grows when the team cannot keep up, so
  a silent cap of 50 would hide the 51st customer on the worst day the business has. It is
  served **oldest wait first** — the person who has waited longest is closest to giving up,
  and newest-first starves exactly them. The cursor encodes **ticks, not milliseconds**:
  ascending keyset paging with `>` re-includes the boundary row when the cursor is floored,
  which made page two start with page one's last row. ORB-C02's descending list with `<` is
  unaffected, which is why it took a paging test on this list to surface it.
- **`handoffReply` is optional in `PUT .../guardrails`.** The frontend spotted that a
  required field on an endpoint that already had a consumer would break its limits screen
  for a business owner rather than for us; null keeps the stored sentence, and existing rows
  are seeded with the default by the migration. Its default promises only what the product
  does — the assistant stops answering and the conversation is left for the team — and
  deliberately not "en un momento te escriben", which would be C06's walked-back promise
  creeping back in through a different sentence.
- **New permission `ViewInbox`** (all four roles) for reading the queue; giving a
  conversation back needs `SendMessages`, because handing a customer back to a machine is
  acting on the conversation rather than viewing it. ORB-B12's inbox listing is the other
  reader `ViewInbox` was named for.
- **The repetition trigger only counts the current 24h window.** A conversation here is a
  lifetime thread, so counting identical questions across all of it would hand a customer
  to a person for asking the same thing once a month. Found measuring against seeded data,
  where long threads legitimately repeat a question; `ReplyContext.WindowHistory` is the
  bounded list, the prompt still gets the full 20 turns.
- **The page and its total come from one SQL statement.** They used to be two, and under
  READ COMMITTED each statement sees its own snapshot: a handoff committing between them
  returned `items: []` with `total: 1`. A parallel test run caught it. `contactName` is
  typed non-nullable for the same reason the frontend asked about it — `display_name` is
  NOT NULL and the join is inner.
- **`lastMessagePreview` in a queue item is almost always our own handoff sentence**, not
  the customer's last words, because it is B03's "last message in either direction" and
  the handoff sentence is sent right after. Found capturing a real item against Neon. Left
  as is on purpose — the column is B03's and the inbox will rely on that meaning; `summary`
  is what a queue screen should show. "The customer's last message" would be a new field.
- **Known gap: "se notifica en vivo" is the outbox event, not a WebSocket push.** The
  frontend confirmed it has no SignalR client at all (no `@microsoft/signalr`, no
  `HubConnection`), and ORB-B14 owns the inbox's realtime story, so a hub here would be
  code with no consumer and a guaranteed collision. Polling `GET .../handoffs` is what the
  dashboard does today — the same answer C02 gave for document status.

### Criterios de aceptación medidos (C02, C03, C04)

Three Track C criteria had been marked done without ever being measured. They were
measured on 2026-09-16 with the real models — against Neon first, then against an isolated
local Postgres with the full seed, because Neon turned out to have another API instance
somewhere consuming the same queues (see HANDOFF). None of the three was met.

**ORB-C03 — search under 200 ms with 100.000 chunks.** Measured at **650 ms**. The query
filtered by agent through a `JOIN` to `knowledge_docs`, and with that shape the planner
never used the HNSW index: it walked `doc_id` and sorted the survivors — exactly the debt
`AddKnowledgeChunkHnswIndex` predicted. `KnowledgeChunkRepository.SearchAsync` now filters
with `doc_id IN (…)` and joins for the title only after the limit, and sets
`SET LOCAL hnsw.iterative_scan = strict_order` so the approximate scan keeps going until
enough rows pass the agent filter (without it, a search can come back short, or empty when
the nearest vectors belong to another agent). **1,4 ms** on the same corpus. Needs pgvector
0.8+, which the test image, the local container and Neon all have (0.8.6).
- **Don't "simplify" it back to a join.** It reads more naturally and it is the 650 ms one.
- The endpoint as a whole still spends ~400–600 ms, all of it embedding the question at
  the provider. The criterion is about the search; the network call is not ours.

## Mandatory engineering conventions

1. **SOLID, strictly.** Every class/service has one reason to change; depend on abstractions (interfaces) at layer boundaries, not concrete infrastructure; prefer composition over inheritance for cross-cutting behavior. If a controller or service is doing more than one job, split it.
2. **Ultra-strict typing.** `Nullable` stays enabled — never annotate around it. No `dynamic`, no bare `object` where a concrete or generic type works, no `!` null-forgiving operator without a comment justifying the invariant. Public APIs (DTOs, controller signatures) must be fully typed, never `object`/`JsonElement` catch-alls.
3. **Nothing ships untested.** Every feature (endpoint, service, domain rule) lands with unit tests and, where it touches persistence or the HTTP pipeline, integration tests, in the same set of commits. A feature branch without tests is not done.
   - `Orbita.UnitTests` covers `Domain`/`Application` in isolation (Moq for repository interfaces, a fake `TimeProvider` — see `TestSupport/FixedTimeProvider` — for deterministic timestamps). No database, no HTTP.
   - `Orbita.IntegrationTests` boots the real `Orbita.Api` via `WebApplicationFactory<Program>` against a real Postgres container (Testcontainers, `pgvector/pgvector:pg17` image). `TenantsApiFixture` is the one shared fixture for every controller test class (`IClassFixture<TenantsApiFixture>`) — it migrates as the container's admin/owner role and configures the app under test to connect as `orbita_app` instead, so RLS is actually exercised; don't spin up a second Postgres container per test class. See `TenantIsolationTests` for how to open a throwaway admin-role `OrbitaDbContext` when a test needs to bypass RLS on purpose (e.g. to check the EF query filter in isolation).
4. **Commit checkpoints.** Commit frequently enough that no more than ~2 hours of work sits uncommitted. Message format: `<type>: <short imperative description>`, max 72 characters, English only (never mix languages in one message). Types: `feat`, `fix`, `refactor`, `chore`, `test`, `docs`, `style`. One logical unit of work per commit — if the message needs "and", split it into two commits. **Never add a `Co-Authored-By`, `Claude-Session`, or any other AI/tool attribution trailer to a commit message in this repo** — the author is the human contributor, full stop.
5. **Branching.** `feature/<short-descriptive-name>` branches from `develop` and merges back into `develop`; `hotfix/<short-descriptive-name>` branches from `main` and merges into both `main` and `develop`. Never branch a feature off another feature branch without explicit coordination.
