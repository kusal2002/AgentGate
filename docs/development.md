# Development

Follow the root README to start PostgreSQL, the API, and the dashboard. No Slack or LLM credentials are necessary.

## Build and lint

```powershell
dotnet build backend/AgentGate.sln
npm --prefix dashboard ci
npm --prefix dashboard run build
npm --prefix dashboard run lint
./scripts/test-backend.ps1
```

Commit the npm lockfile. Use `npm ci` for reproducible installs. The shadcn configuration is `dashboard/components.json`; component source lives in `dashboard/src/components/ui`.

To add another official shadcn component, run `npx shadcn@4.21.3 add <component>` from `dashboard`. The scaffold CLI is not a runtime dependency. This registry version may generate imports from `cn`; use the existing `@/lib/utils` helper instead.

## EF Core migrations (when entities are added)

The pinned local `dotnet-ef` tool is recorded in `dotnet-tools.json`. From the repository root:

```powershell
dotnet tool restore
dotnet ef migrations add YourNextMigration --project backend/AgentGate.Infrastructure --startup-project backend/AgentGate.Api --output-dir Persistence/Migrations
```

For database update, use the launcher to import local database and JWT configuration:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
```

EF CLI does not load `.env` automatically. For direct EF commands, export database variables and `JWT_SECRET` into your process first. The startup project supplies configuration and dependency injection; the migration assembly is Infrastructure. Current migrations are `AccountsAndAuthentication`, `AgentIdentityAndApiKeys`, and `PersistedAgentActions`.

## Troubleshooting

- **Docker command missing:** use your installed PostgreSQL with the credentials and database name from `.env`, or install Docker Desktop and reopen your terminal.
- **Database unavailable:** check `docker compose ps`, the database health check, credentials, port mapping, and `docker compose logs postgres`.
- **API unavailable:** start the backend and check `http://localhost:5000/health` directly. Ensure port 5000 is free.
- **Dashboard port in use:** stop the existing process or change Vite's port intentionally. It uses `strictPort` to avoid silently selecting a different port.
- **Existing PostgreSQL volume:** its initialized password persists even after editing `.env`.
- **Visual Studio:** open the root solution or `backend/AgentGate.sln`; both have the same project references.

## Phase boundaries

Phase 2 implements organizations, authentication, JWT, refresh rotation, and roles. Phase 3 adds registered agents and hashed API keys. Phase 4 adds persisted action requests, validation, idempotency, and dashboard history. `./scripts/test-backend.ps1` runs all 79 tests, including HTTP integration tests against isolated PostgreSQL databases. PostgreSQL must be running and the configured role must be able to create test databases. Use `-UnitOnly` for 25 role, key-format, and canonical action validation tests without a database. See [authentication.md](authentication.md), [agents.md](agents.md), and [actions.md](actions.md).

Policy evaluation, approvals, Slack, and audit history remain later phases. Development-only test allow results must not authorize real sensitive operations.
