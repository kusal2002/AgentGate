import { Link, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ClipboardCheck, RefreshCw, ArrowRight } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/agents";
import type { ApprovalPage } from "@/lib/approvals";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
const statuses = ["pending", "approved", "rejected", "expired", "all"];
export function ApprovalsPage() {
  const { session } = useAuth();
  const [search, setSearch] = useSearchParams();
  const status = statuses.includes(search.get("status") || "")
    ? search.get("status")!
    : "pending";
  const parsedPage = Number(search.get("page") || 1);
  const page =
    Number.isInteger(parsedPage) && parsedPage >= 1 && parsedPage <= 1_000_000
      ? parsedPage
      : 1;
  const approvals = useQuery({
    queryKey: ["approvals", session!.organization.id, status, page],
    queryFn: ({ signal }) =>
      api<ApprovalPage>(
        `/api/approvals?page=${page}&pageSize=12${status === "all" ? "" : `&status=${status}`}`,
        { signal },
      ),
    refetchInterval: 30_000,
  });
  const totalPages = Math.max(1, Math.ceil((approvals.data?.total || 0) / 12));
  return (
    <>
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Human review
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Approvals</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Review agent requests before they continue.
          </p>
        </div>
        <Button
          variant="outline"
          disabled={approvals.isFetching}
          onClick={() => void approvals.refetch()}
        >
          <RefreshCw className="size-4" />
          Refresh approvals
        </Button>
      </div>
      <nav
        aria-label="Approval status filters"
        className="mb-6 flex flex-wrap gap-2"
      >
        {statuses.map((value) => (
          <Button
            key={value}
            variant={status === value ? "default" : "outline"}
            size="sm"
            aria-pressed={status === value}
            onClick={() => setSearch({ status: value })}
          >
            {value[0].toUpperCase() + value.slice(1)}
          </Button>
        ))}
      </nav>
      {approvals.isPending && <p role="status">Loading approvals…</p>}
      {approvals.error && (
        <p role="alert" className="text-sm text-destructive">
          {approvals.error.message}
        </p>
      )}
      {approvals.data?.items.length === 0 && (
        <Card>
          <CardContent className="py-12 text-center">
            <ClipboardCheck className="mx-auto mb-4 size-10 text-primary" />
            <h2 className="text-lg font-semibold">
              No {status === "all" ? "" : status + " "}approvals
            </h2>
            <p className="mt-2 text-sm text-muted-foreground">
              Requests that require human review appear here.
            </p>
          </CardContent>
        </Card>
      )}
      <div className="grid gap-4 lg:grid-cols-2">
        {approvals.data?.items.map((approval) => (
          <Card key={approval.id} className="min-w-0">
            <CardHeader>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <CardTitle className="break-words">
                    {approval.agentName}
                  </CardTitle>
                  <CardDescription className="mt-2">
                    {approval.action}
                  </CardDescription>
                </div>
                <div className="flex gap-2">
                  <Badge variant="outline">{approval.status}</Badge>
                  <Badge variant="secondary">
                    {approval.riskLevel || "Unassessed"}
                  </Badge>
                </div>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              {approval.amount !== null && (
                <p className="text-2xl font-semibold tracking-tight">
                  {approval.amount.toLocaleString()}{" "}
                  <span className="text-sm font-normal text-muted-foreground">
                    {approval.currency}
                  </span>
                </p>
              )}
              <div className="grid gap-3 text-sm sm:grid-cols-2">
                <div>
                  <p className="text-xs text-muted-foreground">Resource</p>
                  <p className="mt-1 break-all">
                    {approval.resource.type} · {approval.resource.id}
                  </p>
                </div>
                <div>
                  <p className="text-xs text-muted-foreground">Reviewer role</p>
                  <p className="mt-1">{approval.reviewerRole}</p>
                </div>
                <div>
                  <p className="text-xs text-muted-foreground">Policy</p>
                  <p className="mt-1">
                    {approval.matchedPolicyName || "Environment default"}
                  </p>
                </div>
                <div>
                  <p className="text-xs text-muted-foreground">Requested</p>
                  <p className="mt-1">{formatDate(approval.requestedAt)}</p>
                </div>
              </div>
              <p className="text-xs text-muted-foreground">
                {approval.status === "pending"
                  ? `Expires: ${formatDate(approval.expiresAt)}`
                  : `Resolved: ${formatDate(approval.resolvedAt)}`}
              </p>
              <Button variant="outline" asChild>
                <Link to={`/approvals/${approval.id}`}>
                  View request
                  <ArrowRight className="size-4" />
                </Link>
              </Button>
            </CardContent>
          </Card>
        ))}
      </div>
      {approvals.data && (totalPages > 1 || page > 1) && (
        <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
          <p className="text-xs text-muted-foreground">
            Page {page} of {totalPages} · {approvals.data.total} requests
          </p>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={page <= 1 || approvals.isFetching}
              onClick={() => setSearch({ status, page: String(page - 1) })}
            >
              Previous
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={page >= totalPages || approvals.isFetching}
              onClick={() => setSearch({ status, page: String(page + 1) })}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </>
  );
}
