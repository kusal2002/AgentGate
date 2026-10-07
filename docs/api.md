# API

Base URL: `http://localhost:5000`.

Authentication/organization endpoints are documented in [authentication.md](authentication.md). Agent management and API-key authentication are documented in [agents.md](agents.md). Management routes use human JWTs; agent identity and the Development prototype use agent keys. Health remains public.

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

## Existing Development-only refund prototype

`POST /v1/actions/evaluate` preserves the original local prototype. It requires an active Development agent API key. There is no persistence, approval request, or idempotency support. This route is absent outside a Development host, and Staging/Production agent keys cannot call it.

```json
{
  "action": "refund",
  "parameters": { "amountMinor": 75000, "currency": "USD" }
}
```

Amounts are integer minor units, so 75000 represents USD 750. Existing thresholds are unchanged: up to USD 100 allows, up to USD 1000 reviews with Support Manager, up to USD 5000 reviews with Finance Manager, and larger amounts deny. Unsupported actions/currencies deny. Missing/nonpositive amount, action, or currency returns 400.

The prototype's uppercase decisions and thresholds are not the final MVP contract. The eventual persisted `/v1/actions/evaluate` endpoint will be introduced in later phases with the specification's DTOs, authentication, tenant isolation, and idempotency rules.
