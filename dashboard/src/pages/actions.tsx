import { Link, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { RefreshCw, ArrowRight, ListChecks } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { formatDate, type Agent } from "@/lib/agents";
import type { ActionPage, ActionSummary } from "@/lib/actions";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function ActionTable({ items }: { items: ActionSummary[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="border-b text-xs text-muted-foreground">
          <tr>
            {[
              "Action",
              "Agent",
              "Resource",
              "Decision",
              "Status",
              "Requested",
              "",
            ].map((label, index) => (
              <th key={index} className="px-3 py-3 font-medium">
                {label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((action) => (
            <tr key={action.id}>
              <td className="px-3 py-4">
                <Link
                  className="font-medium text-primary hover:underline"
                  to={`/actions/${action.id}`}
                >
                  {action.action}
                </Link>
                <p className="mt-1 font-mono text-xs text-muted-foreground">
                  {action.id.slice(0, 8)}…
                </p>
              </td>
              <td className="px-3 py-4">
                <Link
                  className="text-primary hover:underline"
                  to={`/agents/${action.agentId}`}
                >
                  {action.agentName}
                </Link>
              </td>
              <td className="max-w-56 break-words px-3 py-4">
                <p>{action.resource.type}</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {action.resource.id}
                </p>
              </td>
              <td className="px-3 py-4">
                <Badge
                  variant="outline"
                  className={
                    action.decision === "allow"
                      ? "border-emerald-200 bg-emerald-50 text-emerald-800"
                      : action.decision === "deny"
                        ? "border-red-200 bg-red-50 text-red-800"
                        : "bg-amber-50 text-amber-800"
                  }
                >
                  {action.decision}
                </Badge>
              </td>
              <td className="px-3 py-4">
                <Badge variant="secondary">
                  {action.status.replaceAll("_", " ")}
                </Badge>
              </td>
              <td className="whitespace-nowrap px-3 py-4 text-xs">
                {formatDate(action.createdAt)}
              </td>
              <td className="px-3 py-4">
                <Link
                  to={`/actions/${action.id}`}
                  aria-label={`View action ${action.id}`}
                >
                  <ArrowRight className="size-4" />
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function ActionsPage() {
  const { session } = useAuth();
  const orgId = session!.organization.id;
  const [search, setSearch] = useSearchParams();
  const agentId = search.get("agentId") || "";
  const rawPage = Number(search.get("page") || "1");
  const page =
    Number.isInteger(rawPage) && rawPage >= 1 && rawPage <= 1_000_000
      ? rawPage
      : 1;
  const agents = useQuery({
    queryKey: ["agents", orgId],
    queryFn: ({ signal }) => api<Agent[]>("/api/agents", { signal }),
  });
  const actions = useQuery({
    queryKey: ["actions", orgId, agentId, page],
    queryFn: ({ signal }) =>
      api<ActionPage>(
        `/api/actions?page=${page}&pageSize=25${agentId ? `&agentId=${encodeURIComponent(agentId)}` : ""}`,
        { signal },
      ),
    refetchInterval: 30_000,
  });
  const totalPages = Math.max(1, Math.ceil((actions.data?.total || 0) / 25));
  return (
    <>
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Action history
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Actions</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Requests submitted by your agents, with their stored outcomes.
          </p>
        </div>
        <Button
          variant="outline"
          disabled={actions.isFetching}
          onClick={() => void actions.refetch()}
        >
          <RefreshCw className="size-4" />
          Refresh actions
        </Button>
      </div>
      <p className="mb-6 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm">
        Policies determine allow, review, or deny. Human
        approval requests can now be resolved from Approvals. Approval does not execute an action.
      </p>
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <CardTitle>Recorded requests</CardTitle>
              <CardDescription className="mt-2">
                {actions.data
                  ? `${actions.data.total} requests`
                  : "Browse requests in this organization."}
              </CardDescription>
            </div>
            <label className="text-sm" htmlFor="action-agent-filter">
              Agent
              <select
                id="action-agent-filter"
                className="ml-2 max-w-56 rounded-md border bg-background px-3 py-2"
                value={agentId}
                onChange={(event) =>
                  setSearch(
                    event.target.value ? { agentId: event.target.value } : {},
                  )
                }
              >
                <option value="">All agents</option>
                {agents.data?.map((agent) => (
                  <option key={agent.id} value={agent.id}>
                    {agent.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </CardHeader>
        <CardContent>
          {actions.isPending && (
            <p role="status" className="text-sm">
              Loading actions…
            </p>
          )}
          {(actions.error || agents.error) && (
            <p role="alert" className="text-sm text-destructive">
              {(actions.error || agents.error)?.message}
            </p>
          )}
          {actions.data?.items.length === 0 && (
            <div className="py-12 text-center">
              <ListChecks className="mx-auto mb-4 size-10 text-primary" />
              <h2 className="text-lg font-semibold">No action requests yet</h2>
              <p className="mt-2 text-sm text-muted-foreground">
                Submit a request through the action API using an agent key.
              </p>
            </div>
          )}
          {!!actions.data?.items.length && (
            <ActionTable items={actions.data.items} />
          )}
          {actions.data && (totalPages > 1 || page > 1) && (
            <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t pt-4">
              <p className="text-xs text-muted-foreground">
                Page {page} of {totalPages}
              </p>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page <= 1 || actions.isFetching}
                  onClick={() =>
                    setSearch({
                      ...(agentId ? { agentId } : {}),
                      page: String(page - 1),
                    })
                  }
                >
                  Previous
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages || actions.isFetching}
                  onClick={() =>
                    setSearch({
                      ...(agentId ? { agentId } : {}),
                      page: String(page + 1),
                    })
                  }
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  );
}

export function RecentAgentActions({
  agentId,
  orgId,
}: {
  agentId: string;
  orgId: string;
}) {
  const actions = useQuery({
    queryKey: ["recent-actions", orgId, agentId],
    queryFn: ({ signal }) =>
      api<ActionPage>(`/api/actions?agentId=${agentId}&pageSize=5`, { signal }),
    refetchInterval: 30_000,
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle>Recent actions</CardTitle>
        <CardDescription>Latest requests from this agent.</CardDescription>
      </CardHeader>
      <CardContent>
        {actions.isPending && (
          <p role="status" className="text-sm">
            Loading actions…
          </p>
        )}
        {actions.error && (
          <p role="alert" className="text-sm text-destructive">
            {actions.error.message}
          </p>
        )}
        {actions.data?.items.length === 0 && (
          <p className="text-sm text-muted-foreground">
            No action requests yet.
          </p>
        )}
        {!!actions.data?.items.length && (
          <ActionTable items={actions.data.items} />
        )}
        <Link
          className="mt-4 inline-block text-sm text-primary hover:underline"
          to={`/actions?agentId=${agentId}`}
        >
          View all actions from this agent
        </Link>
      </CardContent>
    </Card>
  );
}
