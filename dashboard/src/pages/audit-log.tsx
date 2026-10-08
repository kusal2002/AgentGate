import { Link, useParams, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import {
  auditTypes,
  eventLabel,
  type AuditEvent,
  type AuditPage,
  type AuditOptions,
} from "@/lib/audit";
import { AuditEventRow } from "@/components/audit-timeline";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import type { Agent } from "@/lib/agents";
import { Input } from "@/components/ui/input";

export function AuditLogPage() {
  const { session } = useAuth();
  const [search, setSearch] = useSearchParams();
  const org = session!.organization.id;
  const rawPage = Number(search.get("page") || 1);
  const page =
    Number.isInteger(rawPage) && rawPage >= 1 && rawPage <= 1_000_000
      ? rawPage
      : 1;
  const query = new URLSearchParams(search);
  query.set("page", String(page));
  query.set("pageSize", "25");
  const events = useQuery({
    queryKey: ["audit", org, query.toString()],
    queryFn: ({ signal }) => api<AuditPage>(`/api/audit?${query}`, { signal }),
  });
  const agents = useQuery({
    queryKey: ["agents", org],
    queryFn: ({ signal }) => api<Agent[]>("/api/agents", { signal }),
  });
  const options = useQuery({
    queryKey: ["audit-options", org],
    queryFn: ({ signal }) =>
      api<AuditOptions>("/api/audit/options", { signal }),
    staleTime: 30_000,
  });
  function change(key: string, value: string) {
    const next = new URLSearchParams(search);
    next.delete("page");
    if (value) next.set(key, value);
    else next.delete(key);
    setSearch(next);
  }
  const selectClass =
    "mt-1 w-full rounded-md border bg-white px-3 py-2 text-sm";
  return (
    <>
      <div className="mb-8 flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Accountability
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Audit log</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            An append-only record of this organization’s authorization events.
          </p>
        </div>
        <Button
          variant="outline"
          onClick={() => {
            void events.refetch();
          }}
        >
          Refresh events
        </Button>
      </div>
      <Card>
        <CardHeader>
          <CardTitle role="heading" aria-level={2}>
            Recorded events
          </CardTitle>
          <CardDescription>
            Newest first. Earlier requests have an imported snapshot; new
            requests have complete timelines. Audit data excludes request
            payloads, comments, and credentials.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form
            key={search.get("search") || ""}
            className="mb-5 flex flex-wrap items-end gap-3"
            onSubmit={(event) => {
              event.preventDefault();
              const value = String(
                new FormData(event.currentTarget).get("search") || "",
              ).trim();
              change("search", value);
            }}
          >
            <label
              className="min-w-0 flex-1 text-xs text-muted-foreground"
              htmlFor="audit-search"
            >
              Search IDs
              <Input
                id="audit-search"
                name="search"
                className="mt-1"
                defaultValue={search.get("search") || ""}
                placeholder="Action, agent, approval, event, or customer ID"
                maxLength={200}
              />
            </label>
            <Button variant="outline" type="submit">
              Search
            </Button>
          </form>
          <div className="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <label className="text-xs text-muted-foreground">
              Action
              <select
                aria-label="Action"
                className={selectClass}
                value={search.get("actionType") || ""}
                onChange={(e) => change("actionType", e.target.value)}
              >
                <option value="">All actions</option>
                {options.data?.actions.map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Decision
              <select
                aria-label="Decision"
                className={selectClass}
                value={search.get("decision") || ""}
                onChange={(e) => change("decision", e.target.value)}
              >
                <option value="">All decisions</option>
                {["allow", "review", "deny"].map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Risk
              <select
                aria-label="Risk"
                className={selectClass}
                value={search.get("riskLevel") || ""}
                onChange={(e) => change("riskLevel", e.target.value)}
              >
                <option value="">All risk levels</option>
                {["Low", "Medium", "High", "Critical"].map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Reviewer
              <select
                aria-label="Reviewer"
                className={selectClass}
                value={search.get("reviewerId") || ""}
                onChange={(e) => change("reviewerId", e.target.value)}
              >
                <option value="">All reviewers</option>
                {options.data?.reviewers.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Agent
              <select
                aria-label="Agent"
                className={selectClass}
                value={search.get("agentId") || ""}
                onChange={(e) => change("agentId", e.target.value)}
              >
                <option value="">All agents</option>
                {agents.data?.map((agent) => (
                  <option key={agent.id} value={agent.id}>
                    {agent.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Event
              <select
                aria-label="Event"
                className={selectClass}
                value={search.get("eventType") || ""}
                onChange={(e) => change("eventType", e.target.value)}
              >
                <option value="">All events</option>
                {auditTypes.map((type) => (
                  <option key={type} value={type}>
                    {eventLabel(type)}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              Actor
              <select
                aria-label="Actor"
                className={selectClass}
                value={search.get("actorType") || ""}
                onChange={(e) => change("actorType", e.target.value)}
              >
                <option value="">All actors</option>
                {["Agent", "User", "Policy", "System", "Slack"].map((type) => (
                  <option key={type}>{type}</option>
                ))}
              </select>
            </label>
            <label className="text-xs text-muted-foreground">
              From (UTC)
              <input
                type="date"
                className={selectClass}
                value={search.get("from")?.slice(0, 10) || ""}
                onChange={(e) =>
                  change(
                    "from",
                    e.target.value ? `${e.target.value}T00:00:00Z` : "",
                  )
                }
              />
            </label>
            <label className="text-xs text-muted-foreground">
              Through (UTC)
              <input
                type="date"
                className={selectClass}
                value={search.get("to")?.slice(0, 10) || ""}
                onChange={(e) =>
                  change(
                    "to",
                    e.target.value ? `${e.target.value}T23:59:59.999999Z` : "",
                  )
                }
              />
            </label>
            <div className="flex items-end">
              <Button variant="ghost" onClick={() => setSearch({})}>
                Clear filters
              </Button>
            </div>
          </div>
          {events.isPending && <p role="status">Loading audit events…</p>}
          {events.error && (
            <div role="alert" className="text-sm text-destructive">
              <p>{events.error.message}</p>
              <Button
                className="mt-2"
                size="sm"
                variant="outline"
                onClick={() => void events.refetch()}
              >
                Retry events
              </Button>
            </div>
          )}
          {(options.error || agents.error) && (
            <p role="alert" className="mb-3 text-sm text-destructive">
              Some filter options could not be loaded.{" "}
              {options.error?.message || agents.error?.message}{" "}
              <button
                className="underline"
                onClick={() => {
                  void options.refetch();
                  void agents.refetch();
                }}
              >
                Retry filters
              </button>
            </p>
          )}
          {events.data && (
            <>
              {events.data.total === 0 ? (
                <p className="py-8 text-sm text-muted-foreground">
                  No events match these filters.
                </p>
              ) : (
                <ol>
                  {events.data.items.map((event) => (
                    <AuditEventRow key={event.id} event={event} />
                  ))}
                </ol>
              )}
              <div className="mt-6 flex flex-wrap items-center gap-3">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page === 1}
                  onClick={() => {
                    const next = new URLSearchParams(search);
                    next.set("page", String(page - 1));
                    setSearch(next);
                  }}
                >
                  Previous
                </Button>
                <span className="text-xs text-muted-foreground">
                  Page {page} · {events.data.total} events
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page * 25 >= events.data.total}
                  onClick={() => {
                    const next = new URLSearchParams(search);
                    next.set("page", String(page + 1));
                    setSearch(next);
                  }}
                >
                  Next
                </Button>
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </>
  );
}
export function AuditEventRoute() {
  const { id } = useParams();
  const { session } = useAuth();
  const detail = useQuery({
    queryKey: ["audit-event", session!.organization.id, id],
    queryFn: ({ signal }) => api<AuditEvent>(`/api/audit/${id}`, { signal }),
  });
  return (
    <>
      <Link
        to="/audit-log"
        className="mb-5 inline-block text-sm text-primary hover:underline"
      >
        Back to audit log
      </Link>
      <Card>
        <CardHeader>
          <CardTitle role="heading" aria-level={1}>
            Audit event
          </CardTitle>
          <CardDescription className="break-all">{id}</CardDescription>
        </CardHeader>
        <CardContent>
          {detail.isPending && <p role="status">Loading event…</p>}
          {detail.error && (
            <p role="alert" className="text-sm text-destructive">
              {detail.error.message}
            </p>
          )}
          {detail.data && (
            <>
              <ol>
                <AuditEventRow event={detail.data} />
              </ol>
              <dl className="mt-5 space-y-3 text-xs">
                {detail.data.actorId && (
                  <div>
                    <dt className="text-muted-foreground">Actor ID</dt>
                    <dd className="break-all font-mono">
                      {detail.data.actorId}
                    </dd>
                  </div>
                )}
                {detail.data.reviewerId && (
                  <div>
                    <dt className="text-muted-foreground">
                      Related action reviewer
                    </dt>
                    <dd className="break-all">
                      {detail.data.reviewerName || detail.data.reviewerId}
                    </dd>
                  </div>
                )}
                <div>
                  <dt className="text-muted-foreground">Organization</dt>
                  <dd className="break-all font-mono">
                    {detail.data.organizationId}
                  </dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">
                    Source IP (direct connection)
                  </dt>
                  <dd>{detail.data.ipAddress || "Not recorded"}</dd>
                </div>
                {detail.data.approvalRequestId && (
                  <div>
                    <dt className="text-muted-foreground">Approval</dt>
                    <dd>
                      <Link
                        className="break-all text-primary hover:underline"
                        to={`/approvals/${detail.data.approvalRequestId}`}
                      >
                        {detail.data.approvalRequestId}
                      </Link>
                    </dd>
                  </div>
                )}
              </dl>
            </>
          )}
        </CardContent>
      </Card>
    </>
  );
}
