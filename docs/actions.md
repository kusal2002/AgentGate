# Phase 4: persisted action requests

Phase 4 replaces the old `amountMinor` refund prototype with a generic, persisted action API. It adds validation, concurrent idempotency, agent-scoped retrieval, and tenant-scoped dashboard history. Phase 5 adds the policy engine and Phase 6 adds human approvals. Execution reporting and audit events belong to later phases.

## Setup

Use your installed PostgreSQL and ignored `.env`. Stop the API before building on Windows, then apply `PersistedAgentActions` and restart:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

The migration adds `AgentActions`, a composite agent/organization foreign key, a unique organization/agent/idempotency-key index, and an organization history index. Startup does not automatically migrate. Existing users, agents, and API keys are preserved.

## Current evaluation behavior

Phase 5 now evaluates enabled organization policies. Requests return allow/approved, review/awaiting_approval, or deny/denied, with the matched rule, risk, reviewer, and reason. No-match defaults follow the agent environment: Development review, Staging/Production deny. See [policies.md](policies.md) for rule setup, configuration, and threshold tests.

No external action is executed. Review requests include an approval ID, status, and deadline. See [approvals.md](approvals.md) to approve, reject, or poll a request. Historical Phase 4 test allow records remain visible in the dashboard, but agent replay/retrieval returns 403; submit with a new idempotency key for a real policy decision.

## Submit and check a request

Sign in at `http://localhost:5173`, open a Development agent, and generate/copy an API key. Run this from PowerShell:

```powershell
$env:AGENTGATE_API_KEY = Read-Host 'Paste your Development agent API key'
$headers = @{ Authorization = "Bearer $env:AGENTGATE_API_KEY" }
$body = @{
    action = 'refund'
    resource = @{ type = 'customer'; id = 'CUS-102' }
    parameters = @{ amount = 750; currency = 'USD'; reason = 'duplicate payment' }
    context = @{ customerTier = 'business' }
    idempotencyKey = 'phase5-refund-order-8821'
} | ConvertTo-Json -Depth 10

$first = Invoke-RestMethod http://localhost:5000/v1/actions/evaluate `
    -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$first
$retry = Invoke-RestMethod http://localhost:5000/v1/actions/evaluate `
    -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$first.actionId -eq $retry.actionId # True
Invoke-RestMethod "http://localhost:5000/v1/actions/$($first.actionId)" -Headers $headers
```

With the demo rules from [policies.md](policies.md) installed, expect review / awaiting_approval, testEvaluation false, the Medium refund policy, risk Medium, and reviewer Reviewer. Without a matching rule, a Development agent defaults to review with High risk and no matched policy. The response includes the pending approval ID and deadline; open **Approvals** to review it.

Open **Actions** in the dashboard. There should be one row despite submitting twice. Filter by agent, open the action, check its resource/parameters/context and **Not executed** state, and reload. The agent detail page also shows its five most recent actions. History automatically refreshes every 30 seconds or with **Refresh actions**.

Change `amount` to 751 and submit with the same idempotency key: expect **409**, with no new row. Use a new key for a new request. Disable the agent or revoke its key and submit again: expect **401**. Staging/Production requests use their matching policies or default to deny. A policy change affects new requests; retries retain the original stored result.

## Request validation

- `action`: required lowercase identifier, 1–100 characters; letters, digits, underscores, dots, colons, and hyphens, starting with a letter.
- `resource.type`: same identifier rules. `resource.id`: required nonblank string, at most 200 characters.
- `idempotencyKey`: required nonblank string, at most 200 characters. Leading/trailing whitespace and control characters in text fields are rejected. Keys are case-sensitive.
- `parameters`: required JSON object. `context`: optional JSON object, defaults to `{}`; explicit null is rejected. Each object is limited to 32 KiB UTF-8 and 16 levels of nesting. Duplicate properties inside either object are rejected.
- Numbers must fit .NET decimal precision without rounding. Refunds specifically require a positive decimal `parameters.amount` (major units, so 750 means USD 750) and a three-letter uppercase `currency`. Generic actions such as `send_email` accept object parameters; their policies and schemas will be expanded later.
- Unknown top-level/resource fields, including client-provided organization, decision, approval, or execution state, return 400. Tenant and agent identity always come from the validated API key.
- HTTP bodies are limited to 64 KiB and return 413 if oversized. Malformed JSON and validation failures return 400 Problem Details and create no action.

## Idempotency

Scope is `(OrganizationId, AgentId, IdempotencyKey)`, enforced in PostgreSQL. Identical retries return HTTP 200 and the original action ID and policy decision with the current approval/action status, including simultaneous requests and retries after restarting the backend. Rotating an agent key does not change the scope. A key reused with different action/resource/parameters/context returns 409.

Request hashes are SHA-256 over canonical payloads. Object property ordering and insignificant numeric zeroes do not change identity; array order and actual values do. The first successful database insert wins a race; losing requests retrieve that row and compare its hash. No in-memory idempotency cache is used. This prevents duplicate request records; exactly-once external execution remains future work.

## Endpoints

| Authentication | Method | Route | Behavior |
| --- | --- | --- | --- |
| Agent key | POST | `/v1/actions/evaluate` | Validate, evaluate policies, persist, and return the decision |
| Agent key | GET | `/v1/actions/{id}` | Return the outcome for this agent's own stored action |
| Human JWT | GET | `/api/actions?agentId=…&page=1&pageSize=25` | Paginated organization history; optional agent filter |
| Human JWT | GET | `/api/actions/{id}` | Full organization-scoped details |

All five organization roles can read history. Human JWTs cannot submit agent action requests, and agent keys cannot read dashboard history. Other organizations' IDs and other agents' IDs in agent retrieval return 404. Foreign-agent history filters return an empty list. Page numbers are 1–1000000 and page sizes are 1–100. Lists omit parameter/context payloads and idempotency keys. Evaluation and detail responses set `Cache-Control: no-store`; agent routes use the existing pre-authentication agent rate limiter.

Parameters and context are stored as JSONB for future policy evaluation. Do not put credentials or unnecessary personal data in them. Payload values are not written to application logs. The dashboard masks conventional credential field names recursively for display, but this is a display convenience; authorized detail APIs still return the original stored data.

## Automated verification

```powershell
./scripts/test-backend.ps1
./scripts/test-backend.ps1 -UnitOnly
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

The full suite has 175 tests; unit-only has 82. The test script uses separate build outputs in ignored `.local-verification/backend-tests`, so you can leave the API running during tests on Windows. PostgreSQL tests create and remove isolated databases. Coverage includes concurrent identical and conflicting retries, canonical hashing, restart persistence, validation and limits, tenant/agent/scheme separation, all role reads, pagination/filtering, policy evaluation, and legacy test-result boundaries.
