import { Link, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/agents";
import { maskSecrets, type ActionDetail } from "@/lib/actions";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function ActionDetailRoute() {
  const { id } = useParams();
  const { session } = useAuth();
  const action = useQuery({
    queryKey: ["action", session!.organization.id, id],
    queryFn: ({ signal }) =>
      api<ActionDetail>(`/api/actions/${id}`, { signal }),
    refetchInterval: (query) =>
      query.state.data?.status === "awaiting_approval" ? 5000 : false,
  });
  const data = action.data;
  return (
    <>
      <Link
        className="mb-5 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary"
        to="/actions"
      >
        <ArrowLeft className="size-4" />
        Back to actions
      </Link>
      {action.isPending && <p role="status">Loading action…</p>}
      {action.error && (
        <p role="alert" className="text-sm text-destructive">
          {action.error.message}
        </p>
      )}
      {data && (
        <>
          <div className="mb-8">
            <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
              Action request
            </p>
            <h1 className="break-words text-3xl font-semibold tracking-tight">
              {data.action}
            </h1>
            <p className="mt-2 break-all font-mono text-xs text-muted-foreground">
              {data.id}
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Badge variant="outline">{data.decision}</Badge>
              <Badge variant="secondary">
                {data.status.replaceAll("_", " ")}
              </Badge>
              {data.testEvaluation && (
                <Badge variant="outline" className="bg-amber-50 text-amber-800">
                  Development test
                </Badge>
              )}
            </div>
          </div>
          <div className="grid gap-6">
            {data.approvalId && (
              <Card>
                <CardHeader>
                  <CardTitle>Human approval</CardTitle>
                  <CardDescription>
                    Approval status: {data.approvalStatus}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <Link
                    className="text-sm text-primary hover:underline"
                    to={`/approvals/${data.approvalId}`}
                  >
                    Open approval request
                  </Link>
                </CardContent>
              </Card>
            )}
            <Card>
              <CardHeader>
                <CardTitle>Evaluation outcome</CardTitle>
                <CardDescription className="leading-relaxed">
                  {data.reason}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="grid gap-4 text-sm sm:grid-cols-2">
                  <div>
                    <p className="text-xs text-muted-foreground">Agent</p>
                    <Link
                      className="mt-1 inline-block text-primary hover:underline"
                      to={`/agents/${data.agentId}`}
                    >
                      {data.agentName}
                    </Link>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">Resource</p>
                    <p className="mt-1 break-all">
                      {data.resource.type} · {data.resource.id}
                    </p>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">Requested</p>
                    <p className="mt-1">{formatDate(data.createdAt)}</p>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">Execution</p>
                    <p className="mt-1">
                      {formatDate(data.executedAt, "Not executed")}
                    </p>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">Risk level</p>
                    <p className="mt-1">{data.riskLevel || "Not assessed"}</p>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">
                      Matched policy
                    </p>
                    {data.matchedPolicyId ? (
                      <Link
                        className="mt-1 inline-block break-all text-primary hover:underline"
                        to={`/policies/${data.matchedPolicyId}`}
                      >
                        {data.matchedPolicyName || data.matchedPolicyId}
                      </Link>
                    ) : (
                      <p className="mt-1">
                        {data.testEvaluation
                          ? "Legacy test result"
                          : "Environment default"}
                      </p>
                    )}
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">
                      Reviewer role
                    </p>
                    <p className="mt-1">
                      {data.reviewerRole || "Not required"}
                    </p>
                  </div>
                  {data.policyUpdatedAt && (
                    <div>
                      <p className="text-xs text-muted-foreground">
                        Policy version evaluated
                      </p>
                      <p className="mt-1">{formatDate(data.policyUpdatedAt)}</p>
                    </div>
                  )}
                  <div className="sm:col-span-2">
                    <p className="text-xs text-muted-foreground">
                      Idempotency key
                    </p>
                    <p className="mt-1 break-all font-mono text-xs">
                      {data.idempotencyKey}
                    </p>
                  </div>
                </div>
              </CardContent>
            </Card>
            <div className="grid min-w-0 gap-6 lg:grid-cols-2">
              {[
                { title: "Parameters", value: data.parameters },
                { title: "Context", value: data.context },
              ].map((item) => (
                <Card key={item.title} className="min-w-0">
                  <CardHeader>
                    <CardTitle>{item.title}</CardTitle>
                    <CardDescription>
                      Common credential fields are masked.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <pre className="max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-secondary p-4 font-mono text-xs leading-relaxed">
                      {JSON.stringify(maskSecrets(item.value), null, 2)}
                    </pre>
                  </CardContent>
                </Card>
              ))}
            </div>
          </div>
        </>
      )}
    </>
  );
}
