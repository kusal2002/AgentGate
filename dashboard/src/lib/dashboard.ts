import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import type { ActionResource } from "@/lib/actions";

export type ActionStatistics = {
  actionsEvaluated: number;
  autoAllowed: number;
  humanReviews: number;
  denied: number;
  pendingApprovals: number;
  averageApprovalSeconds: number | null;
  approvedActions: number;
  executedActions: number;
};
export type RecentActivity = {
  id: string;
  agentId: string;
  agentName: string;
  action: string;
  resource: ActionResource;
  decision: string;
  status: string;
  riskLevel: string | null;
  createdAt: string;
  approvalId: string | null;
  reviewerId: string | null;
  reviewerName: string | null;
};
export type DashboardOverview = {
  statistics: ActionStatistics;
  totalAgents: number;
  activeAgents: number;
  enabledPolicies: number;
  recentActivity: RecentActivity[];
  asOf: string;
};
export function useDashboard() {
  const { session } = useAuth();
  return useQuery({
    queryKey: ["dashboard", session!.organization.id],
    queryFn: ({ signal }) =>
      api<DashboardOverview>("/api/dashboard", { signal }),
    refetchInterval: 30_000,
    staleTime: 10_000,
  });
}
export function formatDuration(seconds: number | null) {
  if (seconds === null) return "—";
  const total = Math.max(0, Math.round(seconds));
  if (total < 60) return `${total}s`;
  if (total < 3600) return `${Math.floor(total / 60)}m ${total % 60}s`;
  return `${Math.floor(total / 3600)}h ${Math.floor((total % 3600) / 60)}m`;
}
