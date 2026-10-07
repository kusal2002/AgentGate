# AgentGate dashboard

React + TypeScript + Vite workspace with Tailwind v4, shadcn/ui, React Router, and TanStack Query. Run `npm ci`, then `npm run dev` at `http://localhost:5173`.

The root `.env` sets `BACKEND_URL` for Vite's server-side proxy and `VITE_API_BASE_URL` for the browser. `/api` is the default browser base and is forwarded to `http://localhost:5000` during development. Health endpoints lose the `/api` prefix; account APIs retain it. Auth refresh cookies are HttpOnly and access tokens live only in memory.

Create an account at `/register`, or sign in at `/login`. Overview displays live API and database health. The organization selector switches among your memberships. Settings manages organizations and roles. Agents supports registration, metadata edits, disabling, safe key metadata, one-time API-key generation, expiry, and revocation. Approvals, Policies, and Audit log remain placeholders. There are no mocked business records.

Use `npm run build` and `npm run lint` to verify. For configuration and migrations, see the root README and `docs/development.md`.
