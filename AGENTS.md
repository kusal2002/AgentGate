# AgentGate development workflow

Implement the approved specification phase by phase. Inspect the current repository before edits and preserve working functionality. Keep domain/application logic separate from HTTP and persistence code. Enforce tenant isolation, secure credentials, deterministic authorization, and transactional state transitions.

## Branches and pull requests

The user requested a new branch, commit, and pull request after each completed phase.

- Create a dedicated branch for each phase, such as `phase-3-agent-identity`, before implementing it. Reuse that phase's existing branch and PR for follow-up fixes.
- Review the full changes, including new files, before committing. Resolve actionable defects and run the relevant backend tests/builds and frontend build/lint checks.
- Keep `.env`, real credentials, generated build files, and local verification artifacts out of commits.
- Commit and push the completed phase, then create a pull request against the repository's default branch. Describe the resulting behavior, migration/setup requirements, validation, and remaining phase boundaries.
- Link the PR in the final response and attach it to the current chat using the Codex artifact tool.
- Do not merge a pull request unless the user asks. Avoid rewriting published history unless explicitly requested.

## Local verification

Use the user's installed PostgreSQL; Docker is optional. `scripts/start-backend.ps1` imports ignored local database and JWT environment settings. Apply migrations explicitly with `-MigrateOnly`. Run `scripts/test-backend.ps1` for isolated PostgreSQL tests, or `-UnitOnly` for role rules without PostgreSQL. Run `npm --prefix dashboard run build` and `npm --prefix dashboard run lint` after frontend changes.
