# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

`Orbita` is the backend API for Órbita: a multi-tenant conversational CRM (WhatsApp + Instagram, TikTok in phase 1) with AI agents, built on ASP.NET Core. This is currently a **fresh scaffold** — a default `dotnet new webapi` (controllers, no auth, no persistence) with Scalar added for API docs. There is no database access, no domain model, and no test project yet: the first work here is building all of that from scratch, so treat every convention below as binding from commit one, not as a retrofit.

The authoritative data model lives one level up at `../docs/orbita-schema.dbml` — read it before creating or changing any entity. Do not infer schema from guesswork; the DBML file's inline notes encode real product/business rules (see Architecture below).

## Commands

- Build: `dotnet build Orbita.slnx`
- Run: `dotnet run --project Orbita.Api`
- Run with hot reload: `dotnet watch run --project Orbita.Api`
- Format: `dotnet format Orbita.slnx`
- Test (once a test project exists): `dotnet test`
  - Single test: `dotnet test --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`
- API docs (Development only): OpenAPI document is mapped via `MapOpenApi()`, browsable through Scalar's UI mapped by `MapScalarApiReference()` (default route `/scalar/v1`).

There is no test project in the solution yet. The first feature branch that adds backend logic must also add a test project (e.g. `Orbita.Api.Tests`, xUnit) and reference it from `Orbita.slnx` — nothing ships untested (see Testing below).

## Architecture

- Solution format is `.slnx` (`Orbita.slnx`), currently referencing a single project: `Orbita.Api/Orbita.Api.csproj`.
- Target framework: `net10.0`. `Nullable` and `ImplicitUsings` are already enabled in the csproj — never disable them.
- API style: controller-based (`AddControllers()` / `MapControllers()`), not Minimal APIs. Keep new endpoints consistent with this.
- `Program.cs` is the single composition root today; as the app grows, extend it with extension methods (`AddXyzServices`, `MapXyzEndpoints`) rather than letting it grow unbounded.

### Domain rules from `orbita-schema.dbml` (binding on all backend code, not just DB migrations)

1. **Tenant isolation.** Every business entity carries `tenant_id`, and it must be enforced at three layers: EF Core global query filter, PostgreSQL Row-Level Security (`current_setting('app.tenant_id')`), and `tenant_id`-first composite indexes. A new entity that skips any of these three is a bug.
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
4. **Commit checkpoints.** Commit frequently enough that no more than ~2 hours of work sits uncommitted. Message format: `<type>: <short imperative description>`, max 72 characters, English only (never mix languages in one message). Types: `feat`, `fix`, `refactor`, `chore`, `test`, `docs`, `style`. One logical unit of work per commit — if the message needs "and", split it into two commits.
5. **Branching.** `feature/<short-descriptive-name>` branches from `develop` and merges back into `develop`; `hotfix/<short-descriptive-name>` branches from `main` and merges into both `main` and `develop`. Never branch a feature off another feature branch without explicit coordination.
   - **Current state:** this repo only has `main` (single `init` commit). `develop` does not exist yet — create it from `main` before opening the first `feature/*` branch.
