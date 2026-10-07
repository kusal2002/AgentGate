# Phase 6: human approvals

Review actions now create a pending approval in the same database transaction as the action. Human decisions update the approval and action together and append one immutable decision record. Agent keys can poll the outcome. No external action is executed by approval, Slack notifications and decisions are available in Phase 7; see [slack-setup.md](slack-setup.md).

## Local setup

Use your installed PostgreSQL and ignored `.env`. Stop the normal API before rebuilding/migrating on Windows:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

The `20261007120412_HumanApprovals` migration creates approval/decision tables, tenant-scoped foreign keys, a unique approval per action, a unique human decision per approval, expiry indexes, and a database trigger rejecting decision edits/deletes. Existing Phase 5 non-test actions still awaiting approval receive pending approvals with a fresh 24-hour deadline at migration time. Historical allows/denials and Phase 4 test results are preserved.

## Manual checklist

1. Sign in as Owner or Admin at `http://localhost:5173`. Confirm the demo refund policies exist under **Policies**, or add them using the Development sample button. Use a Development agent and its active API key.
2. Submit a $750 USD refund with a new idempotency key:

```powershell
$env:AGENTGATE_API_KEY = Read-Host 'Paste your Development agent API key'
$headers = @{ Authorization = "Bearer $env:AGENTGATE_API_KEY" }
$body = @{
    action = 'refund'
    resource = @{ type = 'customer'; id = 'CUS-102' }
    parameters = @{ amount = 750; currency = 'USD'; reason = 'duplicate payment' }
    context = @{ customerTier = 'business' }
    idempotencyKey = 'phase6-refund-' + [Guid]::NewGuid().ToString('N')
} | ConvertTo-Json -Depth 10
$result = Invoke-RestMethod http://localhost:5000/v1/actions/evaluate `
    -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$result
