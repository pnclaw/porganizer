# CLAUDE

## Project Structure & Module Organization

- `src/porganizer.Api` is organized by feature, not by technical layer.
- Use `src/porganizer.Api/Features/<FeatureName>/<UseCase>/` for API slices.
- Keep each slice self-contained: controller, request/response models, validators, mapping, and orchestration logic should live together.
- Shared cross-feature code belongs in `src/porganizer.Api/Common` only when reuse is real.

## Architecture Overview

- Follow feature-based vertical slice architecture.
- The primary unit of change is a use case, not a layer.
- API endpoints are implemented with Controllers, not minimal APIs.
- Each slice should own its HTTP contract, use-case logic, validation, mapping, and required dependencies.
- Avoid generic service or repository layers unless they provide clear cross-feature value.
- Feature slices should not depend on other feature slices directly.
- Reuse cross-cutting code through `Common` or infrastructure abstractions.
- Keep database and infrastructure details out of controllers; controllers should delegate quickly to slice logic.

## Tech Stack

- .NET 10
- SQLite
- Entity Framework 10 with a code-first approach
- Serilog
- `Microsoft.AspNetCore.OpenApi`

## Coding Standards

- Follow `.editorconfig`: 4-space indent, `System.*` first, prefer `var`, PascalCase types and methods, camelCase locals.
- Add missing `using` directives instead of fully qualified type names.
- Use modern C# syntax.
- Prefer primary constructors for dependency-injected classes unless a standard constructor is clearly required.
- Prefer modern collection expressions (`[]`).
- Use Controllers instead of minimal APIs.

## OpenAPI Metadata Standards

- Use the built-in OpenAPI pipeline only: `AddOpenApi` and `MapOpenApi`.
- Do not add Swashbuckle unless explicitly requested.
- Every controller action must declare explicit response metadata for success and relevant failure cases.
- Declare content types with `[Produces("application/json")]` and `[Consumes("application/json")]` where appropriate.
- Every controller action must include both `[EndpointSummary("...")]` and `[EndpointDescription("...")]`.
- Use explicit, slice-local request and response DTOs rather than exposing EF entities directly.
- Keep OpenAPI output deterministic and avoid anonymous response objects.

## Dependency Injection & Services

- Keep controller actions thin and delegate business logic to a slice-local service.
- Define slice-local service interfaces and implementations together, using `I<UseCase>Service` and `<UseCase>Service`.
- Register services explicitly in `src/porganizer.Api/Program.cs`; do not rely on assembly scanning.
- Prefer `AddScoped` by default unless another lifetime is clearly justified.
- Depend on abstractions in constructors rather than concrete implementations.
- Keep single-slice services in that slice; move code to `Common` only after real reuse appears.

## Options Pattern

- Use the Microsoft Options pattern for configurable behavior instead of hardcoded values.
- Place feature-specific options classes in the owning slice folder.
- Each options class should define a `SectionName` constant and strongly typed properties.
- Register options in `src/porganizer.Api/Program.cs` with `AddOptions<TOptions>()`, `.BindConfiguration(TOptions.SectionName)`, validation, and `.ValidateOnStart()`.
- Inject options into services via `IOptions<TOptions>` or `IOptionsSnapshot<TOptions>` when needed.
- Controllers should not read configuration directly.

## Entity Base Class Convention

- All entity classes must inherit from `porganizer.Database.Common.BaseEntity`.
- `BaseEntity` provides `CreatedAt`, `UpdatedAt`, `CreatedBy`, and `UpdatedBy`.

## Enum Storage Convention

- Always store enums as integers in the database.
- Enum member integer values must be explicitly declared and must never be reordered.
- If a C# enum member name must start with a digit, use a leading underscore in code.

## Migration Execution

- When an EF migration is needed, the agent may run it without user confirmation.

## Testing

- Primary strategy: integration tests using `WebApplicationFactory<Program>`.
- Tests live in `tests/porganizer.Api.Tests/`.
- Each test class should use its own `PorganizerApiFactory` instance for database isolation.
- Adding or modifying an API endpoint is not complete until integration tests are written.
- Every endpoint needs at minimum a happy-path test and a sad-path test.
- Use `HttpClient.PostAsJsonAsync` and `ReadFromJsonAsync` for request and response serialization.
- Use unit tests only when genuine business logic exists that is worth testing in isolation.

## Solution Files

