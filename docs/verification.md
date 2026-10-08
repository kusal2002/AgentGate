# Phase 1 verification

Verified on 2026-10-07 using .NET SDK 10.0.401 and Node.js 24.19.0 on Windows.

| Check | Result |
| --- | --- |
| Backend solution restore/build | Passed; zero warnings and errors |
| EF CLI context discovery | Passed; Npgsql provider and AgentGateDbContext resolved |
| Frontend production build | Passed |
| Frontend source lint with warnings treated as failures | Passed |
| npm dependency audit after removing scaffold-only tooling | Zero reported vulnerabilities |
| GET /health | 200 with expected service JSON |
| GET /health/ready without PostgreSQL | 503 with sanitized unhealthy JSON |
| GET /health/ready with isolated PostgreSQL | 200 with healthy PostgreSQL check |
| Existing Development refund prototype (USD 750) | REVIEW; original Support Manager threshold retained |
| Refund prototype in Production | 404; development endpoint not exposed |
| Dashboard HTML and Vite API proxy | Both returned 200 |
| Edge desktop and mobile rendering | Passed at 1440px and 390px widths |
| Navigation, deep-route reload, status refresh | Passed |
| Browser JavaScript runtime errors | None |
| Mobile horizontal overflow | None |
| Secret/build artifact ignore rules | Confirmed for .env, node_modules, dist, bin/obj, and user project settings |

Database connectivity was verified against a temporary, isolated local PostgreSQL cluster on loopback port 55432. It was shut down after verification. No application schema exists in Phase 1.

Docker Desktop/CLI was unavailable, so Docker Compose startup and its container health check could not be run. Install Docker Desktop, choose `POSTGRES_PASSWORD` in `.env`, and follow the README to verify the container service on port 5432. Phase 1 implementation is complete; container runtime verification remains a local setup step.

Follow-up verification on 2026-10-07: configured the ignored local `.env` for the user's existing PostgreSQL installation, created the `agentgate` database, and verified `/health/ready` returned `healthy` on port 5432. Docker is optional and is not required for this local setup. Credentials remain in `.env` only.

No authentication, persisted action, Slack, approval, SDK, or AI-agent acceptance tests apply yet. These features remain explicitly outside Phase 1.

## Phase 2 verification (2026-10-07)

Backend build and all 22 tests passed, including isolated PostgreSQL HTTP integration tests. Frontend production build and lint with warnings treated as failures passed. The account migration was applied to the user's local `agentgate` database without Docker. A random JWT signing secret was configured in the ignored `.env`.

Isolated Edge browser verification passed registration, login, sign out, refresh on reload, organization rename/create/switch, HttpOnly/SameSite cookie checks, empty browser token storage, and desktop/mobile rendering with no horizontal overflow or JavaScript runtime errors. Test databases are removed afterward. See [authentication.md](authentication.md) for the complete behavior and test commands.

## Phase 3 verification (2026-10-07)

All 44 backend tests passed with zero build warnings/errors. Frontend build and lint passed. The `AgentIdentityAndApiKeys` migration was applied to the user's installed PostgreSQL database without Docker.

Agent coverage includes creation/list/detail/edit/disable, tenant isolation, all five roles, one-time key responses and hash-only storage, environment prefixes, malformed/tampered/unknown keys, JWT/key scheme separation, expiry, idempotent revocation, suspended organizations, immutable environments, last-use tracking, Development prototype authentication, and rate limiting before invalid-key authentication.

Isolated Edge browser checks passed agent registration/edit, one-time key display/dismissal/reload, key authentication, revocation, disabling, Viewer restrictions, desktop/mobile layout, empty credential browser storage, and no runtime errors. Temporary browser data and services were removed after checks. See [agents.md](agents.md).

Phase 3 follow-up: added server-calculated expiry presets (1 week, 1 month, 6 months, no expiry), retained custom dates, and added agent re-enabling. All 49 backend tests passed, including preset persistence/validation, enable role/tenant enforcement, resumed valid-key access, and continued rejection of revoked/expired keys after re-enabling. Frontend production build and lint passed. No database migration is required. The new UI controls were checked by build/lint; the earlier browser verification above predates these controls.

## Phase 4 verification (2026-10-07)

