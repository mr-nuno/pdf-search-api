# Project Conventions — PDF Search API

This is a **.NET 10 + RavenDB** service that ingests PDFs page-by-page and exposes a
full-text search endpoint. It follows the global `~/.claude/dotnet-conventions.md`
**except** where this file overrides them. RavenDB replaces SQL Server / EF Core, so all
EF/SQL-specific conventions are superseded by the rules below.

## Stack deltas vs. global conventions
- **Persistence: RavenDB** (`RavenDB.Client`) instead of EF Core + SQL Server.
- **PDF extraction**: `UglyToad.PdfPig` behind `IPdfTextExtractor`.
- Kept from global: FastEndpoints (+ Swagger), MediatR **12.4.1** (Apache-2.0, never upgrade),
  FluentValidation, Ardalis.Result, Serilog (Console + Seq), `ApiResponse<T>` envelope,
  xUnit + Shouldly + NSubstitute, vertical-slice + layered structure, Scalar, health checks.

## Persistence seam (the one layer-owned abstraction)
- Handlers depend on `IApplicationDbContext` (in `Application/Common/Interfaces/`), **never** on
  `IDocumentStore` or `IAsyncDocumentSession`. No repositories, no raw client — exactly one seam.
- The seam exposes Raven's native `IRavenQueryable<T>` (the way the EF seam exposed `DbSet<T>`),
  plus `StoreAsync`, `SaveChangesAsync` (`Task`, not `int`), and an `IndexScore<T>` helper that
  hides `session.Advanced.GetMetadataFor(...)`.
- `IDocumentStore` is a **singleton** (Infrastructure-only: store + index creation).
  `IAsyncDocumentSession` and `RavenContext : IApplicationDbContext` are **scoped**
  (one session = one unit of work per request).
- **Shared store conventions**: `RavenConventions.Apply(IDocumentStore)` is the single place that
  shapes stored data (id/identity separators, collection naming). Production DI **and** the
  integration-test store call it — never re-derive naming/id rules in tests.

## Documents & indexes (replaces EF entities/migrations/configs)
- Stored documents (e.g. `DocumentPage`) live in `Domain/Documents/` as **plain POCOs with public
  setters** (Raven deserialization; read-mostly stored data). **No** `AuditableEntity`,
  **no** `[StronglyTypedId]`, **no** `IAggregateRoot` — those are EF/SQL-only.
- Ids are Raven **strings** (`DocumentPages/...`), assigned by the store — not Guid/int STIDs.
- Full-text indexes are `AbstractIndexCreationTask<T>` in `Infrastructure/Persistence/Indexes/`,
  registered via `IndexCreation.CreateIndexes` in a startup `IHostedService`.
  **No EF migrations, no `IEntityTypeConfiguration<T>`, no `dotnet ef`.**
- Timestamps (e.g. `IngestedAt`) are set explicitly in the handler via `IDateTimeProvider`
  (Raven has no auto-audit) — the global "never set audit fields manually" rule does not apply.

## Routes & auth
- Routes use the global `/v{n}/...` pattern (e.g. `GET /v1/search`, `POST /v1/documents`) — no `/api/`.
- Entra-ID JWT auth is scaffolded per global conventions, but functional endpoints are
  `AllowAnonymous()` (the system has no authorization model). Add `Policies(...)` to gate later.

## Config
- **No** `ConnectionStrings:DefaultConnection`. RavenDB config lives under a `RavenDb` section:
  `Urls` (string[]) + `Database`. Serilog config is unchanged from global.

## Testing
- Integration tests use **`RavenDB.TestDriver`** (a real test-mode Raven server) — **not**
  Testcontainers/Respawn (those are SQL-only) and never EF InMemory. Use `WaitForIndexing`
  to avoid stale-index flakiness. Otherwise global testing rules hold (Shouldly, NSubstitute,
  `WebApplicationFactory<Program>`, `ApiResponse<T>` deserialization helper).

## Not applicable here (EF/SQL-only global rules)
EF Core, `AppDbContext`/`IEntityTypeConfiguration`, migrations, StronglyTypedId, `AuditableEntity`,
`Enumeration` value conversions, Ardalis.Specification, Testcontainers/Respawn, SQL health checks.
