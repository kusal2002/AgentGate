# Phase 5: deterministic policies

Phase 5 replaces the temporary Development test evaluator with deterministic organization policies. Enabled rules return `allow`, `review`, or `deny`. No LLM participates in authorization. Decisions, matching policy ID/name, policy update timestamp, risk, reason, and reviewer role are saved with the action. Phase 6 now adds human approval requests and decisions; see [approvals.md](approvals.md). No external action is executed.

## Setup and manual checks

Use your installed PostgreSQL. The `20261007104059_DeterministicPolicies` migration adds policies, optimistic version tracking, tenant-scoped policy references, and outcome snapshot fields. Existing accounts, agents, keys, and actions are preserved.

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

Stop the normal API before rebuilding/migrating on Windows. Tests use separate build outputs and can run while it stays open.

1. Sign in as Owner or Admin, then open **Policies**. Open **Create policy** to build a rule, or click **Add refund demo policies** in the development dashboard. The seed endpoint is unavailable outside a Development server. Seeding adds missing samples and preserves edits and disabled state on repeated requests.
2. Register a Development agent if you do not already have one. Open **Test policies**, select that agent, and leave action `refund`, resource `customer` / `CUS-102`, and currency `USD`.
3. Test the four sample amounts below. The matched rule, decision, risk, and reviewer must agree. Previews create no action or approval records.

| Amount | Decision | Risk | Matched sample rule |
| --- | --- | --- | --- |
| 50 | allow | Low | Small refund |
| 750 | review | Medium | Medium refund |
| 2000 | review | High | Large refund |
| 15000 | deny | Critical | Extremely large refund |

4. Open a policy, edit its conditions or priority, save, and test again. Disable/enable it from the list and confirm that new previews change. A stale edit returns 409 rather than overwriting another person's changes.
5. Submit through the action API using the example in [actions.md](actions.md), **with a new idempotency key** such as `phase5-refund-750`. Expect `review`, `awaiting_approval`, a matching policy, `Medium` risk, and reviewer `Reviewer`. Open the resulting action in **Actions** and check the saved details. The response now includes an approval ID and deadline. Open **Approvals** to approve or reject as an eligible reviewer.
6. Retry that exact request and confirm the same action/outcome. Edit the policy and retry again: the original stored result remains. A new idempotency key evaluates the current policy. Disabling a rule does not rewrite past actions.
7. Verify a Developer can view/test policies but cannot create/edit/toggle them. Reviewer and Viewer can read definitions and action history, but cannot test or manage policies.

The sample rules include `agent.environment equals Development` and `parameters.currency equals USD`; they never authorize Production/Staging agents or other currencies. They cover <=100 (allow), >100 and <=1000 (review), >1000 (review), and >10000 (deny). Extreme refunds match two rules, and the higher-priority deny wins. Custom policies apply to all agents in the organization unless their conditions limit the environment/agent.

## Selection and matching

Only enabled policies in the authenticated organization whose `actionType` exactly matches the request are considered. Highest priority wins. At equal priority, deny wins over review, which wins over allow. Equal decision/priority ties use ascending policy UUID, so insertion order does not affect results. The list uses the same order.

Each policy combines **all** conditions with AND. An empty conditions array matches every valid request for that action type. Priorities are integers from 0 to 10000. No wildcard actions, arbitrary scripts, regex conditions, OR groups, or graphical workflows are supported.

Fields:

- `parameters.amount`, `parameters.currency`, or other object paths under `parameters`.
- `context.customerTier` or nested paths such as `context.customer.tier`.
- `resource.type` and `resource.id`.
- `agent.id` and `agent.environment`, derived from authenticated identity for submission or the selected tenant-scoped agent for preview. Client-supplied identity/environment cannot override these fields.

Parameter/context paths use dot-separated identifier segments, up to eight levels. Array indexing and property names containing dots are not supported.

| Operators | Expected condition value | Matching behavior |
| --- | --- | --- |
| `equals`, `not_equals` | String, exact decimal, or boolean | Same-type comparison |
| `greater_than`, `greater_than_or_equal`, `less_than`, `less_than_or_equal` | Exact decimal | Numeric comparison, without converting strings |
| `contains`, `not_contains` | Nonempty string | Case-sensitive ordinal substring comparison |
| `in`, `not_in` | 1–100 same-type scalar values | Same-type membership comparison |

