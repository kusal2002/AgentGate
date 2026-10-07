import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, FlaskConical, SlidersHorizontal } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import type { Policy } from "@/lib/policies";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function PoliciesPage() {
  const { session } = useAuth();
  const orgId = session!.organization.id;
  const manager = ["Owner", "Admin"].includes(session!.organization.role);
  const tester = manager || session!.organization.role === "Developer";
  const queryClient = useQueryClient();
  const policies = useQuery({
    queryKey: ["policies", orgId],
    queryFn: ({ signal }) => api<Policy[]>("/api/policies", { signal }),
  });
  const change = useMutation({
    mutationFn: async (policy: Policy | null) => {
      if (policy)
        await api<Policy>(`/api/policies/${policy.id}/status`, {
          method: "POST",
          body: JSON.stringify({
            enabled: !policy.enabled,
            version: policy.version,
          }),
        });
      else
        await api<Policy[]>("/api/policies/seed-refund-demo", {
          method: "POST",
        });
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["policies", orgId] }),
    onError: () => {
      void queryClient.invalidateQueries({ queryKey: ["policies", orgId] });
    },
  });
  return (
    <>
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Authorization rules
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Policies</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Control which actions your agents can take.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {tester && (
            <Button variant="outline" asChild>
              <Link to="/policies/test">
                <FlaskConical className="size-4" />
                Test policies
              </Link>
            </Button>
          )}
          {manager && (
            <Button asChild>
              <Link to="/policies/new">
                <Plus className="size-4" />
                Create policy
              </Link>
            </Button>
          )}
        </div>
      </div>
      <Card className="mb-6">
        <CardHeader>
          <CardTitle>How rules are selected</CardTitle>
          <CardDescription className="leading-relaxed">
            The matching rule with the highest priority wins. At equal priority,
            deny takes precedence over review and allow. All conditions within a
            rule must match. Requests with no matching rule use the environment
            default.
          </CardDescription>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          Review outcomes wait for human approval. The approval workflow is
          coming in Phase 6.
        </CardContent>
      </Card>
      {change.error && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          {change.error.message}
        </p>
      )}
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>Organization policies</CardTitle>
              <CardDescription className="mt-2">
                {manager
                  ? "Edit rules or disable them without changing historical decisions."
                  : "You have read-only access to policy definitions."}
              </CardDescription>
            </div>
            {manager && import.meta.env.DEV && (
              <Button
                variant="outline"
                size="sm"
                disabled={change.isPending}
                onClick={() => {
                  if (
                    window.confirm(
                      "Add four sample USD refund policies for Development agents? Existing sample policies will be preserved.",
                    )
                  )
                    change.mutate(null);
                }}
              >
                Add refund demo policies
              </Button>
            )}
          </div>
        </CardHeader>
        <CardContent>
          {policies.isPending && <p role="status">Loading policies…</p>}
          {policies.error && (
            <p role="alert" className="text-sm text-destructive">
              {policies.error.message}
            </p>
          )}
          {policies.data?.length === 0 && (
            <div className="py-12 text-center">
              <SlidersHorizontal className="mx-auto mb-4 size-10 text-primary" />
              <h2 className="text-lg font-semibold">No policies yet</h2>
              <p className="mt-2 text-sm text-muted-foreground">
                An Owner or Admin can create the first rule. Environment
                defaults apply until a rule matches.
              </p>
            </div>
          )}
          {!!policies.data?.length && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b text-xs text-muted-foreground">
                  <tr>
                    {[
                      "Policy",
                      "Action",
                      "Decision",
                      "Risk",
                      "Reviewer",
                      "Priority",
                      "Status",
                      "",
                    ].map((label, i) => (
                      <th className="px-3 py-3 font-medium" key={i}>
                        {label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {policies.data.map((policy) => (
                    <tr key={policy.id}>
                      <td className="px-3 py-4">
                        <Link
                          className="font-medium text-primary hover:underline"
                          to={`/policies/${policy.id}`}
                        >
                          {policy.name}
                        </Link>
                      </td>
                      <td className="px-3 py-4">{policy.actionType}</td>
                      <td className="px-3 py-4">
                        <Badge variant="outline">{policy.decision}</Badge>
                      </td>
                      <td className="px-3 py-4">{policy.riskLevel}</td>
                      <td className="px-3 py-4">
                        {policy.reviewerRole || "—"}
                      </td>
                      <td className="px-3 py-4">{policy.priority}</td>
                      <td className="px-3 py-4">
                        <Badge
                          variant={policy.enabled ? "secondary" : "outline"}
                        >
                          {policy.enabled ? "Enabled" : "Disabled"}
                        </Badge>
                      </td>
                      <td className="px-3 py-4">
                        {manager && (
                          <Button
                            variant="outline"
                            size="sm"
                            disabled={change.isPending}
                            onClick={() => {
                              if (
                                window.confirm(
                                  `${policy.enabled ? "Disable" : "Enable"} ${policy.name}? This affects new action requests.`,
                                )
                              )
                                change.mutate(policy);
                            }}
                          >
                            {policy.enabled ? "Disable" : "Enable"}
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  );
}
