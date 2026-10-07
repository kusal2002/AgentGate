# Architecture

AgentGate will authenticate agent action requests, evaluate deterministic policies, obtain human approval where required, and retain an append-only audit trail. It will not depend on an LLM for authorization.

## Current dependencies

```text
Domain <- Application <- Infrastructure
              ^              ^
              +---- API -----+

Dashboard -- Vite proxy --> API --> Infrastructure --> PostgreSQL
```

- **Domain:** provider-independent account, agent, API-key, AgentAction, and Policy entities with role/status/environment/decision enums.
- **Application:** references Domain; owns account, agent, action, and policy use cases, validation/canonical hashing, DTOs, persistence/password/token interfaces, API-key generation/verification, and deterministic policy matching/defaults.
- **Infrastructure:** references Application; owns EF Core account/agent/action stores, atomic insert-or-retrieve idempotency, ASP.NET password hashing, Npgsql connection setup, and database readiness checks.
- **API:** references Application and Infrastructure; owns thin controllers, separate JWT and agent-key authentication schemes, rate limits, cookie handling, HTTP account context, and Problem Details.
- **Dashboard:** React Router handles navigation; TanStack Query handles server health state; Tailwind v4 and local shadcn/ui components handle styling.

Membership and session records carry organization ownership. Current organization comes from the JWT's validated database session. Member lookup always combines the organization ID with the member ID. User-specific organization lists filter by the authenticated user. No organization IDs are hard-coded.

`AgentGateDbContext` scans entity configurations in Infrastructure. Database creation and migrations are explicit developer operations; startup does not call `EnsureCreated` or auto-migrate. `AccountsAndAuthentication` and `AgentIdentityAndApiKeys` establish unique keys, tenant/environment foreign keys, and optimistic concurrency for role and agent metadata updates.

Action handling lives in application services and thin controllers. `PersistedAgentActions` stores JSONB payloads and immutable request hashes, enforces a unique tenant/agent/idempotency key, and links each action to an agent in the same tenant. Concurrent inserts resolve through the unique constraint. Phase 5's `DeterministicPolicies` adds organization policies and a tenant-scoped action/policy foreign key. Enabled rules match typed conditions deterministically; priority and deny/review/allow tie rules select the winner. Snapshot fields retain the matching policy name/update timestamp, reason, risk, and reviewer even after edits. Retries do not re-evaluate. Legacy Phase 4 test allows cannot authorize policy-controlled work. See [actions.md](actions.md) and [policies.md](policies.md).

## Configuration

The database accepts `ConnectionStrings__AgentGate` as an override. Otherwise its connection string is built safely from `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`. No password is present in source files or appsettings.

The root `.env` feeds optional Docker Compose, the PowerShell backend launcher, and Vite. Only `VITE_` variables are exposed to browser code. The backend launcher imports database and JWT variables and never evaluates their values as code. Authentication configuration requires a random `JWT_SECRET`; see [authentication.md](authentication.md).

## Health semantics

Liveness (`/health`) reports that the HTTP host is running. Readiness (`/health/ready`) opens a database connection through EF Core and does not create tables. The dashboard uses both so a database outage cannot masquerade as a working persistence layer.
