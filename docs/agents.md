# Phase 3: agent identity and API keys

Phase 3 adds registered agents, editable metadata, disabling, hashed API keys, expiry/revocation, and API-key authentication. Persisted actions, idempotency, policies, approvals, and audit events remain later phases.

## Local setup

Use your existing PostgreSQL and ignored `.env`; Docker is optional. Apply the `AgentIdentityAndApiKeys` migration and start the API:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

Run the dashboard with `npm --prefix dashboard run dev`, sign in, and open **Agents**. Owner, Admin, and Developer members can register an agent, edit its name/description/version, disable it, generate keys, and revoke keys. Reviewer and Viewer members can view agents and key metadata.

Create a separate agent for each environment: Development, Staging, or Production. Environment is fixed after registration; changing the agent name retains its stable ID and slug. A disabled agent remains visible and editable, but it cannot authenticate or receive new keys. Managers can enable it again; valid keys resume working, while revoked or expired keys remain invalid. Deletion is not implemented in this phase.

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
| POST | `/api/agents/{id}/enable` | Resume agent authentication for valid keys |
| GET | `/api/agents/{id}/keys` | List safe key metadata and prefixes |
| POST | `/api/agents/{id}/keys` | Generate `{ name, expiresAt?, expiryPreset? }`; returns `{ key, apiKey }` once |
| POST | `/api/agents/{id}/keys/{keyId}/revoke` | Revoke a key; repeated revocation is idempotent |

All management routes require `Authorization: Bearer <user JWT>`. Mutation routes enforce Owner/Admin/Developer permissions in both API authorization and application services. Queries constrain organization and agent/key IDs. Cross-tenant identifiers return 404. Invalid input returns 400; insufficient roles return 403; disabled key generation and conflicting metadata updates return 409.

The dashboard offers 1 week, 1 month (default), 6 months, no expiry, and a custom date. Presets use the server's UTC time at generation: `oneWeek` adds 7 days, `oneMonth` and `sixMonths` add calendar months, and `never` has no scheduled expiry. Month-end dates clamp to the last day of the target month. Send either `expiryPreset` or a future `expiresAt` for a custom date, not both. Omitting both retains the API's no-expiry behavior for existing clients. Custom dates are normalized to UTC.

Revocation and agent disable/enable take effect on subsequent authentication requests without restarting the API. Composite foreign keys enforce the agent/organization/environment relationship, unique constraints enforce lookup prefixes and tenant slugs, and PostgreSQL row versions protect concurrent agent metadata updates.

## Agent identity API (agent key)

`GET /v1/agents/me` proves the key can authenticate. It returns the server-derived agent ID, organization ID, key ID, name, environment, and version. Request headers or bodies cannot override the organization.

```powershell
# Set AGENTGATE_API_KEY in this terminal to the key you saved from the dashboard.
Invoke-RestMethod http://localhost:5000/v1/agents/me -Headers @{
    Authorization = "Bearer $env:AGENTGATE_API_KEY"
}
```

Phase 4 replaced the `amountMinor` prototype with the persisted action API. Agent keys can submit requests and retrieve their own outcomes at `/v1/actions/*`; dashboard JWTs can read organization history. See [actions.md](actions.md) for the request contract, idempotency, and temporary Development test evaluation boundaries.

## Verification

`./scripts/test-backend.ps1` runs the current backend suite in isolated PostgreSQL databases (140 tests after Phase 5). `-UnitOnly` runs role, key-format, and action payload tests without a database. `npm --prefix dashboard run build` and `npm --prefix dashboard run lint` verify the frontend.

Coverage includes all five roles, tenant isolation across agents and keys, environment prefixes, random key generation, one-time responses and hash-only storage, immutable environments, server-derived identity, JWT/key scheme separation, tampered/malformed/unknown keys, revocation, expiry, agent disable/enable, organization suspension, last-use tracking, input validation, action test evaluation boundaries, and rate limiting before invalid-key authentication.

Edge browser checks passed agent creation/details/edit, key display/dismissal/reload, authentication, key revocation, disabling, Viewer restrictions, desktop/mobile layout, no horizontal overflow, empty credential browser storage, and no JavaScript runtime errors. Browser data was created in an isolated database and removed after testing.
