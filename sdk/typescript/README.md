# @agentgate/sdk

Phase 10 server-side TypeScript SDK. Requires Node.js 22.13 or newer. Includes ESM imports, native Node CommonJS `require`, declarations, and no runtime dependencies. The package is private and is **not published to npm**.

Build and install from this repository:

```powershell
npm --prefix sdk/typescript ci
npm --prefix sdk/typescript run build
# From your agent application's directory, using your repository's actual path:
npm install D:/Projects/AgentGate/sdk/typescript
```

Rebuild the SDK after source changes. For a portable installation, run `npm pack` inside `sdk/typescript` and install the resulting tarball in your application. Do not commit that tarball.

```typescript
import { AgentGate } from '@agentgate/sdk';

const gate = new AgentGate({
  apiKey: process.env.AGENTGATE_API_KEY!,
  baseUrl: process.env.AGENTGATE_BASE_URL ?? 'http://localhost:5000',
});

const result = await gate.evaluate({
  action: 'refund',
  resource: { type: 'customer', id: 'CUS-102' },
  parameters: { amount: 750, currency: 'USD' },
  idempotencyKey: 'refund-order-102-attempt-1',
});

if (result.decision === 'review') {
  const approval = await gate.waitForApproval(result.approvalId, {
    timeoutMs: 600_000,
    pollIntervalMs: 2000,
  });
  console.log({ actionId: result.actionId, approvalStatus: approval.status });
} else {
  console.log({ actionId: result.actionId, decision: result.decision, status: result.status });
}
```

This example requests authorization and prints the outcome. It does not execute a refund. Keep keys in a server process environment; never put them in frontend code or `VITE_*` variables. Use HTTPS except for loopback local development. Redirects are rejected to prevent forwarding credentials to another endpoint. A custom `fetch` is trusted application code and must honor these settings.

## Methods

- `evaluate(request, { signal?, timeoutMs? })`: records/evaluates an action. Supply a stable idempotency key for the same logical request. Retries reuse the identical serialized payload. Reusing a key with different parameters produces HTTP 409.
- `getApproval(approvalId, { signal?, timeoutMs? })`: reads an approval owned by this agent. Foreign agent/organization approvals return HTTP 404.
- `waitForApproval(approvalId, { signal?, timeoutMs?, pollIntervalMs? })`: immediately reads, then polls sequentially until the server returns `approved`, `rejected`, `expired`, or `cancelled`. Pending is never returned as a resolved result. Server time determines expiry; local timestamps are informational.

Polling defaults to two seconds and a ten-minute total deadline. The wait deadline includes network attempts and sleeps; each read also uses `requestTimeoutMs`. Timeout or cancellation stops waiting without changing the server decision. Save the approval ID so you can resume waiting. `approved` authorizes the request; it does not prove execution occurred. Never execute after denial, rejection, expiry, cancellation, or a client error. Check current action state before handling a replay: an already executed action must not run again. External side effects require their own idempotency mechanism.

## Configuration and errors

| Option | Default | Accepted range |
| --- | --- | --- |
| `requestTimeoutMs` | 10,000 | 1–120,000 ms |
| `maxRetries` | 2 | 0–5 |
| `retryDelayMs` | 500 | 1–10,000 ms |
| `maxRetryDelayMs` | 30,000 | 1–120,000 ms |
| Wait `timeoutMs` | 600,000 | 1–604,800,000 ms |
| `pollIntervalMs` | 2,000 | 100–60,000 ms |

Request timeouts cover all attempts, backoff, and response reads. Transport failures and HTTP 408/429/500/502/503/504 retry with bounded exponential delays. Valid `Retry-After` seconds or HTTP dates are honored; if the requested delay exceeds the configured maximum, the HTTP error is returned. Other HTTP failures, malformed responses, and interrupted response bodies are not retried.

Exports include `AgentGateError`, `AgentGateValidationError`, `AgentGateHttpError` (`statusCode`, `retryAfterMs`), `AgentGateNetworkError`, `AgentGateProtocolError`, `AgentGateTimeoutError`, and `AgentGateAbortError`. SDK error messages omit response bodies, request data, URLs, credentials, and underlying transport causes. Handle these errors as a failure to obtain authorization. Responses are validated against the implemented API; incompatible/test-only responses fail closed.

Parameters/context accept plain JSON objects, finite numbers, and up to 16 nesting levels. Each object is limited to 32 KiB and the full request to 64 KiB. The API remains authoritative for action-specific validation and decimal ranges.

## Verification

```powershell
npm --prefix sdk/typescript test
npm --prefix sdk/typescript run typecheck
npm --prefix sdk/typescript run lint
npm --prefix sdk/typescript run test:package
```

See the repository's `docs/sdk.md` for the isolated PostgreSQL/API runner and manual approval checks. `run({ execute })`, automatic action execution, publishing, and the demo agent remain outside this phase.
