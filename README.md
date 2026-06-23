# PDF Search API

A lightweight **.NET 10 + RavenDB** service that ingests PDFs **page-by-page** and exposes a
high-performance **full-text search** endpoint. Each page is stored as an independent document so
search hits can be targeted precisely to a source file and page number.

---

## Features

- **Granular ingestion** — PDFs are split per page; each non-empty page becomes its own searchable document.
- **Full-text search** — backed by RavenDB's `DocumentPages/Search` index using Lucene's `StandardAnalyzer` (stemming, case-insensitive matching).
- **Relevance scoring** — every hit carries its `@index-score` (Lucene engine), exposed as `SearchScore`.
- **Result completeness** — results return the matched content, source file name, and page number.
- **Consistent envelope** — every endpoint returns `ApiResponse<T>`, never a raw DTO.
- **Production-ready scaffolding** — Serilog (Console + Seq), health probes, Scalar/OpenAPI, global exception handling, Entra-ID JWT auth scaffold.

## Tech stack

| Concern         | Choice                                                        |
|-----------------|---------------------------------------------------------------|
| Runtime         | .NET 10 (`global.json` pins SDK `10.0.100`)                    |
| Persistence     | RavenDB (`RavenDB.Client`)                                     |
| PDF extraction  | `UglyToad.PdfPig` behind `IPdfTextExtractor`                   |
| API             | FastEndpoints + Swagger/OpenAPI, Scalar UI                    |
| Mediation       | MediatR 12.4.1 (Apache-2.0)                                    |
| Validation      | FluentValidation                                              |
| Results         | Ardalis.Result                                                |
| Logging         | Serilog → Console + Seq                                        |
| Testing         | xUnit, Shouldly, NSubstitute, RavenDB.TestDriver              |

## Architecture

Vertical-slice features over a layered solution:

```
src/
  Domain/          DocumentPage POCO (Domain/Documents/)
  Application/     Features (Ingestion, Search), MediatR handlers,
                   IApplicationDbContext seam, ApiResponse<T>, behaviors
  Infrastructure/  RavenDB store + RavenContext, indexes, PdfPig extractor,
                   RavenConventions, index-initializer hosted service
  Api/             FastEndpoints, Program.cs, middleware, DI wiring
tests/
  Api.IntegrationTests/   WebApplicationFactory<Program> + RavenDB.TestDriver
```

**Persistence seam.** Handlers depend only on `IApplicationDbContext` — never on `IDocumentStore`
or `IAsyncDocumentSession`. The seam exposes Raven's native `IRavenQueryable<T>` plus `StoreAsync`,
`SaveChangesAsync`, and an `IndexScore<T>` helper. The `IDocumentStore` is a singleton;
`IAsyncDocumentSession` and `RavenContext` are scoped (one session = one unit of work per request).
Store conventions live in one place: `RavenConventions.Apply(IDocumentStore)`.

## API

Routes follow the `/v{n}/...` pattern. Functional endpoints are `AllowAnonymous()` (no authorization
model yet — the Entra-ID JWT pipeline is scaffolded for later gating).

### Ingest a PDF

```
POST /v1/documents
Content-Type: multipart/form-data
```

Field `File` — the PDF to ingest. Text is extracted page-by-page; whitespace-only pages are skipped.

```bash
curl -F "File=@sample.pdf" http://localhost:5041/v1/documents
```

```jsonc
// 201 Created
{
  "success": true,
  "data": { "fileName": "sample.pdf", "pagesIngested": 12 }
}
```

### Full-text search

```
GET /v1/search?query={term}
```

Returns up to **25** matched pages ordered by relevance.

```bash
curl "http://localhost:5041/v1/search?query=invoice"
```

```jsonc
// 200 OK
{
  "success": true,
  "data": {
    "query": "invoice",
    "totalHits": 3,
    "results": [
      {
        "sourceFileName": "sample.pdf",
        "pageNumber": 4,
        "content": "…matched page text…",
        "searchScore": 1.87
      }
    ]
  }
}
```

A missing or empty `query` returns `400` with `validationErrors`.

### Health

- `GET /health/live` — liveness probe
- `GET /health/ready` — readiness probe

Both return `{ "status": "Healthy", "version": "1.0.0" }`.

## Getting started

### Run everything with Docker Compose

Brings up the API, RavenDB, and Seq:

```bash
docker compose up --build
```

| Service        | URL                                            |
|----------------|------------------------------------------------|
| API            | http://localhost:5000                          |
| RavenDB Studio | http://localhost:8080                          |
| Seq logs       | http://localhost:8081                          |

### Run the API locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/) and a reachable RavenDB instance
(e.g. `docker compose up ravendb`):

```bash
dotnet run --project src/Api
```

- API: http://localhost:5041
- OpenAPI + Scalar UI (Development only): http://localhost:5041/scalar/v1

The `DocumentPages/Search` index is created automatically on startup by a hosted service.

## Configuration

RavenDB config lives under the `RavenDb` section (no `ConnectionStrings`):

```json
{
  "RavenDb": {
    "Urls": [ "http://localhost:8080" ],
    "Database": "PdfSearch"
  }
}
```

Override via environment variables in containers, e.g. `RavenDb__Urls__0`, `RavenDb__Database`,
and `Serilog__WriteTo__1__Args__serverUrl` for the Seq sink.

## Testing

Integration tests run against a **real** RavenDB via `RavenDB.TestDriver` (test-mode server) — not
Testcontainers, not EF InMemory. `WaitForIndexing` is used to avoid stale-index flakiness.

```bash
dotnet test
```

## CI/CD

GitHub Actions (`.github/workflows/ci.yml`):

- **Build & Test** on every branch push and PR to `main`.
- **Build & Push Image** on `*.*.*` tags (multi-stage Docker build, version baked in via the `VERSION` arg).

Pushing a feature branch auto-opens a PR (`auto-pr.yml`).

## License

Internal project — no license specified.
