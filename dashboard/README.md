# AgentGate dashboard

React + TypeScript + Vite workspace with Tailwind v4, shadcn/ui, React Router, and TanStack Query. Run `npm ci`, then `npm run dev` at `http://localhost:5173`.

The root `.env` sets `BACKEND_URL` for Vite's server-side proxy and `VITE_API_BASE_URL` for the browser. `/api` is the default browser base and is forwarded to `http://localhost:5000` during development. Health endpoints lose the `/api` prefix; account APIs retain it. Auth refresh cookies are HttpOnly and access tokens live only in memory.

Create an account at `/register`, or sign in at `/login`. Overview displays live API and database health. Settings manages organizations and roles. Agents supports registration, metadata edits, disable/enable, secure key generation, expiry presets, and revocation. Actions displays persisted history and policy outcome snapshots. Policies adds rule creation/editing/toggling, a typed condition builder, Development sample rules, and previews. Owner/Admin can manage; Developer can test; Reviewer/Viewer have read-only access. Preview creates no action. Approved requests have not been executed, and Approvals supports filtering, details, authorized approve/reject decisions, comments, and deadline expiry. Common credential fields are masked in payload display. Settings includes Slack channel enable/disable, reviewer mappings, and failed delivery counts. Decision history identifies Slack/dashboard provenance. Audit log remains a placeholder.

Use `npm run build` and `npm run lint` to verify. For configuration and migrations, see the root README and `docs/development.md`.
