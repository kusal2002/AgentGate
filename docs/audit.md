# Phase 8 — Audit system

Audit log records authorization activity for the signed-in organization. All organization roles can read it; agent API keys cannot access the human audit endpoints. There are no edit, delete, or client-supplied event endpoints.

## Start locally

Stop the backend before applying a migration, then run these commands from the repository root:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

In a separate terminal, start the dashboard:

```powershell
npm --prefix dashboard run dev
```

Your installed PostgreSQL is sufficient. No additional environment variables or Docker services are required. Keep your existing Slack configuration and tunnel running when testing Slack decisions.

## Check this phase

1. Sign in and open **Audit log**. Existing actions should have `action.imported` events. These are snapshots taken when the migration ran, with original request time and observed status in metadata. They do not reconstruct earlier policy executions, Slack messages, or reviewers.
2. Submit a **new** action through your agent using a new idempotency key. For a request requiring review, open its action details. **Audit timeline** should show `agent.action_requested`, `policy.evaluated`, `action.review_required`, and `approval.created`, in that order.
3. Repeat the same request and key. No second action or second set of events should appear. Reusing the key with a changed payload still returns 409.
4. Approve or reject the new request. The timeline gains one `approval.approved` or `approval.rejected` event. Dashboard decisions show actor **User**; Slack decisions show **Slack**, with the mapped AgentGate user ID. The original decision stays recorded after repeated button clicks.
5. With Slack configured, confirm `approval.slack_sent` appears after the message is successfully sent and `approval.slack_updated` after its status is updated. Failed sends do not claim successful delivery.
6. Use the **Agent**, **Event**, **Actor**, **From**, and **Through** filters. Dates use UTC; displayed timestamps use your browser’s local time. Clear filters, paginate, refresh, and open **Event details** or an event’s detail page.
7. Test a policy allowing a small refund and denying a large refund. Their timelines should contain `action.allowed` and `action.denied`, with no human approval event. Policy preview remains a dry run and creates no action or audit events.
8. Disable and enable an agent, create or revoke a key, and create or update a policy. Corresponding management events should appear. Repeated key revocation adds no extra event. Key values and hashes are never included.
9. Switch organizations. You should see only the new organization’s events; another organization’s event, action timeline, or approval timeline returns 404.

You can also run the automated checks:

```powershell
./scripts/test-backend.ps1
./scripts/test-backend.ps1 -UnitOnly
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

## API

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/audit` | Paginated events, newest first |
| GET | `/api/audit/{id}` | Single event with redacted metadata |
| GET | `/api/actions/{id}/timeline` | Action events, oldest first |
| GET | `/api/approvals/{id}/timeline` | The approval’s full action timeline, oldest first |

List filters: `agentId`, `actionId`, `approvalRequestId`, `eventType`, `actorType`, `from`, `to`, `page`, `pageSize`. Actor types are `Agent`, `User`, `Policy`, `System`, and `Slack`. Date bounds are inclusive ISO timestamps with offsets. Page defaults to 1 and size to 25; size is limited to 100. Timeline endpoints accept `page` and `pageSize`. Every result is scoped to the authenticated organization and uses `no-store` responses.

## Integrity and boundaries

Action/approval creation, human resolution, expiration, agent/key changes, and policy changes save their events in the same transaction as the state transition. A failed audit insert rolls back that transition. Idempotent requests reuse the original events. Same-transition events share a timestamp and use an explicit ordering field; IDs break remaining ties. No mutable names are used as actor identity.

Both EF persistence guards and PostgreSQL triggers reject updates and deletes; a database trigger also rejects truncation. Tenant-scoped composite foreign keys prevent cross-organization references. Database administrators who can alter schema or disable triggers still control the database; this is not an externally anchored cryptographic ledger. Downgrading the migration deliberately drops the audit table and its history.

Event metadata is an allowlist of identifiers, decisions, statuses, reviewer roles, deadlines, and policy version times. Arbitrary payloads, policy condition values, descriptions, comments, credentials, token hashes, and key prefixes are omitted. `ISensitiveDataRedactor` recursively masks credential and financial field names before audit metadata is persisted. Existing action payload storage and approval comments retain their earlier behavior; the audit log does not copy them.

`IPAddress` is the direct HTTP connection address, not a trusted client address behind ngrok. Background events have no source IP. A policy default has a **Policy** actor with no policy ID. Slack decisions use the mapped AgentGate user ID; Slack delivery events use **System**.

Slack delivery remains a durable, retryable external operation. Its audit event is committed with the delivery state after a successful API response; a process or database failure after the remote send can require retrying it. Phase 8 does not provide exactly-once external execution or execute approved actions. SDK resume/execution events, dashboard completion, and demo execution belong to later phases.
