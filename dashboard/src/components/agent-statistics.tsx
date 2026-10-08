import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import type { ActionStatistics } from "@/lib/dashboard";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";

export function AgentStatistics({ agentId }: { agentId: string }) {
  const { session } = useAuth();
  const stats = useQuery({
    queryKey: ["agent-statistics", session!.organization.id, agentId],
    queryFn: ({ signal }) =>
      api<ActionStatistics>(`/api/agents/${agentId}/statistics`, { signal }),
    refetchInterval: 30_000,
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle role="heading" aria-level={2}>
          Action totals
        </CardTitle>
        <CardDescription>
          All-time policy evaluations for this agent. Execution totals reflect
          completed executions.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {stats.isPending && (
          <p role="status" className="text-sm">
            Loading action totals…
          </p>
        )}
        {stats.error && (
          <div role="alert" className="text-sm text-destructive">
            <p>{stats.error.message}</p>
            <Button
              variant="outline"
              size="sm"
              className="mt-2"
              onClick={() => void stats.refetch()}
            >
              Retry totals
            </Button>
          </div>
        )}
        {stats.data && (
          <dl className="grid gap-4 sm:grid-cols-3">
            {[
              ["Evaluated", stats.data.actionsEvaluated],
              ["Approved", stats.data.approvedActions],
              ["Human reviews", stats.data.humanReviews],
              ["Denied by policy", stats.data.denied],
              ["Pending approvals", stats.data.pendingApprovals],
              ["Executed successfully", stats.data.executedActions],
            ].map(([label, value]) => (
              <div key={label} className="rounded-lg bg-secondary p-4">
                <dt className="text-xs text-muted-foreground">{label}</dt>
                <dd className="mt-2 text-2xl font-semibold tabular-nums">
                  {value.toLocaleString()}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
