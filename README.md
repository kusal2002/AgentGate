# AgentGate

**The authorization layer for AI agents.** Control what your AI agents can do, require human approval for sensitive actions, and maintain a complete audit trail.

## Current milestone

Phase 1 establishes the monorepo, .NET 10 Clean Architecture backend, EF Core/PostgreSQL configuration, and React dashboard shell. Authentication, organization data, agent keys, policies, approvals, Slack, SDK, and the AI demo belong to later phases.

The pre-existing, unauthenticated refund prototype remains available **only in Development** at `POST /v1/actions/evaluate`. It accepts `amountMinor` and is not the future persisted action API. Do not use it to authorize real actions.

## Repository

```text
backend/
  AgentGate.sln
  AgentGate.Api/             HTTP host and endpoint composition
  AgentGate.Application/     Future use cases and service contracts
  AgentGate.Domain/          Future provider-independent domain rules
  AgentGate.Infrastructure/  EF Core context, Npgsql, database health
dashboard/                  React + TypeScript + Vite
sdk/typescript/             Reserved for Phase 10
examples/refund-agent/      Reserved for Phase 11
docker/                     Container setup notes
docs/                       Architecture, API, and development docs
scripts/start-backend.ps1   Safe local environment loader and API launcher
```

The original root `AgentGate.sln` is retained for existing Visual Studio workflows. Both solutions contain the same four projects; add future projects to both.

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
./scripts/start-backend.ps1
```

The script reads `POSTGRES_*` and `ConnectionStrings__*` values from `.env` as data. Existing process environment variables take precedence. ASP.NET Core does not load dotenv files automatically. Outside PowerShell, set the PostgreSQL variables in your process and run `dotnet run --project backend/AgentGate.Api --launch-profile http`.

Start the dashboard in another terminal:

```powershell
cd dashboard
npm ci
npm run dev
```

Open [the dashboard](http://localhost:5173). The API listens at [localhost:5000](http://localhost:5000/health). Vite forwards `/api/*` to the backend and strips `/api`, so no CORS configuration is needed for local development. This proxy is a development feature; production hosting will need a same-origin reverse proxy or explicit API/CORS configuration.

The dashboard displays live service health. Its other pages are placeholders without business data or actions.

## Verify

```powershell
dotnet build backend/AgentGate.sln
npm --prefix dashboard run build
npm --prefix dashboard run lint
Invoke-RestMethod http://localhost:5000/health
Invoke-RestMethod http://localhost:5000/health/ready
```

`/health` returns 200 while the API is running, independently of the database. `/health/ready` checks PostgreSQL and returns 200 if reachable or 503 otherwise. No credentials or exception details are returned to the browser.

EF Core is configured but the domain has no entities yet, so Phase 1 requires no schema migration. Migration tooling and the workflow are described in [development.md](docs/development.md).

Stop the database with `docker compose stop postgres`. Its data survives restart in the named volume. Changing `.env` does not change the password of an already initialized PostgreSQL volume; update the database role password or deliberately recreate the local volume.

## Further documentation

- [Architecture](docs/architecture.md)
- [API and prototype](docs/api.md)
- [Development and migrations](docs/development.md)
- [Slack setup roadmap](docs/slack-setup.md)
- [Verification record](docs/verification.md)

The demo refund workflow is planned for later phases. No Slack app, tokens, or AI provider integration is created by this milestone.
