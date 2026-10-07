# Phase 2: organizations and authentication

Phase 2 adds persisted users, organizations, memberships, sessions, registration/login, JWT access tokens, rotating refresh tokens, organization switching, and role management. Phase 3 agent keys are now documented separately in [agents.md](agents.md); persisted action authorization remains later work.

## Start locally

Your existing PostgreSQL installation can be used; Docker is optional. Configure `POSTGRES_*` and a random `JWT_SECRET` in the ignored root `.env`. Generate a signing secret with:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Copy the result into `JWT_SECRET`. Keep it out of Git and out of `VITE_` variables. `JWT_ISSUER` defaults to `AgentGate`; `JWT_AUDIENCE` defaults to `AgentGate.Dashboard`.

```powershell
./scripts/start-backend.ps1 -MigrateOnly
./scripts/start-backend.ps1
```

In a second terminal, run `npm --prefix dashboard run dev` and open `http://localhost:5173/register`. Registration creates the user, an organization, its Owner membership, and the initial session in one database commit. No default account or shared password is seeded.

Use Settings to rename the organization, add an already registered user, assign a role, or create another organization. The header selects among your organizations. Email invitations and ownership transfer are not implemented in this phase.

## Security behavior

- Passwords use ASP.NET Core's `PasswordHasher<User>`. Passwords and full refresh tokens are never stored in the database or logged by the application.
- Access JWTs expire after 15 minutes. Signature, issuer, audience, lifetime, and the allowed signing algorithm are validated.
- Refresh tokens are random, valid for seven days, and stored only as SHA-256 hashes in PostgreSQL. They are sent to the browser through an HttpOnly, SameSite=Strict cookie scoped to `/api/auth`.
- The browser keeps access tokens only in memory. Reloading rotates the refresh token. Browser storage contains neither token.
- Refresh rotation conditionally revokes the old session and inserts the new session in a PostgreSQL transaction. Concurrent refresh requests have one winner. Old refresh tokens cannot be reused.
- Logout and organization switching revoke the old session. Every authenticated request checks session validity, current membership role, and organization status in PostgreSQL. Changed roles, revoked sessions, and suspended organizations do not retain access through old JWT claims.
- Organization member queries constrain both organization and member identifiers. The organization context comes from the authenticated session; clients cannot change it by supplying headers or member IDs.
- Auth mutations require `X-AgentGate-Client: dashboard`; cookies use SameSite=Strict and cross-origin CORS is not enabled. Refresh cookie requests from a foreign form cannot supply the required header. Registration/login consume validated JSON bodies.
- Registration, login, and refresh are limited to 30 requests per minute per client IP. Five bad passwords lock an account for 15 minutes. Unknown users, bad passwords, and lockout return the same error text.
- HTTPS redirection and Secure cookies are enabled outside Development. Production hosting must provide an HTTPS endpoint or properly configured HTTPS reverse proxy. Local Development uses HTTP as requested by the project specification.

## Roles

| Role | Phase 2 capabilities |
| --- | --- |
| Owner | View and rename organization; add members; assign Admin, Developer, Reviewer, Viewer |
| Admin | View and rename organization; add/change Developer, Reviewer, Viewer members |
| Developer | View organization and membership; Phase 3 agent/key management |
| Reviewer | View organization and membership; later approval review |
| Viewer | View organization and membership |

All users can create another organization and become its Owner. Roles are scoped to individual organizations. Owner membership cannot be changed through member endpoints. Admins cannot grant or change Admin/Owner roles. `ManageAgents` protects the implemented Phase 3 endpoints; `ReviewActions` is reserved for the future approval system.

## API

Auth POST requests require `X-AgentGate-Client: dashboard`. Browser requests also send the refresh cookie automatically. Successful registration/login/refresh/switch responses return `accessToken`, `expiresAt`, `user`, and `organization`. Authenticated routes require `Authorization: Bearer <accessToken>`.

| Method | Route | Behavior |
| --- | --- | --- |
| POST | `/api/auth/register` | `{ email, password, name, organizationName }`; password 12–128 characters |
| POST | `/api/auth/login` | `{ email, password, organizationId? }`; defaults to first active membership |
| POST | `/api/auth/refresh` | Rotate the refresh cookie and issue a new access token |
| POST | `/api/auth/logout` | Revoke the cookie's session and clear the cookie |
| GET | `/api/auth/me` | Current user and organization |
| POST | `/api/auth/switch-organization` | `{ organizationId }`; only your own memberships |
| POST | `/api/auth/organizations` | `{ name }`; create and switch to another organization |
| GET | `/api/organizations` | Organizations the current user belongs to |
| GET | `/api/organizations/current` | Current organization's details |
| PATCH | `/api/organizations/current` | `{ name }`; Owner/Admin only |
| GET | `/api/organizations/current/members` | Current organization's members |
| POST | `/api/organizations/current/members` | `{ email, role }`; add an existing registered account |
| PATCH | `/api/organizations/current/members/{id}` | `{ role }`; tenant-scoped membership update |

Validation and application errors use Problem Details. Credentials and session failures return 401, insufficient privileges return 403, inaccessible member/organization identifiers return 404, duplicate accounts/memberships or concurrent updates return 409, and rate limits return 429.

## Verification

Run `./scripts/test-backend.ps1`. It uses `.env` database credentials or an explicit `AGENTGATE_TEST_CONNECTION`; the PostgreSQL role must be able to create databases. It creates a unique `agentgate_tests_*` database, runs the real EF migration, tests the HTTP API, and drops only that isolated database afterward. It does not reset or delete your AgentGate database.

`./scripts/test-backend.ps1 -UnitOnly` runs role and key-format tests without PostgreSQL. Frontend checks remain `npm --prefix dashboard run build` and `npm --prefix dashboard run lint`.

Verified: 22 backend tests; registration validation and hashing; duplicate email normalization; JWT expiry, issuer, audience, and signature rejection; tenant isolation; all five roles; immediate role-change enforcement; lockout; CSRF header rejection; atomic concurrent refresh; replay rejection; revocation; organization switching; suspension and expiry; auth rate limits. Browser verification covers registration, login, logout, reload refresh, organization rename/create/switch, cookie protections, empty browser token storage, desktop/mobile layout, and no JavaScript runtime errors.
