import { AuditTimeline } from '@/components/audit-timeline';
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CheckCircle2, XCircle } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/agents";
import { maskSecrets } from "@/lib/actions";
import type { ApprovalDetail } from "@/lib/approvals";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
export function ApprovalDetailRoute() {
  const { id } = useParams();
  const { session } = useAuth();
  return (
    <ApprovalDetailPage key={`${session!.organization.id}:${id}`} id={id!} />
  );
}
function ApprovalDetailPage({ id }: { id: string }) {
  const { session } = useAuth();
  const orgId = session!.organization.id;
  const queryClient = useQueryClient();
  const [comment, setComment] = useState("");
  const detail = useQuery({
    queryKey: ["approval", orgId, id],
    queryFn: ({ signal }) =>
      api<ApprovalDetail>(`/api/approvals/${id}`, { signal }),
    refetchInterval: (query) =>
      query.state.data?.approval.status === "pending" ? 5000 : false,
  });
  const resolve = useMutation({
    mutationFn: (decision: string) =>
      api<ApprovalDetail>(`/api/approvals/${id}/${decision}`, {
        method: "POST",
        body: JSON.stringify({ comment }),
      }),
    onSuccess: async (value) => {
      queryClient.setQueryData(["approval", orgId, id], value);
      setComment("");
      for (const key of ["approvals", "actions", "action", "recent-actions", "audit-timeline", "audit", "audit-options", "dashboard", "agent-statistics"])
        await queryClient.invalidateQueries({ queryKey: [key, orgId] });
    },
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: ["approval", orgId, id] });
    },
  });
  const data = detail.data;
  return (
    <>
      <Link
        className="mb-5 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary"
        to="/approvals"
      >
        <ArrowLeft className="size-4" />
        Back to approvals
      </Link>
      {detail.isPending && <p role="status">Loading approval…</p>}
      {detail.error && (
        <p role="alert" className="text-sm text-destructive">
          {detail.error.message}
        </p>
      )}
      {data && (
        <>
          <div className="mb-8">
            <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
              Approval request
            </p>
            <h1 className="break-words text-3xl font-semibold tracking-tight">
              {data.approval.action}
            </h1>
            <p className="mt-2 break-all font-mono text-xs text-muted-foreground">
              {id}
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Badge variant="outline">{data.approval.status}</Badge>
              <Badge variant="secondary">{data.approval.riskLevel}</Badge>
            </div>
          </div>
          <div className="grid min-w-0 gap-6 lg:grid-cols-[1.4fr_1fr]">
            <div className="min-w-0 space-y-6">
              <Card>
                <CardHeader>
                  <CardTitle>Request details</CardTitle>
                  <CardDescription>{data.action.reason}</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="grid gap-4 text-sm sm:grid-cols-2">
                    <div>
                      <p className="text-xs text-muted-foreground">Agent</p>
                      <Link
                        className="mt-1 inline-block text-primary hover:underline"
                        to={`/agents/${data.approval.agentId}`}
                      >
                        {data.approval.agentName}
                      </Link>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Resource</p>
                      <p className="mt-1 break-all">
                        {data.approval.resource.type} ·{" "}
                        {data.approval.resource.id}
                      </p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">
                        Reviewer role
                      </p>
                      <p className="mt-1">{data.approval.reviewerRole}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Expires</p>
                      <p className="mt-1">
                        {formatDate(data.approval.expiresAt)}
                      </p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">
                        Action status
                      </p>
                      <p className="mt-1">
                        {data.action.status.replaceAll("_", " ")}
                      </p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Execution</p>
                      <p className="mt-1">
                        {formatDate(data.action.executedAt, "Not executed")}
                      </p>
                    </div>
                  </div>
                  <Link
                    className="mt-5 inline-block text-sm text-primary hover:underline"
                    to={`/actions/${data.approval.actionId}`}
                  >
                    View full action and policy snapshot
                  </Link>
                </CardContent>
              </Card>
              {["parameters", "context"].map((key) => (
                <Card key={key} className="min-w-0">
                  <CardHeader>
                    <CardTitle>
                      {key === "parameters" ? "Parameters" : "Context"}
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <pre className="max-h-80 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-secondary p-4 font-mono text-xs">
                      {JSON.stringify(
                        maskSecrets(
                          key === "parameters"
                            ? data.action.parameters
                            : data.action.context,
                        ),
                        null,
                        2,
                      )}
                    </pre>
                  </CardContent>
                </Card>
              ))}
            </div>
            <div className="min-w-0 space-y-6">
              <Card>
                <CardHeader>
                  <CardTitle>
                    {data.approval.status === "pending"
                      ? "Your decision"
                      : "Resolved request"}
                  </CardTitle>
                  <CardDescription>
                    Approval permits the agent to continue; it does not execute
                    the action.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  {resolve.error && (
                    <p role="alert" className="mb-4 text-sm text-destructive">
                      {resolve.error.message}
                    </p>
                  )}
                  {data.canReview ? (
                    <>
                      <label className="block text-sm" htmlFor="review-comment">
                        Comment (optional)
                        <textarea
                          id="review-comment"
                          className="mt-2 min-h-28 w-full rounded-md border bg-background p-3 text-sm"
                          maxLength={2000}
                          value={comment}
                          onChange={(event) => setComment(event.target.value)}
                          disabled={resolve.isPending}
                        />
                      </label>
                      <div className="mt-4 flex flex-wrap gap-2">
                        <Button
                          disabled={resolve.isPending}
                          onClick={() => {
                            if (
                              window.confirm(
                                "Approve this request and permit the agent to continue?",
                              )
                            )
                              resolve.mutate("approve");
                          }}
                        >
                          <CheckCircle2 className="size-4" />
                          Approve
                        </Button>
                        <Button
                          variant="outline"
                          className="text-destructive"
                          disabled={resolve.isPending}
                          onClick={() => {
                            if (
                              window.confirm(
                                "Reject this request? The agent must not execute it.",
                              )
                            )
                              resolve.mutate("reject");
                          }}
                        >
                          <XCircle className="size-4" />
                          Reject
                        </Button>
                      </div>
                    </>
                  ) : (
                    <p className="text-sm text-muted-foreground">
                      {data.approval.status === "pending"
                        ? "Your current role cannot review this request."
                        : data.approval.status === "expired"
                          ? "The review deadline passed. Submit a new action request if review is still needed."
                          : `This request is ${data.approval.status}. It cannot be resolved again.`}
                    </p>
                  )}
                </CardContent>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>Request history</CardTitle>
                  <CardDescription>
                    Saved request and decision times.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-5 text-sm">
                  <div>
                    <p className="font-medium">Action requested</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {formatDate(data.action.createdAt)}
                    </p>
                  </div>
                  <div>
                    <p className="font-medium">Approval requested</p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {formatDate(data.approval.requestedAt)}
                    </p>
                  </div>
                  {data.decisions.map((decision) => (
                    <div key={decision.id}>
                      <p className="font-medium">
                        {decision.decision === "approve"
                          ? "Approved"
                          : "Rejected"}{" "}
                        by {decision.reviewerName} via {decision.source === 'slack' ? 'Slack' : 'dashboard'}
                      </p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        {formatDate(decision.createdAt)}
                      </p>
                      {decision.comment && (
                        <p className="mt-2 whitespace-pre-wrap break-words rounded-md bg-secondary p-3">
                          {decision.comment}
                        </p>
                      )}
                    </div>
                  ))}
                  {data.approval.status === "expired" && (
                    <div>
                      <p className="font-medium">Review expired</p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        {formatDate(data.approval.resolvedAt)}
                      </p>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </div>
          <div className="mt-6"><AuditTimeline key={data.action.id} actionId={data.action.id} /></div>
        </>
      )}
    </>
  );
}
