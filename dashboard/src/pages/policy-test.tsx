import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import type { Agent } from "@/lib/agents";
import type { PolicyResult } from "@/lib/policies";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function PolicyTestPage() {
  const { session } = useAuth();
  const tester = ["Owner", "Admin", "Developer"].includes(
    session!.organization.role,
  );
  const agents = useQuery({
    queryKey: ["agents", session!.organization.id],
    queryFn: ({ signal }) => api<Agent[]>("/api/agents", { signal }),
    enabled: tester,
  });
  const [result, setResult] = useState<PolicyResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  return (
    <>
      <Link
        className="mb-5 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary"
        to="/policies"
      >
        <ArrowLeft className="size-4" />
        Back to policies
      </Link>
      <h1 className="mb-6 text-3xl font-semibold tracking-tight">
        Test policies
      </h1>
      {!tester ? (
        <p className="text-sm text-muted-foreground">
          Owner, Admin, or Developer role required.
        </p>
      ) : (
        <div className="grid min-w-0 gap-6 lg:grid-cols-2">
          <Card className="min-w-0">
            <CardHeader>
              <CardTitle>Sample request</CardTitle>
              <CardDescription>
                Uses the current enabled rules and the selected agent's
                environment. No action or approval is saved.
              </CardDescription>
            </CardHeader>
            <CardContent>
              {agents.error && (
                <p role="alert" className="mb-4 text-sm text-destructive">
                  {agents.error.message}
                </p>
              )}
              <form
                className="grid gap-4 sm:grid-cols-2"
                onChange={() => setResult(null)}
                onSubmit={async (event) => {
                  event.preventDefault();
                  setBusy(true);
                  setError("");
                  setResult(null);
                  const data = Object.fromEntries(
                    new FormData(event.currentTarget),
                  );
                  try {
                    setResult(
                      await api<PolicyResult>("/api/policies/test", {
                        method: "POST",
                        body: JSON.stringify({
                          agentId: data.agentId,
                          action: data.action,
                          resource: {
                            type: data.resourceType,
                            id: data.resourceId,
                          },
                          parameters: JSON.parse(String(data.parameters)),
                          context: JSON.parse(String(data.context)),
                        }),
                      }),
                    );
                  } catch (cause) {
                    setError(
                      cause instanceof Error
                        ? cause.message
                        : "Policy test failed.",
                    );
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                <label className="text-sm sm:col-span-2" htmlFor="test-agent">
                  Agent
                  <select
                    id="test-agent"
                    name="agentId"
                    className="mt-1.5 h-9 w-full rounded-md border bg-background px-3 text-sm"
                    defaultValue=""
                    required
                    disabled={busy}
                  >
                    <option value="" disabled>
                      Select an agent
                    </option>
                    {agents.data?.map((agent) => (
                      <option key={agent.id} value={agent.id}>
                        {agent.name} · {agent.environment}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="text-sm sm:col-span-2" htmlFor="test-action">
                  Action
                  <Input
                    id="test-action"
                    name="action"
                    className="mt-1.5"
                    defaultValue="refund"
                    maxLength={100}
                    required
                    disabled={busy}
                  />
                </label>
                <label className="text-sm" htmlFor="test-resource-type">
                  Resource type
                  <Input
                    id="test-resource-type"
                    name="resourceType"
                    className="mt-1.5"
                    defaultValue="customer"
                    maxLength={100}
                    required
                    disabled={busy}
                  />
                </label>
                <label className="text-sm" htmlFor="test-resource-id">
                  Resource ID
                  <Input
                    id="test-resource-id"
                    name="resourceId"
                    className="mt-1.5"
                    defaultValue="CUS-102"
                    maxLength={200}
                    required
                    disabled={busy}
                  />
                </label>
                <label
                  className="min-w-0 text-sm sm:col-span-2"
                  htmlFor="test-parameters"
                >
                  Parameters (JSON)
                  <textarea
                    id="test-parameters"
                    name="parameters"
                    className="mt-1.5 min-h-32 w-full rounded-md border bg-background p-3 font-mono text-xs"
                    defaultValue={'{\n  "amount": 750,\n  "currency": "USD"\n}'}
                    maxLength={32768}
                    required
                    disabled={busy}
                  />
                </label>
                <label
                  className="min-w-0 text-sm sm:col-span-2"
                  htmlFor="test-context"
                >
                  Context (JSON)
                  <textarea
                    id="test-context"
                    name="context"
                    className="mt-1.5 min-h-20 w-full rounded-md border bg-background p-3 font-mono text-xs"
                    defaultValue="{}"
                    maxLength={32768}
                    required
                    disabled={busy}
                  />
                </label>
                {error && (
                  <p
                    role="alert"
                    className="text-sm text-destructive sm:col-span-2"
                  >
                    {error}
                  </p>
                )}
                <div className="sm:col-span-2">
                  <Button
                    disabled={busy || agents.isPending || !agents.data?.length}
                  >
                    {busy ? "Testing…" : "Run policy test"}
                  </Button>
                  {agents.data?.length === 0 && (
                    <p className="mt-2 text-xs text-muted-foreground">
                      Register an agent first to test its policies.
                    </p>
                  )}
                </div>
              </form>
            </CardContent>
          </Card>
          <Card className="min-w-0 self-start">
            <CardHeader>
              <CardTitle>Evaluation result</CardTitle>
              <CardDescription>
                Preview only. Submission through the action API is required for
                a stored decision.
              </CardDescription>
            </CardHeader>
            <CardContent>
              {result ? (
                <div className="space-y-4 text-sm">
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="outline">{result.decision}</Badge>
                    <Badge variant="secondary">
                      {result.status.replaceAll("_", " ")}
                    </Badge>
                    <Badge variant="outline">{result.riskLevel}</Badge>
                  </div>
                  <p className="leading-relaxed">{result.reason}</p>
                  <div>
                    <p className="text-xs text-muted-foreground">
                      Matched policy
                    </p>
                    {result.matchedPolicyId ? (
                      <Link
                        className="mt-1 inline-block text-primary hover:underline"
                        to={`/policies/${result.matchedPolicyId}`}
                      >
                        {result.matchedPolicyName}
                      </Link>
                    ) : (
                      <p className="mt-1">Environment default</p>
                    )}
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">
                      Reviewer role
                    </p>
                    <p className="mt-1">
                      {result.reviewerRole || "Not required"}
                    </p>
                  </div>
                  {result.decision === "review" && (
                    <p className="rounded-md bg-amber-50 p-3 text-sm">
                      This action would wait for approval. Human approval
                      requests are created for real evaluations, not previews.
                    </p>
                  )}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">
                  Run a test to inspect the matching rule and outcome.
                </p>
              )}
            </CardContent>
          </Card>
        </div>
      )}
    </>
  );
}
