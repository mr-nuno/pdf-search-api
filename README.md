# PDF Search API

A lightweight **.NET 10 + RavenDB** service that ingests PDFs **page-by-page** and exposes a
high-performance **full-text search** endpoint. Each page is stored as an independent document so
search hits can be targeted precisely to a source file and page number.

---

## Features

- **Granular ingestion** — PDFs are split per page; each non-empty page becomes its own searchable document.
- **Layout-aware extraction** — text is reconstructed from word positions (not the raw glyph stream), so running headers and printed page numbers are split into their own fields and the body is rendered as **markdown** (line/paragraph breaks preserved, enlarged lines promoted to headings) for clean display by consumers.
- **Full-text search** — backed by RavenDB's `DocumentPages/Search` index using Lucene's `StandardAnalyzer` (stemming, case-insensitive matching). Both body `Content` and the running `Header` are indexed, so chapter-title queries still match.
- **Relevance scoring** — every hit carries its `@index-score` (Lucene engine), exposed as `SearchScore`.
- **Result completeness** — results return the markdown body content, running header, page label, source file name, and page number.
- **Multiple tags** — documents can be tagged with multiple labels (stored lowercase). Search and source listing both reflect all tags.
- **Source management** — list all ingested source files, replace a document in-place (PUT), or delete a source and all its pages.
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

Functional endpoints are `AllowAnonymous()` (no authorization model yet — the Entra-ID JWT pipeline
is scaffolded for later gating).

### Ingest a PDF

```
POST /documents
Content-Type: multipart/form-data
```

Field `File` — the PDF to ingest. Text is extracted page-by-page using layout analysis: line and
paragraph breaks are preserved, the running header and printed page number are separated from the
body, and the body is stored as markdown. Whitespace-only pages are skipped.
Field `tags` (optional, repeatable) — one or more tags to categorize the document (stored lowercase). Defaults to `"none"`.

```bash
curl -F "File=@sample.pdf" -F "tags=finance" -F "tags=quarterly" http://localhost:5041/documents
```

```jsonc
// 201 Created
{
  "success": true,
  "data": { "fileName": "sample.pdf", "pagesIngested": 12 }
}
```

### Replace an existing PDF (re-ingest)

```
PUT /documents
Content-Type: multipart/form-data
```

Same fields as `POST /documents`. Deletes all previously stored pages for that file name, then
ingests the new version. Use this to update a document that has already been ingested.

```bash
curl -X PUT -F "File=@sample.pdf" -F "tags=finance" http://localhost:5041/documents
```

```jsonc
// 200 OK
{
  "success": true,
  "data": { "fileName": "sample.pdf", "pagesIngested": 10 }
}
```

### List ingested source files

```
GET /sources
```

Returns all ingested PDF files grouped by file name, with the number of stored pages and all
associated tags.

```bash
curl http://localhost:5041/sources
```

```jsonc
// 200 OK
{
  "success": true,
  "data": {
    "sources": [
      { "sourceFileName": "manual.pdf", "pageCount": 42, "tags": ["docs"] },
      { "sourceFileName": "report.pdf", "pageCount": 12, "tags": ["finance", "quarterly"] }
    ]
  }
}
```

### Delete an ingested source file

```
DELETE /sources?sourceFileName={name}
```

Deletes all stored pages for the given source file name. Returns the count of deleted pages and any
tags that are no longer present on any remaining document.

```bash
curl -X DELETE "http://localhost:5041/sources?sourceFileName=sample.pdf"
```

```jsonc
// 200 OK
{
  "success": true,
  "data": {
    "sourceFileName": "sample.pdf",
    "pagesDeleted": 12,
    "tagsRemoved": ["quarterly"]
  }
}
```

Returns `404` if no documents are found for the given file name.

### Full-text search

```
GET /search?query={term}&tags={optional_tag}
```

Returns up to **25** matched pages ordered by relevance. The `tags` parameter filters results to
pages carrying at least one of the specified tags (OR semantics). Repeat the parameter for multiple
tags. If omitted, all tags are searched.

```bash
curl "http://localhost:5041/search?query=invoice&tags=finance&tags=quarterly"
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
        "header": "KAPITEL 8 – ÄVENTYR",
        "content": "## Fällor\n\n…matched body text…",
        "tags": ["finance", "quarterly"],
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

| Service              | URL                                            |
|----------------------|------------------------------------------------|
| API                  | http://localhost:5000                          |
| OpenAPI / Scalar UI  | http://localhost:5000/scalar/v1                |
| RavenDB Studio       | http://localhost:8080                          |
| Seq logs             | http://localhost:8081                          |

### Run the API locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/) and a reachable RavenDB instance
(e.g. `docker compose up ravendb`):

```bash
dotnet run --project src/Api
```

| Service              | URL                                            |
|----------------------|------------------------------------------------|
| API                  | http://localhost:5041                          |
| OpenAPI / Scalar UI  | http://localhost:5041/scalar/v1                |

The `DocumentPages/Search` index is created automatically on startup by a hosted service.

### OpenAPI specification

The interactive Scalar UI and the raw OpenAPI specification are served in Development only.

| Resource              | URL                                              |
|-----------------------|--------------------------------------------------|
| Scalar UI             | http://localhost:5041/scalar/v1                  |
| OpenAPI specification | http://localhost:5041/openapi/v1.json            |

Use the Scalar UI to browse all endpoints, inspect request/response schemas, and try calls directly
in the browser. The specification URL is what Scalar fetches behind the scenes — point any
OpenAPI-compatible tool (Postman, Insomnia, code generators) at it directly.

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
