# Phase 7: Slack approvals

AgentGate sends pending approvals to Slack, handles signed Approve/Reject interactions, and updates messages after Slack/dashboard decisions or expiry. Approval never executes a refund.

This MVP uses your manually installed app, bound to **one Slack workspace and one AgentGate organization per deployment**. Other organizations retain dashboard approvals. Multi-workspace OAuth is future work; `SLACK_CLIENT_ID` and `SLACK_CLIENT_SECRET` are unused.

## Configure your existing app

1. Open your app at [Slack app management](https://api.slack.com/apps). Under **OAuth & Permissions**, add bot scopes **chat:write** and **users:read**, then install/reinstall it. Copy the **Bot User OAuth Token**. Email lookup scopes are unnecessary because reviewers are explicitly mapped. See [message posting](https://docs.slack.dev/reference/methods/chat.postMessage/) and [member lookup](https://docs.slack.dev/reference/methods/users.info/).
2. Under **Basic Information**, find the **Signing Secret** and **App ID**. Find your workspace ID (`T…`, for example in the Slack browser URL).
3. In AgentGate, open **Settings → Slack approvals** and copy the organization ID. Add the actual values to the ignored root `.env` locally:

```dotenv
SLACK_BOT_TOKEN=
SLACK_SIGNING_SECRET=
SLACK_APP_ID=
SLACK_TEAM_ID=
SLACK_ORGANIZATION_ID=
```

All five values are required. Tokens/secrets are never accepted or displayed by the dashboard. Existing process environment settings override `.env`.

4. Apply migrations and start/restart the API:

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

Phase 7 adds `SlackIntegration` and `SlackInteractionFeedback`: integration settings, reviewer mappings, durable deliveries/feedback, and immutable decision provenance. Existing decisions default to Dashboard. Migration does not enable an integration.

5. Make the local API on port 5000 reachable through your HTTPS tunnel. Enable **Interactivity & Shortcuts** in Slack and set **Request URL** to:

```text
https://YOUR-HTTPS-HOST/api/integrations/slack/actions
```

Forward the path to the API and preserve raw bodies/signature headers. Update Slack when a temporary tunnel URL changes. No event subscriptions, slash commands, Socket Mode, or OAuth redirect URL is needed. Keep the API and tunnel running during clicks. Slack requires acknowledgment within three seconds; see [interactivity](https://docs.slack.dev/interactivity/handling-user-interaction/).

6. Invite the bot to your approval channel and copy its channel ID (`C…` or `G…`). As Owner/Admin, enter it in **Settings → Slack approvals**, enable, and save. The server checks that the bot token belongs to the configured workspace.
7. Select each reviewer's existing AgentGate membership, enter their Slack member ID (`U…` or `W…`, available from the profile menu), and save. The server verifies an active human in that Slack workspace. One Slack member maps to one account per organization; check that it is the correct person.

Enabling sends outstanding unexpired approvals and new requests. Changing channels applies to new deliveries; already tracked messages stay in their original channel. Disabling stops Slack sends/updates and decisions. Dashboard approvals remain available.

## Live checklist

1. Submit the [Phase 6 request example](approvals.md#manual-checklist), using demo refund policies, a Development agent, USD 750, and a **new idempotency key**.
2. Expect review/awaiting_approval and an approval ID. A Slack message should appear shortly afterward with agent, action, resource, amount, risk, reviewer role, deadline, and buttons. Full parameters/context and reviewer comments are not sent.
3. Click **Approve** as a mapped eligible reviewer. Expect private feedback and a shared message showing approved with buttons removed. Check the dashboard for your saved AgentGate identity, source **Slack**, and time. Agent polling should return approved; the original policy decision remains review and execution stays unset.
4. Submit another request and **Reject** in Slack. Check rejection in Slack, dashboard, and polling.
5. Resolve a request in the dashboard: its Slack message updates too. Use [one-minute expiry](approvals.md#check-expiry) on a new request: Slack becomes expired, the action cancelled, and buttons disappear.
6. An unmapped user or mapped Developer/Viewer cannot resolve requests. Admin cannot resolve an Owner request. Retry a click or race Slack/dashboard: one human decision wins.
7. Restart with a pending request and confirm persistence. Demo USD 50/15000 allow/deny requests create no Slack approval message.

Private feedback uses `chat.postEphemeral`, independently of acknowledgment, and retries for up to five minutes. Callback `response_url` values are ignored. Shared updates are eventual; stale buttons cannot override terminal database state.

## Security and delivery

- HMAC-SHA256 verifies exact raw bytes before MVC parses forms, with constant-time comparison and a five-minute past/future window. Missing/tampered/stale signatures fail. Bodies are limited to 64 KiB. See [signature verification](https://docs.slack.dev/authentication/verifying-requests-from-slack/).
- Buttons contain only an approval UUID. Callbacks must match the configured app/workspace and a recorded approval/channel/message. Stored mappings choose the reviewer; the approval transaction checks active organization, current membership, required role, pending state, and database deadline.
- Immutable decisions record Dashboard/Slack provenance. Full audit events remain Phase 8. Messages use plain text blocks to prevent user-supplied mentions/markup; payloads, comments, tokens, and callback URLs are not logged.
- Durable scans discover up to 100 requests per tick. The worker processes one message and one feedback item per iteration, normally two seconds. Row locks prevent competing workers from processing one tracked item. Failed delivery retries use exponential backoff and Slack Retry-After delays; Settings shows failed deliveries.
- Slack calls do not block action submission or hold approval locks. A network timeout/crash after Slack accepts a post but before its timestamp commits can cause a duplicate message on retry. Stable client message IDs are supplied, but exactly-once remote delivery is not guaranteed. Only the stored message reference can resolve the approval, and one human decision can win.

## Troubleshooting

**Unavailable settings:** check all five variables, exact organization UUID, and restart. A different organization is unavailable on this deployment.

**No message / failed delivery count:** check enabled settings, bot channel membership, `chat:write`, correct token/workspace, and outbound access to `slack.com`. Retries are automatic. Already resolved requests are skipped if never sent.

**Interaction error:** check HTTPS tunnel, Request URL, App ID/workspace/signing secret, and system clock. Rotated secrets reject requests signed with the old secret.

**Permission feedback:** correct the explicit mapping or AgentGate role. The required role is the original policy snapshot. Optional reviewer comments remain available in the dashboard; buttons record “Decision made through Slack.”

## Automated checks

```powershell
./scripts/test-backend.ps1
./scripts/test-backend.ps1 -UnitOnly
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

Tests use isolated PostgreSQL and fake Slack clients/HTTP handlers. They cover signatures, forged callbacks, isolation, role changes, replay/races, expiry, retries, message updates, provenance, private feedback, safe content, and rate limits. They do not send workspace messages. Live installation/tunnel verification requires the setup above.
