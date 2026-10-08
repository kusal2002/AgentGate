# Phase 9 — Dashboard completion

The overview now shows organization activity from PostgreSQL: evaluated requests, auto-allowed requests, requests requiring human review, policy denials, pending approvals, and average human decision time. It also shows active/total agents, enabled policies, and the ten most recent evaluated requests. The header's bell opens the pending approval queue; its number is the current pending count, not an unread-message count.

No new migration, environment variable, Docker service, or Slack installation change is required. Phase 8's audit migration must already be applied.

## Start and check

Restart the API to load the new endpoints. If it is running, use Ctrl+C in its terminal, then run:

```powershell
./scripts/start-backend.ps1
```

Keep the dashboard running, or start it in another terminal:

```powershell
npm --prefix dashboard run dev
```

1. Open **Overview**. Your existing policy-controlled requests should appear in the totals and **Recent activity**. An empty organization shows zero counts, an empty-state guide, and a dash for average approval time.
2. Submit a new request with a new idempotency key. Use a small refund allowed by your policy, one requiring review, and one denied by policy. **Refresh overview** and check the corresponding counts and recent requests. Counts also refresh every 30 seconds.
3. Approve or reject a reviewed request in the dashboard or Slack. Refresh the overview. Pending approvals should decrease; recent activity should show the current status and reviewer. A dashboard decision refreshes the cached overview and agent totals immediately; Slack changes appear on refresh or the next poll.
4. Click the header bell. It should open **Approvals → Pending**. The number represents unexpired approvals waiting for a decision, regardless of your own reviewer eligibility.
5. Open **Agents → your agent**. **Action totals** shows that agent's evaluated, approved, reviewed, policy-denied, pending, and executed counts. Approval alone must not increase **Executed successfully**.
6. Open **Audit log**. Combine **Action**, **Decision**, **Risk**, **Reviewer**, **Agent**, **Event**, **Actor**, and date filters. Reviewer options are people with recorded human decisions in this organization, including Slack decisions.
7. Paste a full action, agent, approval, event, or customer ID into **Search IDs**, then click **Search**. Search uses exact identifiers; it does not search payloads, names, comments, or partial customer IDs. Customer IDs refer to resources whose type is `customer`. Clear filters to reset the search and page. Reloading preserves filters in the URL.
8. Open an event or action timeline. Friendly agent/policy/user names and related action information should be visible. The event detail still exposes the stable actor ID and original metadata. Names are current lookup labels, not historical name snapshots; removed members without a decision record may display only their ID.
9. Switch organizations. Counts, recent activity, filter options, and pending queue should switch together without showing the previous organization's data. Viewer, Reviewer, and Developer members can read activity; existing management permissions still apply.
10. Check the overview, agent details, approvals, policies, actions, settings, and audit page on a narrow screen. Wide tables scroll inside their container. An unknown workspace URL should show **Page not found** with a return link.

If a refresh fails, the overview reports the error and retains the last successful snapshot, instead of replacing it with invented zero counts. **Retry overview** reloads it. Audit and agent-total errors also have retry controls.

## Metric definitions

- **Actions evaluated:** all stored policy-controlled requests in the current organization. Idempotent retries count once; policy previews and legacy prototype test results are excluded.
- **Auto allowed / Human reviews / Denied:** the original policy decision, `allow`, `review`, or `deny`. A reviewed request remains part of Human reviews after approval or rejection. A reviewer rejection is not a policy denial.
- **Pending approvals:** pending requests whose deadline has not passed, checked against the database clock. Due approvals are excluded even when more are waiting than the maintenance batch can expire in one read.
- **Average approval time:** average elapsed seconds from approval request to a recorded human approval or rejection. Dashboard and Slack decisions count; expirations, pending requests, and automatic allows do not. No decisions yields null in the API and a dash in the UI.
- **Approved actions:** actions currently marked Approved or Executed. **Executed successfully** counts only the Executed state. Phase 9 does not execute approved actions or synthesize execution results.

All metrics are all time. Summary counts and recent activity use one repeatable-read database snapshot. Related audit filters use the stored action's original decision/risk and its recorded reviewer; filtering by reviewer returns the related action's events, not just the final decision event. Friendly names and related context are read-only additions; no audit event is updated.

## API additions

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/dashboard` | Organization statistics, agent/policy counts, ten recent requests, and snapshot time |
| GET | `/api/agents/{id}/statistics` | Totals for one agent in the authenticated organization |
| GET | `/api/audit/options` | Recorded action types and historical reviewer IDs/names for this organization |

These routes require a human JWT, allow every organization role to read, use `no-store`, and reject agent API-key authentication. Agent statistics return 404 for an unknown or foreign-tenant agent. Organization IDs supplied as query parameters do not override the authenticated tenant.

`GET /api/audit` additionally accepts `actionType`, `decision`, `riskLevel`, `reviewerId`, and `search`. Existing filters and pagination remain supported. Decision values are allow/review/deny; risks are Low/Medium/High/Critical. Returned events additionally include current friendly names and related action/resource/decision/risk/reviewer context, while retaining original metadata and stable IDs.

## Automated checks

```powershell
./scripts/test-backend.ps1
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

SDK integration and the demo agent remain Phases 10 and 11.