```

Expect `decision: review`, `status: awaiting_approval`, `approvalId`, `approvalStatus: pending`, and `approvalExpiresAt`. With the demo rules installed, risk is Medium and reviewer is Reviewer. Without a matching rule, Development defaults still create a review approval with High risk.

3. Open **Approvals → Pending → View request**. Check the action, resource, amount/parameters, policy snapshot, reviewer role, deadline, and **Not executed** state.
4. Enter a comment such as “Verified duplicate charge.” and click **Approve**. Confirm status approved, your recorded comment/name/time, and the absence of further decision buttons. Reload to verify persistence.
5. Poll using the agent key:

```powershell
Invoke-RestMethod "http://localhost:5000/v1/approvals/$($result.approvalId)" -Headers $headers
Invoke-RestMethod "http://localhost:5000/v1/actions/$($result.actionId)" -Headers $headers
```

Expect approval status approved and action status approved. The original policy decision remains review; the human outcome is represented by status. Approval permits the caller to continue, but AgentGate has not executed the refund.

6. Submit another request with a new idempotency key, then **Reject** it with a comment. Approval/action statuses become rejected. Check the Approved and Rejected filters.
7. Retry the original unchanged `$body`. It must return the same action and approval IDs and their current outcome. No second approval or decision is created. A changed payload under the same key still returns 409.
8. Check a Reviewer account can resolve Reviewer requests. Developer and Viewer accounts can read details, but see no Approve/Reject controls. An Admin cannot resolve a request requiring Owner. Cross-organization IDs are inaccessible.
9. Submit $50 and $15000 refunds with the demo policies: allow/deny results have no approval ID and create no approval request.

## Check expiry

The default timeout is 1440 minutes (24 hours). For a short local check, set this in ignored `.env`, stop/restart the API, then submit a **new** review request:

```dotenv
Approvals__TimeoutMinutes=1
```

Wait until its `approvalExpiresAt` passes, then refresh/read it. Expect approval expired, action cancelled, and no human decision record. Approve/Reject attempts return 409. Restore 1440 and restart afterward. Existing approvals retain the deadline assigned when they were created; changing the setting only affects new requests. Backfilled Phase 5 approvals use the migration's fixed 24-hour deadline.

Valid configuration is 1–10080 minutes. Invalid settings fail startup. A worker processes up to 100 overdue requests every 30 seconds, and reads refresh expiry for their tenant/target. A blocked maintenance batch can be retried later; the decision transaction always checks the database clock after locking the approval, so an expired request cannot be approved even if background processing is delayed.

## Reviewer permissions

All five organization roles can view their organization's approvals and decision history. Mutations require current membership and the snapshotted reviewer role:

| Actual role | Reviewer requests | Admin requests | Owner requests |
| --- | --- | --- | --- |
| Owner | Yes | Yes | Yes |
| Admin | Yes | Yes | No |
| Reviewer | Yes | No | No |
| Developer / Viewer | No | No | No |

JWT validation and the decision transaction use current membership rather than client-supplied role/user IDs. The required role comes from the original policy result and does not change when a policy is edited. Agent keys cannot resolve approvals.

## APIs

| Authentication | Method | Route | Behavior |
| --- | --- | --- | --- |
| Human JWT | GET | `/api/approvals?status=pending&page=1&pageSize=25` | Tenant-scoped list; omit status for all |
| Human JWT | GET | `/api/approvals/{id}` | Action payload, policy snapshot, eligibility, and decisions |
| Human JWT | POST | `/api/approvals/{id}/approve` | Resolve with `{ "comment": "..." }` |
| Human JWT | POST | `/api/approvals/{id}/reject` | Resolve with `{ "comment": "..." }` |
| Human JWT | POST | `/v1/approvals/{id}/approve` or `/reject` | Specification-compatible aliases using human JWTs |
| Agent key | GET | `/v1/approvals/{id}` | Poll only this agent's own approval outcome |

Valid status filters are pending, approved, rejected, expired, and cancelled. Page size is 1–100; page is 1–1000000. Cancellation is reserved for later workflows; expiry currently marks the action cancelled and the approval expired. The dashboard has Pending/Approved/Rejected/Expired/All filters, pagination, automatic refresh, comments, and saved request/decision times.

Comments are optional strings, trimmed and limited to 2000 characters. Unknown mutation fields such as reviewerUserId or approval status return 400. Human decision bodies are limited to 8 KiB. Other organizations' IDs and other agents' approval IDs return 404. Insufficient reviewer roles return 403; terminal, expired, or incompatible action states return 409. Details/polling responses are no-store; agent polling uses the existing pre-authentication rate limit.

## Transaction and history guarantees

- Action and approval creation commit together; a failed approval insert rolls back the action. Idempotency is enforced by the original tenant/agent/key index and a unique approval/action relationship.
- Approve, reject, and expiry lock the same approval row. Exactly one transition can win; losing human decisions return 409. Approval status, action status, actor/comment, and the decision record commit together.
- Human decisions are append-only in EF and PostgreSQL. No mutation/deletion API exists; a database trigger also blocks direct row edits/deletes. Expiry records its time without fabricating a human decision.
- Approved/rejected/expired requests cannot be resolved again. Denied actions cannot be overridden through this workflow. Legacy test allows remain blocked.
- Polling, decisions, and outstanding requests survive restart. Policy snapshots and original decision stay unchanged; action and approval status track the human outcome.

These guarantees prevent duplicate request/decision records. Exactly-once external execution and full append-only audit events remain later work. Displayed request history is derived from stored timestamps/decisions, not a complete Phase 8 audit timeline. Payloads remain tenant-private; common credential fields are masked in the UI, and payload/comment values are not logged.

## Automated checks

```powershell
./scripts/test-backend.ps1
./scripts/test-backend.ps1 -UnitOnly
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

The full suite has 207 tests and unit-only has 96. Coverage includes approval/rejection aliases, role hierarchy, tenant/agent/scheme separation, concurrent retries, mixed decisions, expiry races and maintenance, transaction rollback, restart/configuration, migration backfill, append-only protection, comments/input validation, filtering, and pagination. Integration tests create/drop isolated PostgreSQL databases.
