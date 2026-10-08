# AgentGate

**The authorization layer for AI agents.** Control what your AI agents can do, require human approval for sensitive actions, and maintain a complete audit trail.

## Current milestone

Phases 1–7 are implemented: the foundation, authentication/organizations, agent identity and secure API keys, persisted action requests with idempotency/history, deterministic policies with management and testing screens, and human approvals with transactional decisions and expiry, plus Slack notifications and approval buttons. Audit events, SDK, and the AI demo belong to later phases. See [authentication.md](docs/authentication.md), [agents.md](docs/agents.md), [actions.md](docs/actions.md), and [policies.md](docs/policies.md) for the implemented APIs. Human approval APIs and the current manual checklist are in [approvals.md](docs/approvals.md).

`POST /v1/actions/evaluate` persists the specification's request and evaluates enabled organization policies, recording allow/review/deny, the matched rule, risk, reviewer role, and reason. With no match, Development agents default to review and Staging/Production to deny. Phase 4's temporary test allow is removed; historical test results cannot authorize policy-controlled work. No external action is executed. Review outcomes create one pending approval atomically with the action. Authorized humans can approve or reject; agents poll the result. See [Phase 6 manual checks](docs/approvals.md).

## Repository

```text
backend/
  AgentGate.sln
  AgentGate.Api/             HTTP host and endpoint composition
  AgentGate.Application/     Use cases and service contracts
  AgentGate.Domain/          Provider-independent domain rules
  AgentGate.Infrastructure/  EF Core context, Npgsql, database health
  AgentGate.Tests/           Role rules and PostgreSQL HTTP integration tests
dashboard/                  React + TypeScript + Vite
sdk/typescript/             Reserved for Phase 10
examples/refund-agent/      Reserved for Phase 11
docker/                     Container setup notes
docs/                       Architecture, API, and development docs
scripts/start-backend.ps1   Safe local environment loader and API launcher
scripts/test-backend.ps1    Isolated PostgreSQL integration tests
```

The original root `AgentGate.sln` is retained for existing Visual Studio workflows. Both solutions contain the same projects; add future projects to both.

## Prerequisites

- .NET SDK 10.0.201 or newer in the .NET 10 line.
- Node.js 22.12+ or Node.js 24, with npm.
- PostgreSQL installed locally, or Docker Desktop with Linux containers and Compose v2.
- Git.

Slack and an LLM API key are **not required for Phase 1**.

## Run locally (PowerShell)

From the repository root:

```powershell
Copy-Item .env.example .env
```

Edit `.env` and set `POSTGRES_PASSWORD` to your own local password. The password is intentionally empty in the template. Do not commit `.env`. `POSTGRES_HOST` is the host visible to the backend, normally `localhost`.

Set `JWT_SECRET` to a random signing secret of at least 32 bytes. Generate one with `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`. The existing local environment was configured during Phase 2; a fresh checkout requires its own secret. Never put secrets in `VITE_` variables.

If PostgreSQL is already installed, set `POSTGRES_USER` and `POSTGRES_PASSWORD` to your existing login and create a database named `agentgate` in pgAdmin or with:

```powershell
createdb -h localhost -p 5432 -U postgres agentgate
```

Skip database creation if `agentgate` already exists. Keep the PostgreSQL service running. Docker is not needed with this option.

Alternatively, start PostgreSQL with Docker:

```powershell
docker compose up -d postgres
docker compose ps
```

Start the backend in its own terminal:

```powershell
dotnet restore backend/AgentGate.sln
dotnet build backend/AgentGate.sln
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

The script reads `POSTGRES_*`, `JWT_*`, `ConnectionStrings__*`, `PolicyDefaults__*`, `Approvals__*`, and `SLACK_*` values from `.env` as data. Existing process environment variables take precedence. ASP.NET Core does not load dotenv files automatically. Outside PowerShell, set the database and JWT variables in your process and run `dotnet run --project backend/AgentGate.Api --launch-profile http`.

Start the dashboard in another terminal:

```powershell
cd dashboard
npm ci
npm run dev
```

Open [the dashboard](http://localhost:5173) and create an account. The API listens at [localhost:5000](http://localhost:5000/health). Vite forwards `/api/*` to the backend, retaining the prefix for account, agent, action, and policy APIs and stripping it for health endpoints. No CORS configuration is needed for local development. This proxy is a development feature; production hosting needs a same-origin HTTPS reverse proxy.

The dashboard displays live organization statistics and recent activity, accounts/organizations, agent management (including disable/enable, key expiry presets, and action totals), and Actions history with filtering, pagination, details, and recent requests per agent. Policies includes rule management, a condition builder, and previews. Approvals includes pending requests, reviewer decisions/comments, history, and expiry; the header links directly to the pending queue. Settings includes Slack channel configuration and reviewer mappings. Audit log includes action/decision/risk/reviewer filters, exact identifier search, event details, and timelines on action and approval pages. The overview retains service health checks.

## Verify

```powershell
dotnet build backend/AgentGate.sln
npm --prefix dashboard run build
npm --prefix dashboard run lint
./scripts/test-backend.ps1
Invoke-RestMethod http://localhost:5000/health
Invoke-RestMethod http://localhost:5000/health/ready
```

`/health` returns 200 while the API is running, independently of the database. `/health/ready` checks PostgreSQL and returns 200 if reachable or 503 otherwise. No credentials or exception details are returned to the browser.

The migrations create users, organizations, memberships, hashed refresh sessions, agents, hashed API keys, persisted actions, policies, approval requests, append-only approval decisions and audit events, Slack settings/mappings, and durable delivery/feedback records. Phase 8 imports existing actions as explicit historical snapshots; full event recording starts when the audit migration is applied. Apply migrations with the `-MigrateOnly` command above. Migration tooling and the workflow are described in [development.md](docs/development.md).

Stop the database with `docker compose stop postgres`. Its data survives restart in the named volume. Changing `.env` does not change the password of an already initialized PostgreSQL volume; update the database role password or deliberately recreate the local volume.

## Further documentation

- [Architecture](docs/architecture.md)
- [API](docs/api.md)
- [Development and migrations](docs/development.md)
- [Slack setup and Phase 7 checks](docs/slack-setup.md)
- [Verification record](docs/verification.md)
- [Organizations and authentication](docs/authentication.md)
- [Agent identity and API keys](docs/agents.md)
- [Persisted actions and phase verification](docs/actions.md)
- [Policies and Phase 5 checks](docs/policies.md)
- [Human approvals and Phase 6 checks](docs/approvals.md)
- [Audit log and Phase 8 checks](docs/audit.md)
- [Dashboard and Phase 9 checks](docs/dashboard.md)

The demo refund workflow is planned for later phases. Connect your existing Slack app using [Phase 7 setup and live checks](docs/slack-setup.md). No AI provider integration is required.
