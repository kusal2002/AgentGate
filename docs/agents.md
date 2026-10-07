# Phase 3: agent identity and API keys

Phase 3 adds registered agents, editable metadata, disabling, hashed API keys, expiry/revocation, and API-key authentication. Persisted actions, idempotency, policies, approvals, and audit events remain later phases.

## Local setup

Use your existing PostgreSQL and ignored `.env`; Docker is optional. Apply the `AgentIdentityAndApiKeys` migration and start the API:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

Run the dashboard with `npm --prefix dashboard run dev`, sign in, and open **Agents**. Owner, Admin, and Developer members can register an agent, edit its name/description/version, disable it, generate keys, and revoke keys. Reviewer and Viewer members can view agents and key metadata.

Create a separate agent for each environment: Development, Staging, or Production. Environment is fixed after registration; changing the agent name retains its stable ID and slug. A disabled agent remains visible and editable, but it cannot authenticate or receive new keys. Re-enabling and deletion are not implemented in this phase.

## API keys

Development and Staging keys begin with `ag_test_`; Production keys begin with `ag_live_`. Each contains 32 cryptographically random bytes encoded as 64 lowercase hexadecimal characters. Keys are scoped to the agent, organization, and environment that issued them.

The full key appears only in the generation response and the dashboard's temporary display. Save it securely in your agent's environment. The browser does not store keys in local/session storage or TanStack Query caches; dismissal, navigation, reload, or organization changes clear the temporary display. API responses containing full keys set `Cache-Control: no-store`.

PostgreSQL stores a lookup prefix and SHA-256 hash, never the full key. Authentication uses a constant-time hash comparison, then rechecks revocation, expiration, agent status, environment, and organization status before recording `LastUsedAt`. Last activity is the most recent successful API-key authentication, not an action execution timestamp.

Revoked/expired keys, disabled agents, suspended organizations, malformed keys, and unknown keys return 401. Failed authentication does not record key use. Agent identity endpoints are limited to 120 requests per minute per client IP, including invalid-key attempts; this limiter runs before authentication. Database errors fail closed.

Human JWT authentication and agent API-key authentication are separate schemes. Agent keys cannot access organization/dashboard management endpoints. User JWTs cannot access agent identity endpoints.

## Management API (human JWT)

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/agents` | List the current organization's agents and last activity |
| POST | `/api/agents` | Register `{ name, environment, version, description? }` |
| GET | `/api/agents/{id}` | Retrieve tenant-scoped agent details |
| PATCH | `/api/agents/{id}` | Update `{ name, version, description? }`; environment is immutable |
| POST | `/api/agents/{id}/disable` | Disable agent authentication |
| GET | `/api/agents/{id}/keys` | List safe key metadata and prefixes |
| POST | `/api/agents/{id}/keys` | Generate `{ name, expiresAt? }`; returns `{ key, apiKey }` once |
| POST | `/api/agents/{id}/keys/{keyId}/revoke` | Revoke a key; repeated revocation is idempotent |

All management routes require `Authorization: Bearer <user JWT>`. Mutation routes enforce Owner/Admin/Developer permissions in both API authorization and application services. Queries constrain organization and agent/key IDs. Cross-tenant identifiers return 404. Invalid input returns 400; insufficient roles return 403; disabled key generation and conflicting metadata updates return 409.

An optional expiry must be in the future and is normalized to UTC. Omit it for a key without scheduled expiry. Revocation and agent disable take effect on subsequent authentication requests without restarting the API. Composite foreign keys enforce the agent/organization/environment relationship, unique constraints enforce lookup prefixes and tenant slugs, and PostgreSQL row versions protect concurrent agent metadata updates.

## Agent identity API (agent key)

`GET /v1/agents/me` proves the key can authenticate. It returns the server-derived agent ID, organization ID, key ID, name, environment, and version. Request headers or bodies cannot override the organization.

```powershell
# Set AGENTGATE_API_KEY in this terminal to the key you saved from the dashboard.
Invoke-RestMethod http://localhost:5000/v1/agents/me -Headers @{
    Authorization = "Bearer $env:AGENTGATE_API_KEY"
}
```

The existing Development-only `POST /v1/actions/evaluate` prototype now also requires an active **Development agent** key. It still accepts integer `amountMinor`, uses the original demo thresholds, and has no action persistence, approval creation, or idempotency. Staging/Production agent keys receive 403 from this local prototype; the route is absent outside a Development host. It must not be used as the production authorization gateway. Phase 4 will introduce the real persisted action API.

## Verification

`./scripts/test-backend.ps1` runs all 44 tests in isolated PostgreSQL databases. `-UnitOnly` runs role and key-format tests without a database. `npm --prefix dashboard run build` and `npm --prefix dashboard run lint` verify the frontend.

Coverage includes all five roles, tenant isolation across agents and keys, environment prefixes, random key generation, one-time responses and hash-only storage, immutable environments, server-derived identity, JWT/key scheme separation, tampered/malformed/unknown keys, revocation, expiry, agent disable, organization suspension, last-use tracking, input validation, the authenticated prototype boundary, and rate limiting before invalid-key authentication.

Edge browser checks passed agent creation/details/edit, key display/dismissal/reload, authentication, key revocation, disabling, Viewer restrictions, desktop/mobile layout, no horizontal overflow, empty credential browser storage, and no JavaScript runtime errors. Browser data was created in an isolated database and removed after testing.
