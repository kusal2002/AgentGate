# Slack integration (planned for Phase 7)

Phase 1 does not create a Slack app or send messages. The root `.env.example` reserves `SLACK_BOT_TOKEN`, `SLACK_SIGNING_SECRET`, `SLACK_CLIENT_ID`, and `SLACK_CLIENT_SECRET` for later use.

Before implementing Slack, finish agent authentication, deterministic policies, persisted approvals, and reviewer permissions. Then configure a Slack app with bot messaging and interactivity, and set its callback URL to the future `POST /api/integrations/slack/actions` route through a secure HTTPS tunnel in development.

That route is not implemented yet. Its eventual implementation must verify signatures and replay windows, load opaque approval identifiers from the database, map Slack users to authorized organization reviewers, resolve pending requests transactionally, and append audit events. Buttons must not contain action payloads.