- Use only the modern solution format.
- Do not create or commit legacy `*.sln` files.

## Build Execution

- Prefer non-parallel builds to avoid intermittent MSBuild or restore failures with poor diagnostics.
- Default build command for AI agents:
  - `dotnet build porganizer.slnx -m:1 -p:BuildInParallel=false -v minimal`

## Git Workflow

This project follows Git Flow.

- `main` is for stable releases only.
- `develop` is the integration branch and the target for feature PRs.
- `feature/*` branches are for new features and branch from `develop`.
- `bugfix/*` branches are for fixes and branch from `develop`.
- `release/*` and `hotfix/*` branches are owner-only.
- Never push directly to `main` or `develop`.
- Always branch from `develop` for features and bugfixes.
- Always open a PR against `develop`, except hotfixes which target `main`.
- Delete branches after merge.
- Use conventional commit prefixes such as `feat:`, `fix:`, `docs:`, `chore:`, and `refactor:`.

Branch naming details:

- Feature branches should be named `feature/<slug>`.
- If the user provides a topic, slugify it by lowercasing, replacing spaces or punctuation with hyphens, collapsing repeated hyphens, and trimming leading or trailing hyphens.
- If the user does not provide a topic, generate a random slug of exactly three common English words joined by hyphens.
- Do not ask the user to confirm a derived branch name unless they explicitly ask to review it first.
- Before starting or finishing branch workflow commands, stop if the working tree is dirty or the current branch has unpushed commits when that would make the workflow unsafe.
- New feature branches should be created from `origin/develop`.
- Finish-feature workflow should verify the branch is not `develop` or `main`, ensure `CHANGELOG.md` has a matching entry, push the branch, and open a PR targeting `develop`.
- Cleanup workflow should verify the branch is not `develop` or `main`, confirm the branch was merged to `develop`, switch back to `develop`, update it, and then delete the local and remote feature branch.

## Changelog

- `CHANGELOG.md` is the active log at the repo root and has a hard cap of 300 lines.
- `docs/changelog/<year>.md` is the yearly archive.
- Every `feature/*` or `bugfix/*` PR must include a changelog entry in `CHANGELOG.md`.
- Prepend new changelog entries so the newest entry appears first.
- `### Dead Ends` is mandatory, even when the value is `*(none)*`.
- If `CHANGELOG.md` exceeds 300 lines, move the oldest complete entry blocks into the appropriate yearly archive until the file is back under the limit.

Entry format:

```md
## feature/branch-name — YYYY-MM-DD

### Done
- Concise bullet per meaningful change

### Dead Ends
- Describe each approach that was tried and abandoned, and why.
- If nothing failed, write: *(none)*
```

## PRDB Sync Rules

### Current-State Delta Feed Convention

When implementing or changing sync features against the prdb.net API, prefer endpoints that follow the Current-State Delta Feed Convention.

This convention now explicitly applies to sync work for:

- wanted videos
- favorite sites
- favorite actors
- any other PRDB resource that exposes a compatible `/changes` endpoint

From this project's perspective, that means the endpoint should provide:

- Current-state change events, not append-only history.
- A stable incremental cursor, ideally ordered by `(updatedAtUtc, id)`.
- Created and updated rows in the same feed.
- Soft-deleted rows in the same feed, typically via `isDeleted` and `deletedAtUtc`.
- A resumable cursor returned by the API, not page-number-based polling for incremental sync.

This is the preferred contract because it lets `porganizer`:

- Run an initial backfill once, then stay in sync incrementally.
- Apply upserts and deletes safely and idempotently.
- Avoid expensive full reconciliation sweeps just to detect removals.
- Avoid page drift problems from page-number-based incremental polling.

### Rule For New PRDB Sync Features

When adding a new prdb.net sync feature, explicitly ask for an endpoint with these capabilities if one does not already exist.

When the API already exposes a compatible `/changes` endpoint for a resource, prefer that endpoint over list endpoints or created-at polling. This applies to wanted videos, favorite sites, and favorite actors.

At minimum, ask the prdb.net API team for:

- Incremental sync by update cursor, not only by create time.
- A stable seek cursor with a deterministic tie-breaker such as `(updatedAtUtc, id)`.
- Delete visibility through soft-delete fields or equivalent tombstones.
- A single change feed that covers create, update, and delete events.

Do not design new long-term sync logic around latest-created rows alone unless there is no better API available and the limitation is documented in the implementation.