All 79 backend tests and the 25-test unit-only subset passed with zero build warnings/errors. Coverage includes persisted action submission/retrieval, identical/concurrent/conflicting retries, canonical payload hashing, persistence across new hosts, tenant and agent isolation, authentication scheme separation, all five history roles, validation/size/depth/precision rules, pagination/filtering, Development test allow, non-Development deny, and blocking replay of test results in Production. See [actions.md](actions.md) for test commands and the manual checklist.

Frontend production build and lint passed. The `20261007064441_PersistedAgentActions` migration was applied to the user's installed PostgreSQL without Docker. Existing data was preserved.

Isolated Edge browser verification passed action list/filter/detail/reload, recent history per agent, one row on retry, 409 on changed payload, 413 for an oversized HTTP request, credential field display masking, desktop/mobile layout without horizontal overflow, empty browser credential storage, and no JavaScript runtime errors. Screenshots contain only synthetic data and masked credential fields. Browser services and their isolated database were removed after testing. Policy evaluation, approvals, external execution, and audit events remain future work.

Windows follow-up: the test launcher now sends build outputs to ignored `.local-verification/backend-tests`, avoiding DLL locks from the running API. All 79 backend tests and 25 unit-only tests passed while the original API process stayed running; `/health/ready` remained healthy.

## Phase 5 verification (2026-10-07)

The deterministic policy engine replaces test allow. All 140 backend tests and 69 unit-only tests passed with zero build warnings/errors. Coverage includes the ten operators, typed/nested matching, missing/null fields, refund boundaries, priority/tie ordering, corruption denial, configurable Review/Deny defaults, role/tenant isolation, concurrent edits/seeding, preview/persisted agreement, production rules, retained historical outcome snapshots, and blocked legacy test allows. Frontend build and lint passed.

The `20261007104059_DeterministicPolicies` migration was applied to the user's installed PostgreSQL without Docker, preserving existing records. No policies or demo credentials were automatically added to the user's organization.

Isolated Edge checks passed all four refund previews, policy creation/edit, enable/disable, action policy snapshots/reload, Developer/Viewer UI restrictions, mobile builder/preview without overflow, empty credential browser storage, and no JavaScript runtime errors. Temporary services and browser database were removed. Approval creation/resolution and external execution remain outside Phase 5. See [policies.md](policies.md) for manual checks.

## Phase 6 verification (2026-10-07)

All 175 backend tests and the 82-test unit-only subset passed. Frontend production build and lint passed. Approval coverage includes atomic action/request insertion and rollback, concurrent idempotent submissions, competing approve/reject transitions, deadline races, reviewer hierarchy, tenant/agent/authentication isolation, immutable decision records, restart persistence, configurable timeout, migration backfill, and global expiry maintenance. Concurrent demo-policy seeding now serializes on the organization row to avoid insert-order deadlocks.

The `20261007120412_HumanApprovals` migration was applied to the user's installed PostgreSQL. Existing unresolved non-test review actions receive pending approvals with a fresh 24-hour deadline.

Isolated Edge checks passed pending list/details, masked payloads, Viewer restrictions, Reviewer approval with persisted name/comment, Owner rejection, expired/cancelled outcomes, agent polling, status filters, action-to-approval navigation, and desktop/mobile layout without overflow. Browser credential storage stayed empty and no JavaScript runtime errors occurred. Temporary services and their isolated database were removed. No external action executes; Slack, audit events, and SDK integration remain later phases. See [approvals.md](approvals.md) for the manual checklist.

## Phase 7 verification (2026-10-07)

All 207 backend tests and 96 database-free tests passed, with zero build warnings/errors. Frontend production build and lint passed. Coverage includes the official Slack signature vector, raw-body tampering/replay windows, forged app/workspace/channel/message references, role changes and mappings, competing Slack/dashboard decisions, expiry, delivery retries/concurrent workers, message updates, immutable source provenance, deduplicated private feedback, safe content, and Slack API rate limits. Integration tests use isolated PostgreSQL and fake Slack clients/HTTP handlers.

The SlackIntegration and SlackInteractionFeedback migrations were applied explicitly to the installed local PostgreSQL, preserving existing data. No Slack installation was enabled automatically.

