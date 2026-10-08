# Phase 10: TypeScript SDK

The server-side `@agentgate/sdk` package in `sdk/typescript` exposes `evaluate`, `getApproval`, and `waitForApproval`. See [SDK README](../sdk/typescript/README.md) for types, options, installation, retries, and typed errors. It is a private local package; it is not available from the public npm registry. This phase needs no database migration or dashboard restart.

## Automated checks

Use Node.js 22.13 or newer and your installed PostgreSQL. From the repository root:

```powershell
./scripts/test-sdk.ps1
# Without PostgreSQL:
./scripts/test-sdk.ps1 -UnitOnly
```

The full runner builds the backend into separate output files, creates a uniquely named temporary PostgreSQL database, explicitly applies migrations, starts a hidden API on an available loopback port, and disables Slack credentials. It checks allow/deny/review, idempotency and conflict errors, real approval/rejection, timeout/cancellation, agent/tenant isolation, and disabled-agent authentication. It stops its own API, drops its temporary database, and restores the terminal environment. Your normal database and API on port 5000 stay available. PostgreSQL credentials come from the same ignored `.env` used by the backend scripts; `psql` must be on PATH (the existing local PostgreSQL installation is also detected). Logs remain under ignored `.local-verification` for diagnosis.

The SDK-only checks cover retries, malformed responses, deadlines, cancellation, credential-safe errors, redirects, ESM/CommonJS consumers, and an offline installation of the packed package. They require no API key or running service.

## Check against your running application

1. Keep the backend and dashboard running. In **Agents**, select an enabled Development agent and create/copy an API key. Do not use your account login token.
2. In **Policies**, seed the sample refund policies if they are not already installed. Their amounts determine the examples below; custom policies can produce different outcomes.
3. Prepare a separate PowerShell terminal at the repository root:

```powershell
npm --prefix sdk/typescript ci
npm --prefix sdk/typescript run build
$secureKey = Read-Host 'Paste your agent API key' -AsSecureString
$env:AGENTGATE_API_KEY = [System.Net.NetworkCredential]::new('', $secureKey).Password
$env:AGENTGATE_BASE_URL = 'http://localhost:5000'
node sdk/typescript/examples/check.mjs evaluate
```

4. The example submits a USD 750 refund request and prints its IDs and status. With the sample policies it returns `review` and `awaiting_approval`. Copy the full approval UUID into this environment variable:

```powershell
$env:AGENTGATE_APPROVAL_ID = 'PASTE-THE-APPROVAL-UUID'
node sdk/typescript/examples/check.mjs approval
node sdk/typescript/examples/check.mjs wait
```

5. While the terminal waits, approve the matching request in **Approvals**, or in Slack if your tunnel and integration are running. The terminal should report `approved`. **Actions** and the audit timeline should show the same decision; no refund has been executed.
6. Run `evaluate` again to create a new request, set its new approval ID, run `wait`, and reject it. Expect `rejected`. Ctrl+C stops polling; it does not reject or expire the server request.
7. To check idempotency, set `$env:AGENTGATE_IDEMPOTENCY_KEY = 'sdk-manual-repeat-1'` before evaluating twice. The same payload returns the same action ID. Remove that variable to generate fresh requests again.
8. Clear the key after testing: `Remove-Item Env:AGENTGATE_API_KEY`.

Each example creates an authorization request only. `waitForApproval` returns all server terminal statuses, including expired/cancelled; timeout errors mean you should resume or inspect the approval before proceeding. Never treat a client error as permission. Phase 11 will add the demo agent; SDK `run({ execute })` remains outside the MVP.
