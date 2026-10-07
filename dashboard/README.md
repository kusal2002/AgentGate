# AgentGate dashboard

Phase 1 React + TypeScript + Vite workspace with Tailwind v4, shadcn/ui, React Router, and TanStack Query. Run `npm ci`, then `npm run dev` at `http://localhost:5173`.

The root `.env` sets `BACKEND_URL` for Vite's server-side proxy and `VITE_API_BASE_URL` for the browser. `/api` is the default browser base and is forwarded to `http://localhost:5000` during development.

Overview displays live API and database health. Agents, Approvals, Policies, Audit log, and Settings are placeholders for later phases. There are no mocked business records.

Use `npm run build` and `npm run lint` to verify. For configuration and migrations, see the root README and `docs/development.md`.
