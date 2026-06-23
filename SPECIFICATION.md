# Specification: PDF Content Ingestion & Full-Text Search API

## 1. System Overview

A lightweight system designed to parse multi-page PDFs, store their text contents with page-level granularity in a RavenDB instance, and expose a high-performance full-text search endpoint.

### Core Requirements

* **Granular Ingestion:** PDFs must be processed page-by-page. Each page is stored as an independent document to allow precise hit targeting.
* **Text Isolation:** Only extract and store pure textual data.
* **Native Search:** Leverage RavenDB's built-in indexing capability for full-text querying.
* **Result Completeness:** Search results must provide the matched content, the source filename, and the specific page number.

---

## 2. Data Models

### Database Document Model

```csharp
public class DocumentPage
{
    public string Id { get; set; } // e.g., "DocumentPages/1-A" or auto-generated
    public string SourceFileName { get; set; }
    public int PageNumber { get; set; }
    public string Content { get; set; }
    public DateTime IngestedAt { get; set; }
}

```

### API Response Model

```csharp
public record SearchResultDto(
    string SourceFileName,
    int PageNumber,
    string Content,
    double SearchScore
);

public record SearchResponseDto(
    string Query,
    int TotalHits,
    List<SearchResultDto> Results
);

```

---

## 3. Component Specifications

### A. Ingestion Service (`PdfIngestionService.cs`)

Responsible for reading PDF files, extracting text content per page, and saving them to the database.

* **Dependencies:** `PdfPig` (or `UglyToad.PdfPig`) NuGet package for robust, cross-platform PDF text extraction.
* **Method Signature:** `Task IngestPdfAsync(Stream pdfStream, string fileName)`
* **Logic Steps:**
1. Open the PDF document using `PdfDocument.Open(pdfStream)`.
2. Loop through every page (`page.Number`).
3. Extract text via `page.GetText()`.
4. Trim and skip pages containing no text or only whitespace.
5. Construct a `DocumentPage` object for each valid page.
6. Bulk-insert or save via a standard RavenDB session using `session.Store()`.



### B. Database Index (`DocumentPages_Search.cs`)

A RavenDB strongly-typed index definition to enable full-text querying on the text content.

* **Implementation:** Inherit from `AbstractIndexCreationTask<DocumentPage>`.
* **Map Definition:** ```csharp
Map = pages => from page in pages
select new
{
page.Content,
page.SourceFileName
};

```
* **Field Configuration:** Set the `Content` field to be analyzed using Lucene's standard analyzer to ensure accurate keyword matching, stemming, and casing behavior.
```csharp
    Analyze(x => x.Content, "StandardAnalyzer");
    Indexing(x => x.Content, FieldIndexing.Search);

```

### C. Search Endpoint (`SearchEndpoints.cs`)

Exposed via Minimal APIs to provide a high-speed search interface.

* **Route:** `GET /api/search`
* **Query Parameters:** * `query` (string, required)
* **Logic Steps:**
1. Open a RavenDB async session.
2. Query the `DocumentPages_Search` index.
3. Use the `.Search()` extension method to search against the `Content` property using the passed query string.
4. Retrieve metadata scores if required via `session.Advanced.GetMetadataFor()`.
5. Map matched documents directly to `SearchResponseDto`.



---

## 4. Expected Implementation Steps for the Agent

1. **Package Setup:** Ensure `RavenDB.Client` and `UglyToad.PdfPig` are installed.
2. **Infrastructure Hook:** Register `IDocumentStore` in `Program.cs` as a singleton. Create a helper method to initialize indexes (`IndexCreation.CreateIndexes()`) during application startup.
3. **Core Services:** Generate the `DocumentPage` model, the index creation class, and the `PdfIngestionService`.
4. **Endpoints:** Generate the minimal API mapping for `/api/search` and optionally a `/api/ingest` endpoint that accepts a `IFormFile` to easily load test PDFs.