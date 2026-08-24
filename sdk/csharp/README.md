# Pneuma C# SDK

A hand-rolled C# client for the [Pneuma REST API](../../REST_API.md). It wraps `HttpClient` with typed
request/response models and one async method per documented endpoint. Non-2xx responses are surfaced as
a typed `PneumaException` carrying the HTTP status code and response body.

- Targets `net8.0` and `net10.0`
- Nullable reference types enabled
- JSON is camelCase; enums are serialized as strings
- Loopback base URLs are normalized to `127.0.0.1`

## Install

Add a project reference to `Pneuma.Sdk`:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/sdk/csharp/src/Pneuma.Sdk/Pneuma.Sdk.csproj" />
</ItemGroup>
```

Or build the solution directly:

```bash
dotnet build sdk/csharp/src/Pneuma.Sdk.sln
```

## Construction

```csharp
using Pneuma.Sdk;

// Base URL only; localhost is rewritten to 127.0.0.1 automatically.
PneumaClient client = new PneumaClient("http://127.0.0.1:8080");

// Or supply a token you already have:
PneumaClient authed = new PneumaClient("http://127.0.0.1:8080", "your-bearer-token");
```

The constructor is `PneumaClient(string baseUrl, string? token = null)`. An overload accepting a
caller-supplied `HttpClient` is also available for connection pooling scenarios.

## Authentication

Call `LoginAsync` to exchange email/password for a session token. The returned token is stored on the
client and sent as `Authorization: Bearer <token>` on every subsequent request.

```csharp
using Pneuma.Sdk.Responses;

TokenResponse login = await client.LoginAsync("admin@pneuma", "password");
// client.Token is now populated. Optionally pass a tenantId:
// await client.LoginAsync(email, password, tenantId: "ten_...");

// Validate or revoke the session:
TokenResponse principal = await client.ValidateTokenAsync();
await client.LogoutAsync(); // clears client.Token
```

You may also assign `client.Token` directly, or clear it by setting it to `null`.

## Usage example

```csharp
using System;
using System.Collections.Generic;
using Pneuma.Sdk;
using Pneuma.Sdk.Enums;
using Pneuma.Sdk.Models;
using Pneuma.Sdk.Requests;
using Pneuma.Sdk.Responses;

using PneumaClient client = new PneumaClient("http://127.0.0.1:8080");

// Health does not require auth.
HealthResponse health = await client.GetHealthAsync();
Console.WriteLine($"Pneuma {health.Version} is {health.Status}");

// Authenticate.
await client.LoginAsync("admin@pneuma", "password");

// Create a subject archive.
Subject subject = await client.CreateSubjectAsync(new Subject
{
    DisplayName = "Nina Simone",
    Type = SubjectTypeEnum.Person,
    Description = "High priestess of soul."
});

// Pick ingestion endpoints (embedding + completion) to process the link.
IngestionEndpointsResponse endpoints = await client.ListIngestionEndpointsAsync();
string embeddingEndpointId = endpoints.Embedding[0].Id;
string completionEndpointId = endpoints.Completion[0].Id;

// Submit a content link; this enqueues an ingestion job.
SubjectLink link = await client.SubmitLinkAsync(subject.Id, new SubmitLinkRequest
{
    Url = "https://example.com/article",
    Title = "Overview article",
    EmbeddingEndpointId = embeddingEndpointId,
    CompletionEndpointId = completionEndpointId
});

// Or submit several URLs at once.
BulkSubmitLinkResponse bulk = await client.SubmitLinksAsync(subject.Id, new BulkSubmitLinkRequest
{
    Urls = new List<string> { "https://example.com/a", "https://example.com/b" },
    EmbeddingEndpointId = embeddingEndpointId,
    CompletionEndpointId = completionEndpointId
});
Console.WriteLine($"created {bulk.Created} links");

// Watch ingestion jobs. List calls return a paginated EnumerationResult<T>; records are in .Objects.
EnumerationResult<IngestionJob> jobs = await client.ListJobsAsync();
foreach (IngestionJob job in jobs.Objects)
    Console.WriteLine($"{job.Id}: {job.Status} @ {job.Stage}");

// Search and ask grounded questions.
SearchResponse hits = await client.SearchAsync("protest songs", max: 10);
QueryResponse answer = await client.QueryAsync("What themes recur across the catalog?");
Console.WriteLine(answer.Answer);
```

## Labels, tags & metadata filters

When submitting a link you can attach operator-supplied `Labels` (a list of plain strings) and
`Tags` (a key/value map) via `SubmitLinkRequest` (or `BulkSubmitLinkRequest`, where they apply to
every URL in the batch). Both are optional. They ride along with every chunk the link produces, and
they round-trip on the returned `SubjectLink`, so retrieval can later be scoped to them.

```csharp
using System.Collections.Generic;
using Pneuma.Sdk.Enums;
using Pneuma.Sdk.Models;
using Pneuma.Sdk.Requests;

