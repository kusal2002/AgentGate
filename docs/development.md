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

EF CLI does not load `.env` automatically. For direct EF commands, export database variables and `JWT_SECRET` into your process first. The startup project supplies configuration and dependency injection; the migration assembly is Infrastructure. Current migrations are `AccountsAndAuthentication`, `AgentIdentityAndApiKeys`, `PersistedAgentActions`, `DeterministicPolicies`, `HumanApprovals`, `SlackIntegration`, `SlackInteractionFeedback`, and `AppendOnlyAuditLog`. The local launcher also imports `Approvals__*` and `PolicyDefaults__*` from ignored `.env`; only Review/Deny no-match defaults are accepted.

## Troubleshooting

- **Docker command missing:** use your installed PostgreSQL with the credentials and database name from `.env`, or install Docker Desktop and reopen your terminal.
- **Database unavailable:** check `docker compose ps`, the database health check, credentials, port mapping, and `docker compose logs postgres`.
- **API unavailable:** start the backend and check `http://localhost:5000/health` directly. Ensure port 5000 is free.
- **Windows locked DLLs during build:** `scripts/test-backend.ps1` builds into ignored `.local-verification/backend-tests`, so tests can run while the API is open. For a direct `dotnet build` or migration that rebuilds the normal API output, stop the API with Ctrl+C first, then restart it afterward.
- **Dashboard port in use:** stop the existing process or change Vite's port intentionally. It uses `strictPort` to avoid silently selecting a different port.
- **Existing PostgreSQL volume:** its initialized password persists even after editing `.env`.
- **Visual Studio:** open the root solution or `backend/AgentGate.sln`; both have the same project references.

## Phase boundaries

Phases 2–4 add accounts, organizations, agent identity, keys, persisted actions, and idempotency. Phase 5 adds deterministic policies, management, preview, and outcome snapshots. `./scripts/test-backend.ps1` runs all 236 tests in isolated PostgreSQL databases. The role must be able to create test databases. Use `-UnitOnly` for 106 role, key-format, action validation, policy evaluator, and approval permissions, Slack signature/HTTP client, and audit redaction cases without a database. See [policies.md](policies.md) for the manual checklist.

Phase 6 creates approval requests atomically for review outcomes and resolves them with transactional human decisions or deadline expiry. See [approvals.md](approvals.md) for configuration and manual checks. Phase 7 adds signed Slack interactions, durable message updates, and reviewer mappings; see [slack-setup.md](slack-setup.md). Phase 8 adds append-only authorization events and timelines; see [audit.md](audit.md) for migration snapshots, redaction, and manual checks. Phase 9 completes the live dashboard, per-agent totals, audit search/filtering, and workspace navigation; see [dashboard.md](dashboard.md). No Phase 9 schema migration is required. Historical temporary test allows cannot be replayed through the agent API after Phase 5.
