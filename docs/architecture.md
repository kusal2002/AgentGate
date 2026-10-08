# Architecture

AgentGate will authenticate agent action requests, evaluate deterministic policies, obtain human approval where required, and retain an append-only audit trail. It will not depend on an LLM for authorization.

## Current dependencies

```text
Domain <- Application <- Infrastructure
              ^              ^
              +---- API -----+

Dashboard -- Vite proxy --> API --> Infrastructure --> PostgreSQL
```

- **Domain:** provider-independent account, agent, API-key, AgentAction, Policy, ApprovalRequest, and ApprovalDecision entities with role/status/environment/decision enums.
- **Application:** references Domain; owns account, agent, action, policy, and approval use cases, validation/canonical hashing, DTOs, persistence/password/token interfaces, API-key generation/verification, deterministic policy matching/defaults, and reviewer permission rules.
- **Infrastructure:** references Application; owns EF Core account/agent/action stores, atomic insert-or-retrieve idempotency, transactional approval resolution/expiry, ASP.NET password hashing, Npgsql connection setup, and database readiness checks.
- **API:** references Application and Infrastructure; owns thin controllers, separate JWT and agent-key authentication schemes, rate limits, cookie handling, HTTP account context, Problem Details, and the approval expiry worker.
- **Dashboard:** React Router handles navigation; TanStack Query handles server health state; Tailwind v4 and local shadcn/ui components handle styling.

Membership and session records carry organization ownership. Current organization comes from the JWT's validated database session. Member lookup always combines the organization ID with the member ID. User-specific organization lists filter by the authenticated user. No organization IDs are hard-coded.

`AgentGateDbContext` scans entity configurations in Infrastructure. Database creation and migrations are explicit developer operations; startup does not call `EnsureCreated` or auto-migrate. `AccountsAndAuthentication` and `AgentIdentityAndApiKeys` establish unique keys, tenant/environment foreign keys, and optimistic concurrency for role and agent metadata updates.

Action handling lives in application services and thin controllers. `PersistedAgentActions` stores JSONB payloads and immutable request hashes, enforces a unique tenant/agent/idempotency key, and links each action to an agent in the same tenant. Concurrent inserts resolve through the unique constraint. Phase 5's `DeterministicPolicies` adds organization policies and a tenant-scoped action/policy foreign key. Enabled rules match typed conditions deterministically; priority and deny/review/allow tie rules select the winner. Snapshot fields retain the matching policy name/update timestamp, reason, risk, and reviewer even after edits. Retries do not re-evaluate. Legacy Phase 4 test allows cannot authorize policy-controlled work. See [actions.md](actions.md) and [policies.md](policies.md).

## Configuration

The database accepts `ConnectionStrings__AgentGate` as an override. Otherwise its connection string is built safely from `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`. No password is present in source files or appsettings.

The root `.env` feeds optional Docker Compose, the PowerShell backend launcher, and Vite. Only `VITE_` variables are exposed to browser code. The backend launcher imports database and JWT variables and never evaluates their values as code. Authentication configuration requires a random `JWT_SECRET`; see [authentication.md](authentication.md).

## Health semantics

Liveness (`/health`) reports that the HTTP host is running. Readiness (`/health/ready`) opens a database connection through EF Core and does not create tables. The dashboard uses both so a database outage cannot masquerade as a working persistence layer.

## Approval state transitions

`HumanApprovals` links each approval to an action in the same tenant using a composite foreign key and unique index. Action and approval insertion share one EF transaction. Human resolution and deadline expiry lock the same approval row and update approval/action state in one transaction. The database clock defines the deadline; current membership defines reviewer eligibility. A unique decision index allows one winning human decision, and EF guards plus a PostgreSQL trigger prevent decision updates/deletes.

The background worker expires batches every 30 seconds. Tenant-scoped reads also refresh due requests, and resolution checks expiry while holding the lock. Pending requests survive restarts. See [approvals.md](approvals.md) for roles, migration backfill, and timeout configuration.

## Slack boundary

Phase 7 keeps bot credentials/signing secrets in server environment configuration and binds the installation to one workspace/organization. Persistent integration settings and explicitly verified reviewer mappings are tenant-scoped. Durable scanning discovers committed pending approvals without coupling agent submission to Slack availability. Delivery locks serialize each tracked message; no approval lock is held during outbound HTTP. The worker updates terminal outcomes and retries failures, respecting Slack backoff.

An API resource filter validates raw signatures before MVC consumes forms. Callback message/app/workspace references must match database deliveries, and the stored user mapping feeds the existing transactional approval permission checks. Immutable decisions record their Dashboard/Slack source. Durable private feedback uses `chat.postEphemeral`; callback-provided URLs are never followed. Remote delivery can duplicate after an ambiguous network failure, but database approval resolution remains single-winner. See [slack-setup.md](slack-setup.md).

## Audit boundary

Domain owns `AuditEvent` and its actor types. Application owns audit contracts, input validation, tenant-scoped read use cases, and recursive sensitive field redaction. Infrastructure queues allowlisted event metadata into the same EF unit of work as business mutations and owns indexed, paginated persistence. API provides authenticated request context and thin read-only controllers. No caller can choose its audit tenant or actor identity through an HTTP request.

`AppendOnlyAuditLog` adds tenant-scoped foreign keys, imports existing actions as explicitly labeled snapshots, and installs PostgreSQL triggers blocking update, delete, and truncate. EF guards reject edits before reaching the database. Action/approval transitions and their events commit atomically; idempotency races detach losing events. Human and expiration events use the existing approval row lock. Slack delivery events commit with successful delivery state after outbound HTTP. Credentials, arbitrary payloads, and comments are omitted. The direct connection IP is recorded for request actors without trusting forwarded headers. See [audit.md](audit.md).

## Dashboard read models

Application owns overview/statistics contracts and tenant-scoped use cases; Infrastructure aggregates persisted records in a repeatable-read transaction after bounded expiration maintenance. Counts and ten recent requests share a snapshot. Pending counts explicitly exclude due deadlines even when maintenance has more than one batch to process. Human latency averages only recorded approvals/rejections. Legacy test results and previews do not inflate policy statistics; approval never implies successful execution.

Audit filtering uses tenant-scoped related actions and recorded reviewers. Page enrichment performs bounded lookups for names and action context without loading request payloads or issuing one query per event. Names are current lookup labels; immutable actor IDs and metadata remain authoritative. Dashboard and header queries share an organization-keyed cache, and organization switching clears cached data. No schema change is needed for Phase 9. See [dashboard.md](dashboard.md).
