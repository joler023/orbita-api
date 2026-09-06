# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

`Orbita` is the backend API for Órbita: a multi-tenant conversational CRM (WhatsApp + Instagram, TikTok in phase 1) with AI agents, built on ASP.NET Core with a Clean Architecture layering (Domain / Application / Infrastructure / Api). The `Tenant` vertical slice (create + get) is the reference implementation of the full pattern — every new aggregate should follow the same shape across the four projects rather than inventing a new one. The `Identity` slice (`User`, `Membership`, organization registration under `POST /api/organizations`) is the second one, and the one to copy for anything tenant-scoped: it is what actually enforces the isolation rule below (EF query filter + Postgres RLS + tenant-first index), not just declares it.

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
  - A migration that creates a new tenant-scoped table must enable RLS on it (`ALTER TABLE ... ENABLE ROW LEVEL SECURITY` + a `tenant_isolation` policy using `current_setting('app.tenant_id', true)`, `NULLIF`-guarded against the empty string) — `orbita_app` already has the default-privilege grants to read/write it (see the same migration).
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
   - Any write goes through `IUnitOfWork.SaveChangesAsync`, which opens an explicit transaction and sets `app.tenant_id` with `SET LOCAL` semantics inside it before saving — never call `DbContext.SaveChangesAsync` directly for a tenant-scoped write, and never set that session value outside of a transaction that also does the save (a pooled connection does not reliably carry session-level state between separate calls).
   - Give it a composite index with `tenant_id` first.
2. **Events over ad-hoc state.** `events` is an append-only log of everything that happens in the product. The test for whether something needs its own table: *can it be reconstructed from the event log?* If yes, it's derived (a `*_metrics`/`*_daily` table at most, regenerable by delete + recompute). If no (e.g. `ai_feedback`, `message_annotations` — a human judgment that only exists once), it gets a first-class table. Default new features to emitting events rather than inventing new tables.
3. **Transactional Outbox.** `outbox_events` is written in the *same transaction* as the domain change it describes, then a dispatcher publishes it. This is the integration seam for anything reacting to a domain event (analytics, webhooks, integrations) — don't bolt side effects directly onto command handlers.
4. **Append-only tables** (`events`, `outbox_events`, `audit_log`): no `UPDATE`/`DELETE` ever. A wrong fact is corrected with a compensating row, never mutated in place.
5. **Partitioned-by-design tables.** `messages` and `events` are partitioned by date range from day one (`PARTITION BY RANGE`), and their primary key includes the partition column (`(id, created_at)` / `(id, occurred_at)`). Any EF model for these must match that composite key shape.
6. **Meta/WhatsApp business rules are explicit domain concepts, not infra details**: `conversations.window_expires_at` (24h free-form service window) and `message_templates.status = approved` (required to message outside that window) must be modeled and enforced in application logic, not left implicit.
7. **Secrets and media never live in Postgres.** Channel credentials are referenced by `credentials_ref` (Secrets Manager ARN) with tokens encrypted via KMS; media is referenced by `media_key`/`avatar_key` (R2 object key) and served only via signed URLs.
8. **No PII in `events.properties`.** Message bodies, phone numbers, and emails live in `messages`/`contacts`, which have access and deletion controls; the event log only carries identifiers and measures.

## Mandatory engineering conventions

1. **SOLID, strictly.** Every class/service has one reason to change; depend on abstractions (interfaces) at layer boundaries, not concrete infrastructure; prefer composition over inheritance for cross-cutting behavior. If a controller or service is doing more than one job, split it.
2. **Ultra-strict typing.** `Nullable` stays enabled — never annotate around it. No `dynamic`, no bare `object` where a concrete or generic type works, no `!` null-forgiving operator without a comment justifying the invariant. Public APIs (DTOs, controller signatures) must be fully typed, never `object`/`JsonElement` catch-alls.
3. **Nothing ships untested.** Every feature (endpoint, service, domain rule) lands with unit tests and, where it touches persistence or the HTTP pipeline, integration tests, in the same set of commits. A feature branch without tests is not done.
   - `Orbita.UnitTests` covers `Domain`/`Application` in isolation (Moq for repository interfaces, a fake `TimeProvider` — see `TestSupport/FixedTimeProvider` — for deterministic timestamps). No database, no HTTP.
   - `Orbita.IntegrationTests` boots the real `Orbita.Api` via `WebApplicationFactory<Program>` against a real Postgres container (Testcontainers, `pgvector/pgvector:pg17` image). `TenantsApiFixture` is the one shared fixture for every controller test class (`IClassFixture<TenantsApiFixture>`) — it migrates as the container's admin/owner role and configures the app under test to connect as `orbita_app` instead, so RLS is actually exercised; don't spin up a second Postgres container per test class. See `TenantIsolationTests` for how to open a throwaway admin-role `OrbitaDbContext` when a test needs to bypass RLS on purpose (e.g. to check the EF query filter in isolation).
4. **Commit checkpoints.** Commit frequently enough that no more than ~2 hours of work sits uncommitted. Message format: `<type>: <short imperative description>`, max 72 characters, English only (never mix languages in one message). Types: `feat`, `fix`, `refactor`, `chore`, `test`, `docs`, `style`. One logical unit of work per commit — if the message needs "and", split it into two commits. **Never add a `Co-Authored-By`, `Claude-Session`, or any other AI/tool attribution trailer to a commit message in this repo** — the author is the human contributor, full stop.
5. **Branching.** `feature/<short-descriptive-name>` branches from `develop` and merges back into `develop`; `hotfix/<short-descriptive-name>` branches from `main` and merges into both `main` and `develop`. Never branch a feature off another feature branch without explicit coordination.