Isolated Edge browser checks passed channel enable/disable, mapping/removal, reload persistence, validation feedback, Viewer restrictions, unavailable organization state, desktop/mobile layout without overflow, empty credential browser storage, and no JavaScript runtime errors. Browser checks used a fake Slack client; services and the temporary database were removed. Live workspace posting/clicks and the HTTPS tunnel remain unverified until local app configuration is supplied. See [slack-setup.md](slack-setup.md).

Phase 7 follow-up (2026-10-08): live read-only Slack requests reproduced a valid member returning user_not_found with JSON POST, while GET users.info?user=… returned the correct workspace member. The client now uses documented GET query parameters with Bearer authorization. A regression test checks the actual HTTP method, URL, and absence of a request body. All 208 backend tests and the 97-test database-free subset passed. Backend build passed without warnings/errors. No database migration or frontend change is required. The callback 404 was reported while ngrok was not running; keep the tunnel open and save its current backend callback URL in Slack.

## Phase 8 verification (2026-10-08)

All 226 backend tests passed against isolated databases on the installed PostgreSQL. The database-free subset passed all 106 cases. Solution build passed with zero warnings/errors, and EF reported no pending model changes. Frontend production build and lint passed.

New coverage verifies ordered request/policy/outcome/approval timelines, human actor identity, Slack provenance and delivery/update events, omitted payload/comment/credential fields, nested credential and financial redaction, concurrent idempotent retries, single-winner resolution, deadline expiration, tenant isolation, authentication, read-only routes, filters, pagination, management events, repeated key revocation, allow/deny outcomes, and migration snapshots across host restart. EF mutation guards and database update/delete/truncate triggers reject edits. A synthetic audit insert failure rolls back both action creation and human approval resolution. Existing Phase 5-to-6 backfill still passes through all migrations.

Isolated Edge verification passed the audit list, next/previous pages, agent/event/actor filters, filter persistence after reload, empty results and clearing filters, event details, action and approval timelines, live timeline refresh after a dashboard decision, and desktop/mobile layout without horizontal overflow. The new cards have accessible headings and filter controls have explicit labels. Browser credential storage stayed empty and no JavaScript runtime errors occurred. Screenshots and synthetic test data are excluded from commits. Slack was disabled in browser verification; backend Slack tests used a fake client, and the general test factory now clears live Slack environment configuration.

The audit migration was also applied explicitly to the user's local database. Existing actions receive clearly marked imported snapshots; earlier detailed audit events are not reconstructed. See [audit.md](audit.md) for manual verification, metadata boundaries, and direct connection IP behavior. Approved actions are still not executed. Dashboard completion, SDK integration, and demo execution remain later phases.

## Phase 9 verification (2026-10-08)

All 236 backend tests passed against isolated PostgreSQL databases. New coverage verifies empty statistics/null averages, original policy decision counts, current approval outcomes, human decision latency excluding expirations, overdue approvals beyond a maintenance batch, per-agent separation, exclusion of legacy test results and idempotent replays, ten-row ordered recent activity, tenant isolation, rejected agent authentication, and Viewer/Reviewer/Developer read access. Expanded audit filters, exact action/agent/approval/event/customer identifier searches, invalid inputs, literal wildcard/injection-like searches, friendly names, scoped options, and unchanged original metadata passed.

The solution build passed with zero warnings/errors using isolated build outputs so the user's running API could stay open. Frontend production build and lint passed. Phase 9 changes no entities or persistence model and needs no new migration.

Isolated Edge verification passed empty/live overview statistics, the pending approval shortcut, retained last-successful statistics during a simulated server outage and successful retry, per-agent totals with zero executed actions after approval, combined audit action/decision/risk/reviewer filters, exact identifier/customer search, reload persistence, empty/clear states, actor/reviewer names, not-found navigation, and organization switching. All primary workspace pages passed mobile page-width checks; a previously overflowing agent-detail grid was constrained so tables scroll within their cards. Desktop/mobile screenshots were reviewed, browser credential storage stayed empty, and no JavaScript runtime errors occurred. Verification used synthetic data with Slack disabled; temporary API/Vite processes and their isolated database were removed. Local screenshots and harnesses are excluded from commits.

Old placeholder routing and outdated policy text about future approvals were removed. See [dashboard.md](dashboard.md) for restart instructions, metric definitions, and manual checks. SDK and demo execution remain Phases 10 and 11.
