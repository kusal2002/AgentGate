import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  RefreshCw,
  ClipboardCheck,
  Bot,
  SlidersHorizontal,
} from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { useDashboard, formatDuration } from "@/lib/dashboard";
import { formatDate } from "@/lib/agents";
import { getHealth } from "@/lib/health";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function Overview() {
  const { session } = useAuth();
  const dashboard = useDashboard();
  const data = dashboard.data;
  const api = useQuery({
    queryKey: ["health"],
    queryFn: ({ signal }) => getHealth("/health", signal),
    retry: false,
    refetchInterval: 30_000,
  });
  const database = useQuery({
    queryKey: ["readiness"],
    queryFn: ({ signal }) => getHealth("/health/ready", signal),
    retry: false,
    refetchInterval: 30_000,
  });
  const stats = data?.statistics;
  const cards = [
    {
      label: "Actions evaluated",
      value: stats?.actionsEvaluated,
      detail: "Policy evaluations",
      path: "/actions",
    },
    {
      label: "Auto allowed",
      value: stats?.autoAllowed,
      detail: "Allowed by policy",
      path: "/audit-log?decision=allow",
    },
    {
      label: "Human reviews",
      value: stats?.humanReviews,
      detail: "Requests requiring review",
      path: "/approvals?status=all",
    },
    {
      label: "Denied",
      value: stats?.denied,
      detail: "Denied by policy",
      path: "/audit-log?decision=deny",
    },
    {
      label: "Pending approvals",
      value: stats?.pendingApprovals,
      detail: "Waiting for a reviewer",
      path: "/approvals?status=pending",
    },
    {
      label: "Average approval time",
      value: stats ? formatDuration(stats.averageApprovalSeconds) : undefined,
      detail: "Approved and rejected requests",
      path: "/approvals?status=all",
    },
  ];
  return (
    <>
      <div className="mb-7 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Workspace overview
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">
            Your workspace at a glance
          </h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Authorization activity for {session!.organization.name}.
          </p>
        </div>
        <Button
          variant="outline"
          disabled={dashboard.isFetching}
          onClick={() => {
            void dashboard.refetch();
            void api.refetch();
            void database.refetch();
          }}
        >
          <RefreshCw className="size-4" />
          Refresh overview
        </Button>
      </div>
      {dashboard.isPending && (
        <p role="status" className="mb-5 text-sm text-muted-foreground">
          Loading workspace activity…
        </p>
      )}
      {dashboard.error && (
        <div
          role="alert"
          className="mb-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm"
        >
          <p>
            {dashboard.error.message}{" "}
            {data && "Showing the last successful snapshot."}
          </p>
          <Button
            className="mt-2"
            variant="outline"
            size="sm"
            onClick={() => void dashboard.refetch()}
          >
            Retry overview
          </Button>
        </div>
      )}
      <div className="mb-5 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {cards.map((card) => (
          <Link
            key={card.label}
            to={card.path}
            className="rounded-xl focus-visible:outline-2 focus-visible:outline-primary"
          >
            <Card className="h-full transition-colors hover:border-primary/40">
              <CardHeader className="pb-0">
                <CardDescription>{card.label}</CardDescription>
                <CardTitle
                  className="text-3xl tabular-nums"
                  role="heading"
                  aria-level={2}
                >
                  {card.value === undefined
                    ? "—"
                    : typeof card.value === "number"
                      ? card.value.toLocaleString()
                      : card.value}
                </CardTitle>
              </CardHeader>
              <CardContent className="text-xs text-muted-foreground">
                {card.detail}
              </CardContent>
            </Card>
          </Link>
        ))}
      </div>
      <p className="mb-7 text-xs text-muted-foreground">
        All time · Approval authorizes an action; completed execution is tracked
        separately.{data && <> Updated {formatDate(data.asOf)}.</>}
      </p>
      {data && (
        <div className="mb-7 grid gap-4 sm:grid-cols-3">
          {[
            {
              icon: Bot,
              label: "Agents",
              value: `${data.activeAgents} active / ${data.totalAgents} total`,
              path: "/agents",
            },
            {
              icon: SlidersHorizontal,
              label: "Policies",
              value: `${data.enabledPolicies} enabled`,
              path: "/policies",
            },
            {
              icon: ClipboardCheck,
              label: "Review queue",
              value: `${stats!.pendingApprovals} pending`,
              path: "/approvals?status=pending",
            },
          ].map((item) => (
            <Link
              key={item.label}
              to={item.path}
              className="flex items-center gap-3 rounded-lg border bg-white p-4 text-sm hover:border-primary/40"
            >
              <item.icon className="size-5 shrink-0 text-primary" />
              <div>
                <p className="font-medium">{item.label}</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {item.value}
                </p>
              </div>
              <ArrowRight className="ml-auto size-4 text-muted-foreground" />
            </Link>
          ))}
        </div>
      )}
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <CardTitle role="heading" aria-level={2}>
                Recent activity
              </CardTitle>
              <CardDescription className="mt-2">
                The latest ten evaluated requests, with their current outcomes.
              </CardDescription>
            </div>
            <Link
              to="/actions"
              className="text-sm text-primary hover:underline"
            >
              View all actions
            </Link>
          </div>
        </CardHeader>
        <CardContent>
          {dashboard.isPending && (
            <p role="status" className="text-sm">
              Loading recent requests…
            </p>
          )}
          {data?.recentActivity.length === 0 && (
            <div className="py-8 text-center">
              <Bot className="mx-auto mb-3 size-8 text-primary" />
              <h2 className="text-lg font-medium">No evaluated requests yet</h2>
              <p className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
                Register an agent, issue an API key, and submit its first action
                request to see activity here.
              </p>
              <div className="mt-4 flex flex-wrap justify-center gap-4">
                <Link to="/agents" className="text-sm text-primary underline">
                  {["Owner", "Admin", "Developer"].includes(
                    session!.organization.role,
                  )
                    ? "Manage agents"
                    : "View agents"}
                </Link>
                <Link to="/policies" className="text-sm text-primary underline">
                  View policies
                </Link>
              </div>
            </div>
          )}
          {!!data?.recentActivity.length && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b text-xs text-muted-foreground">
                  <tr>
                    {[
                      "Time",
                      "Agent",
                      "Action / resource",
                      "Decision",
                      "Status",
                      "Risk",
                      "Reviewer",
                    ].map((label) => (
                      <th key={label} className="px-3 py-3 font-medium">
                        {label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {data!.recentActivity.map((item) => (
                    <tr key={item.id}>
                      <td className="whitespace-nowrap px-3 py-4 text-xs">
                        {formatDate(item.createdAt)}
                      </td>
                      <td className="px-3 py-4">
                        <Link
                          to={`/agents/${item.agentId}`}
                          className="text-primary hover:underline"
                        >
                          {item.agentName}
                        </Link>
                      </td>
                      <td className="max-w-60 break-words px-3 py-4">
                        <Link
                          to={`/actions/${item.id}`}
                          className="font-medium text-primary hover:underline"
                        >
                          {item.action}
                        </Link>
                        <p className="mt-1 text-xs text-muted-foreground">
                          {item.resource.type} · {item.resource.id}
                        </p>
                      </td>
                      <td className="px-3 py-4">
                        <Badge variant="outline">{item.decision}</Badge>
                      </td>
                      <td className="px-3 py-4">
                        {item.approvalId ? (
                          <Link
                            className="text-primary hover:underline"
                            to={`/approvals/${item.approvalId}`}
                          >
                            {item.status.replaceAll("_", " ")}
                          </Link>
                        ) : (
                          item.status.replaceAll("_", " ")
                        )}
                      </td>
                      <td className="px-3 py-4 text-xs">
                        {item.riskLevel || "—"}
                      </td>
                      <td className="px-3 py-4 text-xs">
                        {item.reviewerName ||
                          (item.reviewerId ? item.reviewerId : "—")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {!data && dashboard.error && (
            <p className="text-sm text-muted-foreground">
              Recent activity is unavailable. Retry the overview to load it.
            </p>
          )}
        </CardContent>
      </Card>
      <div
        className="mt-6 flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border bg-white px-4 py-3 text-xs text-muted-foreground"
        aria-label="Service health"
      >
        <span>
          API:{" "}
          {api.isPending
            ? "Checking"
            : api.data?.status === "ok" && !api.isError
              ? "Online"
              : "Unavailable"}
        </span>
        <span>
          PostgreSQL:{" "}
          {database.isPending
            ? "Checking"
            : database.data?.status === "healthy" && !database.isError
              ? "Connected"
              : "Unavailable"}
        </span>
      </div>
    </>
  );
}
