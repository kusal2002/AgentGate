# API

Base URL: `http://localhost:5000`.

Authentication/organization endpoints are documented in [authentication.md](authentication.md). Agent management and API-key authentication are documented in [agents.md](agents.md). Persisted action requests and history are documented in [actions.md](actions.md). Management/history routes use human JWTs; agent identity and action submission/retrieval use agent keys. Health remains public.

## GET /health

HTTP 200:

```json
{ "status": "ok", "service": "AgentGate" }
```

No database or authentication is required.

## GET /health/ready

HTTP 200 when PostgreSQL is reachable:

```json
{
  "status": "healthy",
  "service": "AgentGate",
  "checks": [{ "name": "postgresql", "status": "healthy" }]
}
```

HTTP 503 when PostgreSQL is unavailable; status and check status become `unhealthy`. This does not imply business tables exist.

## Action requests and history

Phase 4 replaces the old refund prototype with persisted requests at `POST /v1/actions/evaluate` and agent-scoped retrieval at `GET /v1/actions/{id}`. The request requires action, resource, parameters, and an idempotency key; context is optional. Identical retries return one action, and changed payloads under the same key return 409.

Human JWTs can read paginated organization history at `GET /api/actions` and details at `GET /api/actions/{id}`. Phase 5 evaluates enabled organization policies. No-match defaults follow the agent environment: Development review, Staging/Production deny. No external action executes. See [actions.md](actions.md) for request examples and limits, and [policies.md](policies.md) for policy APIs, defaults, and verification.

## Human approvals

Review outcomes create an approval atomically with the action. Human JWTs read `/api/approvals` and `/api/approvals/{id}` and resolve requests with POST `/api/approvals/{id}/approve` or `/reject`. The POST routes also have `/v1/approvals/{id}/approve` and `/reject` aliases and still require human authentication.

Agent keys poll their own requests at GET `/v1/approvals/{id}`. Required reviewer roles, comments, expiry, conflicts, and response contracts are documented in [approvals.md](approvals.md). Approval changes action status while retaining the original policy decision; no external action executes.

## Slack integration

Human JWTs read `GET /api/integrations/slack`. Owner/Admin can `PUT /api/integrations/slack` with `{ channelId, enabled }`, `PUT /api/integrations/slack/reviewers` with `{ userId, slackUserId }`, and `DELETE /api/integrations/slack/reviewers/{userId}`. Configuration is limited to the deployment's configured organization; identity and tenant override fields are rejected. Read responses include availability, organization/workspace, channel, enabled state, mappings, and failed delivery count; credentials are never returned.

`POST /api/integrations/slack/actions` accepts URL-encoded Slack interaction payloads with signature/timestamp headers, independently of human/agent authentication. Raw signatures are checked before form parsing. Callbacks must match a delivered message, app/workspace, and authorized reviewer. Acknowledgments are quick; the worker updates the shared message and sends private feedback through Slack's fixed Web API. See [slack-setup.md](slack-setup.md) for installation and live checks.

## Audit history

Human JWTs read `GET /api/audit`, `GET /api/audit/{id}`, `GET /api/actions/{id}/timeline`, and `GET /api/approvals/{id}/timeline`. All roles can read their current organization's history; agent API keys cannot. List results support agent/action/approval IDs, event type, actor type, inclusive date bounds, and pagination. Timelines are oldest first and lists newest first. Audit endpoints do not accept writes. See [audit.md](audit.md) for the contract, redaction, migration snapshots, integrity boundaries, and manual checks.
