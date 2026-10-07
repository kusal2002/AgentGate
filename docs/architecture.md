# Architecture

AgentGate will authenticate agent action requests, evaluate deterministic policies, obtain human approval where required, and retain an append-only audit trail. It will not depend on an LLM for authorization.

## Phase 1 dependencies

```text
Domain <- Application <- Infrastructure
              ^              ^
              +---- API -----+

Dashboard -- Vite proxy --> API --> Infrastructure --> PostgreSQL
```

- **Domain:** no infrastructure packages or business entities in this phase.
- **Application:** references Domain, reserved for contracts and use cases.
- **Infrastructure:** references Application; owns EF Core, Npgsql connection setup, and database readiness checks.
- **API:** references Application and Infrastructure; owns hosting and endpoint composition.
- **Dashboard:** React Router handles navigation; TanStack Query handles server health state; Tailwind v4 and local shadcn/ui components handle styling.

Application and Domain intentionally have no placeholder business classes. Future models must carry organization ownership where appropriate and enforce tenant boundaries in every tenant query. No organization IDs are hard-coded here.

`AgentGateDbContext` is ready for entity configurations in Infrastructure. Database creation and migrations are explicit developer operations; startup does not call `EnsureCreated` or auto-migrate. No model means no initial migration in Phase 1.

The legacy refund prototype remains in the API file to preserve existing behavior in Development. Before evolving it into the real action API, move decisions into Application/Domain services and add authentication, persistence, idempotency, and tests.

## Configuration

The database accepts `ConnectionStrings__AgentGate` as an override. Otherwise its connection string is built safely from `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`. No password is present in source files or appsettings.

The root `.env` feeds Docker Compose, the PowerShell backend launcher, and Vite. Only `VITE_` variables are exposed to browser code. The backend launcher imports only database-related variables and never evaluates their values as code.

## Health semantics

Liveness (`/health`) reports that the HTTP host is running. Readiness (`/health/ready`) opens a database connection through EF Core and does not create tables. The dashboard uses both so a database outage cannot masquerade as a working persistence layer.
