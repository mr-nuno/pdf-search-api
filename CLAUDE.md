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

## Connecting to a TLS RavenDB endpoint (required for https stores)
- Against a TLS endpoint (mkcert-fronted `ravendb.pew.local`, RavenDB Cloud) `SocketsHttpHandler`
  transparently retries a request on a fresh connection and re-serializes the body; RavenDB's
  single-use `BlittableJsonContent` then throws `"Already called previously, or called after
  EnsureCompletedAsync"`. `RavenConventions.Apply` wraps the client with
  `BufferedRequestContentHandler` (buffers materialized JSON into re-readable `ByteArrayContent`)
  via `store.Conventions.CreateHttpClient` to make the retry safe. **Do not remove this** when
  pointing the store at any https URL.
- A **secured** cluster authenticates with an X.509 client cert: `RavenDbOptions.CertificatePath`
  (+ `CertificatePassword`) is loaded in `DependencyInjection` and set on the store before
  `Initialize()`. Leave both blank for an unsecured store — including the TLS-fronted-but-open
  `ravendb.pew.local`, which needs **no** client cert (the mkcert root is trusted by the OS, so
  .NET validates the server cert with no extra config).

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
- Routes are unversioned per the global convention (e.g. `GET /search`, `POST /documents`) — no `/api/` and no `/v{n}/` prefix.
- Auth provider: **Logto** at `https://auth.pewi.se/oidc` (not Entra ID).
- JWT Bearer audience: `https://api.pdf-ingest.pewi.se`. Scopes: `api:read`, `api:write`.
- Policies are defined in `Api.Auth.AuthPolicy` (`Read`, `Write`). GET endpoints require `Read`; POST/PUT/DELETE require `Write`.
- In **Development**, a `DevBypassAuthHandler` auto-authenticates every request with both scopes — no real token needed locally.
- The `scope` claim may be space-separated (`"api:read api:write"`) or multi-valued; `HasScope` in `DependencyInjection` handles both.

## Config
- **No** `ConnectionStrings:DefaultConnection`. RavenDB config lives under a `RavenDb` section:
  `Urls` (string[]) + `Database`, plus optional `CertificatePath` / `CertificatePassword` for a
  secured (https) cluster. Serilog config is unchanged from global.
- Auth config lives under a `Logto` section: `Authority` + `Audience` (not needed in Development — bypass is unconditional).

## Testing
- Integration tests use **`RavenDB.TestDriver`** (a real test-mode Raven server) — **not**
  Testcontainers/Respawn (those are SQL-only) and never EF InMemory. Use `WaitForIndexing`
  to avoid stale-index flakiness. Otherwise global testing rules hold (Shouldly, NSubstitute,
  `WebApplicationFactory<Program>`, `ApiResponse<T>` deserialization helper).

## Not applicable here (EF/SQL-only global rules)
EF Core, `AppDbContext`/`IEntityTypeConfiguration`, migrations, StronglyTypedId, `AuditableEntity`,
`Enumeration` value conversions, Ardalis.Specification, Testcontainers/Respawn, SQL health checks.
