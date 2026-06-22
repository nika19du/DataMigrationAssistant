# Data Migration Assistant

A C# CLI-first developer tool for generating PostgreSQL seed and migration scripts from Excel/CSV files.

## Project Structure

```
DataMigrationAssistant.Core/      # All business logic — parsing, inference, diffing, validation, SQL gen
DataMigrationAssistant.Cli/       # CLI entry point — calls Core services only, no business logic here
```

A Blazor UI project will be added later; Core must remain UI-agnostic and reusable.

## Architecture Rules

- **All business logic lives in `DataMigrationAssistant.Core`.** The CLI project only wires up DI and delegates to Core services.
- **Separation of concerns** — keep these pipelines distinct:
  - Parsing (Excel/CSV → raw data)
  - Schema inference (raw data → inferred column types/nullability)
  - Diffing (compare inferred schema against existing)
  - Validation (data quality checks before generation)
  - SQL generation (validated data → PostgreSQL scripts)
- **Interfaces + DI everywhere.** Each pipeline stage has a clean interface so it can be swapped or tested in isolation.

## Libraries

- **ClosedXML** for Excel (.xlsx) parsing.
- Target **PostgreSQL** SQL dialect only — no generic/multi-DB abstractions needed.

## SQL Generation Conventions

- Use **snake_case** for all table and column names.
- **Never emit destructive SQL** (`DROP TABLE`, `TRUNCATE`, `DELETE`) without an explicit user confirmation prompt and a clearly visible warning in the output.
- Generated scripts should be idempotent where possible (e.g., `INSERT ... ON CONFLICT DO NOTHING` for seeds).

## MVP Scope

No AI integration in the first MVP. All schema inference and SQL generation is deterministic/rule-based.

## Testing Rule

After implementing a feature, run:

```bash
dotnet build
```

and fix compilation errors before continuing.

## CLI Rule

CLI commands must only:
- parse command arguments
- call Core services
- print results

No Excel parsing, SQL generation, or validation logic in CLI.

## Output Rule

Generated files should be returned/saved separately:
- preview.json
- migration.sql
- seed.sql
- warnings.md
- diff-report.md

## DI Registration Pattern

Core services should expose an `AddDataMigrationCore(this IServiceCollection services)` extension method so both the CLI and future Blazor UI can register everything with a single call.