Strings are case-sensitive; `USD` and `usd` differ. Missing, null, array/object, or incorrectly typed request fields never match, including negative operators. Rules have at most 32 conditions and 16 KiB of condition JSON; scalar strings have at most 1000 characters. Invalid operators, paths, reviewer roles, risks, lossy numbers, and client-provided tenant/outcome fields are rejected with 400. Corrupt stored rules encountered during evaluation return a critical deny rather than falling through to allow.

## Defaults and outcomes

If no rule matches, defaults are **Development: review; Staging: deny; Production: deny**. They follow the **agent's environment**, regardless of the host environment. Change `PolicyDefaults` in API appsettings, process environment, or ignored `.env`:

```dotenv
PolicyDefaults__Development=Review
PolicyDefaults__Staging=Deny
PolicyDefaults__Production=Deny
```

Only `Review` and `Deny` are accepted; invalid settings fail startup. A default allow is not permitted. Defaults have High risk and no matching policy. Default review routes to `Reviewer`. A matching review policy requires `Reviewer`, `Admin`, or `Owner`; other decisions have no reviewer.

| Decision | Stored status | Meaning |
| --- | --- | --- |
| allow | approved | The rule permits the request; AgentGate has not executed it |
| review | awaiting_approval | Hold the request with a pending human approval |
| deny | denied | Do not execute |

New results have `testEvaluation: false`. Historical Phase 4 test allows remain visible in dashboard history, but agent retrieval/replay returns 403 after the policy engine is installed. Submit a new idempotency key for a real policy evaluation. Historical non-test denied/reviewed actions still replay their original results.

## Management and preview APIs

All endpoints use human JWTs. Read access is available to all five roles; management requires Owner/Admin, and testing requires Owner/Admin/Developer. Application services also enforce mutation/test roles, not just controllers.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/policies` | List tenant-scoped rules in selection order |
| GET | `/api/policies/{id}` | Read a definition and concurrency version |
| POST | `/api/policies` | Create a rule |
| PUT | `/api/policies/{id}` | Replace a definition using its current `version` |
| POST | `/api/policies/{id}/status` | Set `{ enabled, version }` |
| POST | `/api/policies/test` | Preview current enabled rules without saving an action |
| POST | `/api/policies/seed-refund-demo` | Development-only, idempotent sample-rule seed |

Example creation:

```json
{
  "name": "Large refund approval",
  "description": "Review USD refunds above $500.",
  "actionType": "refund",
  "priority": 100,
  "enabled": true,
  "conditions": [
    { "field": "parameters.currency", "operator": "equals", "value": "USD" },
    { "field": "parameters.amount", "operator": "greater_than", "value": 500 }
  ],
  "decision": "review",
  "reviewerRole": "Reviewer",
  "riskLevel": "Medium"
}
```

For updates, include `version` from the latest GET/create response. PostgreSQL row versions ensure that only one concurrent edit succeeds; a stale version returns 409. Policies are disabled rather than deleted, preserving action references.

Preview request:

```json
{
  "agentId": "the UUID of your registered agent",
  "action": "refund",
  "resource": { "type": "customer", "id": "CUS-102" },
  "parameters": { "amount": 750, "currency": "USD" },
  "context": {}
}
```

Preview uses the same payload validation and evaluator as action submission. A cross-tenant agent ID or policy ID returns 404. Agent API keys cannot access policy management/testing. Preview responses set `Cache-Control: no-store`. No payload values or secrets are logged. Definitions are limited to 32 KiB HTTP bodies and previews to 64 KiB.

## Verification

```powershell
./scripts/test-backend.ps1
./scripts/test-backend.ps1 -UnitOnly
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

The full suite has 175 tests and unit-only has 82. Coverage includes the ten operators, threshold boundaries, ordinal matching, nested fields, missing/null/type behavior, priority conflicts, defaults, role and tenant isolation, optimistic concurrency, concurrent seeding, preview/persisted agreement, production policy evaluation, stable historical snapshots/retries, and blocked legacy test allows. PostgreSQL integration tests create and drop isolated databases.