// Submit a link with labels and tags.
SubjectLink tagged = await client.SubmitLinkAsync(subject.Id, new SubmitLinkRequest
{
    Url = "https://example.com/1965-live-at-newport",
    Title = "Live at Newport (1965)",
    Labels = new List<string> { "live", "1965" },
    Tags = new Dictionary<string, string>
    {
        ["source"] = "official",
        ["rights"] = "cleared"
    }
});
```

On a grounded query you can pass a `SubjectId` to scope retrieval to one subject, and a
`MetadataFilter` (a `RetrievalFilter`) to restrict retrieval to documents ingested with matching
labels/tags. A `RetrievalFilter` has four optional parts: `RequiredLabels`, `ExcludedLabels`,
`RequiredTags`, and `ExcludedTags`. Each tag entry is a `RetrievalTagCondition` — a `Key`, a
`Condition` (`TagConditionEnum`: `Equals` (default), `NotEquals`, `Contains`, `StartsWith`,
`EndsWith`, `GreaterThan`, `LessThan`, `IsNull`, `IsNotNull`), and a `Value` (ignored for
`IsNull`/`IsNotNull`). A chunk is eligible only when it carries every required label and satisfies
every required tag condition, and none of the excluded labels or tag conditions match. The filter is
merged with the subject's default filter (union of required and excluded) — it narrows, never widens.

```csharp
QueryResponse scoped = await client.QueryAsync(new QueryRequest
{
    Question = "What themes recur across the live recordings?",
    MaxResults = 10,
    SubjectId = subject.Id,
    MetadataFilter = new RetrievalFilter
    {
        RequiredLabels = new List<string> { "live" },
        ExcludedLabels = new List<string> { "bootleg" },
        RequiredTags = new List<RetrievalTagCondition>
        {
            new RetrievalTagCondition { Key = "rights", Condition = TagConditionEnum.Equals, Value = "cleared" }
        }
    }
});
Console.WriteLine(scoped.Answer);
```

## Pagination

Every list (GET-all) call returns a paginated `EnumerationResult<T>` envelope rather than a bare list.
The records are in `.Objects`; `.TotalRecords`, `.RecordsRemaining`, and `.EndOfResults` describe the
full result set. Pass an optional `EnumerationQuery` to page and filter:

```csharp
using Pneuma.Sdk.Requests;
using Pneuma.Sdk.Responses;

EnumerationQuery query = new EnumerationQuery
{
    MaxResults = 50,   // clamped server-side to 1..1000 (default 100)
    Skip = 0,
    Order = "desc",    // "asc" or "desc" (default "desc")
    Search = "nina"    // optional substring filter
};

EnumerationResult<Subject> page = await client.ListSubjectsAsync(query);
Console.WriteLine($"{page.Objects.Count} of {page.TotalRecords}, endOfResults={page.EndOfResults}");
```

## Settings (system admin)

Read and update the server settings object. Secret fields are masked with `********` on read; the
`Meta` describes sections and which secret fields exist.

```csharp
SettingsEnvelope current = await client.GetSettingsAsync();
// current.Settings is a JsonElement? with secrets masked; current.Meta describes the sections.

SettingsEnvelope result = await client.UpdateSettingsAsync(updatedSettingsObject);
if (result.RestartRequired) Console.WriteLine("A restart is required: " + result.Message);
```

## Error handling

Any non-2xx response throws `PneumaException`:

```csharp
try
{
    await client.GetSubjectAsync("sub_does_not_exist");
}
catch (PneumaException ex)
{
    Console.WriteLine($"HTTP {ex.StatusCode}: {ex.Error?.Message ?? ex.ResponseBody}");
}
```

## Surface coverage

The client exposes methods for: health; token validate/details/logout; tenants CRUD; users CRUD;
credentials create/list/get/delete; roles, permissions, assignments, and audit; subjects CRUD;
content links (submit/list/get/delete) and ingestion jobs (list/detail/restart); model runners CRUD;
prompts CRUD; request history (list/summary/get/delete/bulk-delete); knowledge-graph
node/neighbors/edges; full-text search; grounded query; and server settings (get/update).

## Test harness

`src/Test.Automated` is a console app that runs a smoke test against a live server at
`http://127.0.0.1:8080` (logging in as `admin@pneuma` / `password`). It prints PASS/FAIL per step
and exits non-zero on failure. When the server is unreachable it prints SKIPPED for each step and exits
zero, so it is safe to run in environments without a server.

```bash
dotnet run --project sdk/csharp/src/Test.Automated
```
