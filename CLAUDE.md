# Pneuma — Contributor & Agent Guide

Pneuma (Pneuma - information brought to life) is a subject knowledge-graph platform. This file captures the non-negotiable conventions for working in this repo. It mirrors the standards in `C:\code\agents\requirements`; where this file and those documents disagree, the requirement documents win.

## What Pneuma is

A C# (Watson 7.1) backend + Postgres control plane + LiteGraph knowledge graph, with a document→graph→search ingestion pipeline built from DocumentAtom, Partio, Verbex, and PolyPrompt, plus three React/Vite dashboards (admin, subject, user). See `PNEUMA_PLAN.md` for the phased plan and progress checkboxes, and `REST_API.md` for the API.

## Backend C# code style (STRICT)

- **No `var`.** Use the actual type.
- **No tuples.** Set response state / return typed objects instead.
- **No partial classes.** One class or one enum per file.
- `namespace` first; `using` directives **inside** the namespace block. System/Microsoft usings first (alphabetical), then others (alphabetical).
- XML documentation on every public class, constructor, property, method, and enum. No doc comments on private members.
- Public members `LikeThis`; private members `_LikeThis` (underscore + PascalCase, never `_likeThis`).
- Properties with range/null rules use explicit getters/setters over a backing field; throw `ArgumentNullException` for required nulls; `Math.Clamp` numeric ranges.
- Every async method that does cancellable work takes a `CancellationToken`. Every `await` in library/server code uses `.ConfigureAwait(false)`.
- Classic `using (...) { }` blocks, not `using` declarations. Full dispose pattern where resources are held.
- Specific exception types with meaningful messages; document thrown exceptions with `/// <exception>`.
- **No `Console.WriteLine` in library code** — use SyslogLogging.
- No `System.Text.Json` DOM types (`JsonElement`, `JsonNode`, …) for fixed contracts — define typed DTOs. JSON columns only for genuinely schemaless properties, named `*Json`.
- `Nullable` enabled; guard-clause inputs; prefer `.Any()`, `.FirstOrDefault()` with null checks.
- Region order when used: `Public-Members`, `Private-Members`, `Constructors-and-Factories`, `Public-Methods`, `Private-Methods` (optional under 500 lines).

## Backend architecture

- Thin `Program.cs` → `Bootstrapper` → `PneumaServer` (Watson host). Feature route registrars call `server.Routes.{Pre,Post}Authentication.{Static,Parameter}.Add(...)`. Mandatory `Preflight` (CORS OPTIONS) and `PostRouting` (timestamp, CORS, request capture) hooks. `Server.UseOpenApi()`.
- Auth via the `AuthenticateRequest` hook building a typed `RequestContext` stashed in `ctx.Metadata`.
- `PrettyId` prefixed IDs via `Helpers/IdGenerator.cs`; prefixes centralized in `Constants.cs`.
- Provider-neutral data layer: `DatabaseDriverBase` + `DatabaseDriverFactory` + domain `I*Methods` interfaces; provider folders `Sqlite`, `Mysql`, `Postgresql`, `SqlServer` (exact casing), each with `Implementations/` and `Queries/`. Handwritten SQL. Structured columns/child tables — never BLOB-as-JSON for known shapes.
- Multi-tenant: every tenant-owned entity carries `TenantId`; queries always scope by tenant; composite unique indexes `(tenant_id, …)`.
- Versioned, idempotent, tracked migrations (`schema_migrations`). First-boot seeding is idempotent.
- SQLite writes serialized with `SemaphoreSlim`.
- Request capture enabled by default (`/v1.0/api/request-history`), secrets redacted, bodies truncated, tenant-aware, retention-pruned.
- Loopback is `127.0.0.1`, never `localhost`.

## Auth model

Full RBAC per `AUTHENTICATION.md`: accounts, tenants, admins, users, credentials, authsessions, userroles, userroleassignments, userrolemaps (legacy), credentialscopeassignments, permissions, rolepermissionmaps, audit. Dashboards log in with email/password → AES-256 opaque session token (random IV) presented as `Authorization: Bearer`. API-key credentials (`access_`/`secret_`) also present as bearer. `IsAdmin` (= Pneuma.md IsSystemAdmin) and `IsTenantAdmin` bypass RBAC per the documented order; all denials + bypasses are audited. Explicit deny > permit > implicit deny.

## Frontend (three dashboards)

React 19 + Vite 6 + React Router 7, hand-rolled fetch `ApiClient` (no axios), i18next runtime, hand-rolled SVG `ActivityChart` (no charting lib). Required operator surfaces where the role warrants: Home, Request History (+ inspector modal), OpenAPI-driven API Explorer, Settings. Custom confirm modals (never browser `confirm`). Light/dark themes; responsive at 1280/768/390. External links from the user dashboard open in a new tab. See `FRONTEND_ARCHITECTURE.md` and `DASHBOARD_STYLE_AND_USABILITY.md`.

## Integration contracts (confirmed from source)

- **DocumentAtom** `:8000`, no auth, no version prefix. `POST /typedetect` (raw bytes) → `TypeResult`; `Type:"Unknown"` returns 200 — fail ingestion on it. `POST /atom/{type}` with `{ "Settings": null, "Data": "<base64>" }` → `Atom[]`.
- **Partio** `:8400`, Bearer (`partioadmin`). `POST /v1.0/process` with `SemanticCellRequest`; embedding/completion endpoints referenced by ID (`eep_`/`cep_`), configured server-side.
- **Verbex** `:8600`→8080, Bearer (`verbexadmin`/`default`). `POST /v1.0/indices`, `.../documents`, `.../search`. Store LiteGraph node ID in `Tags.litegraphNodeId` + `CustomMetadata`; it round-trips on search hits.
- **LiteGraph** `:8701`. Knowledge graph store; node/edge labels + tags carry metadata, provenance, rights, authority.

## Testing

Touchstone descriptors in `Test.Shared` (no console output), run via `Test.Automated` (CLI), `Test.Xunit`, `Test.Nunit`. Shared DB contract suites run against all four providers. Bind test servers to `127.0.0.1`.

## Build

- `dotnet build src/Pneuma.sln`
- `dotnet run --project src/Test.Automated`
- Dashboards: `npm ci && npm run build` in each dashboard dir.
- Do not commit or push unless explicitly asked.
