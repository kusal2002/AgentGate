import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Plus, ArrowRight } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { formatDate, type Agent } from "@/lib/agents";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function AgentsPage() {
  const { session } = useAuth();
  const orgId = session!.organization.id;
  const manager = ["Owner", "Admin", "Developer"].includes(
    session!.organization.role,
  );
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const agents = useQuery({
    queryKey: ["agents", orgId],
    queryFn: ({ signal }) => api<Agent[]>("/api/agents", { signal }),
  });
  const create = useMutation({
    mutationFn: (data: Record<string, string>) =>
      api<Agent>("/api/agents", { method: "POST", body: JSON.stringify(data) }),
    onSuccess: async (agent) => {
      await queryClient.invalidateQueries({ queryKey: ["agents", orgId] });
      navigate(`/agents/${agent.id}`);
    },
  });
  return (
    <>
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">
            Agent identity
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Agents</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Register your agents and manage how they authenticate.
          </p>
        </div>
        {manager && (
          <Button
            onClick={() => {
              setCreating((value) => !value);
              create.reset();
            }}
          >
            {creating ? (
              "Cancel"
            ) : (
              <>
                <Plus className="size-4" />
                Create agent
              </>
            )}
          </Button>
        )}
      </div>
      {creating && manager && (
        <Card className="mb-6">
          <CardHeader>
            <CardTitle>Create agent</CardTitle>
            <CardDescription>
              The environment stays fixed after registration. Create a separate
              agent for each environment.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form
              className="grid gap-4 md:grid-cols-2"
              onSubmit={(event) => {
                event.preventDefault();
                create.mutate(
                  Object.fromEntries(
                    new FormData(event.currentTarget),
                  ) as Record<string, string>,
                );
              }}
            >
              <label className="text-sm font-medium" htmlFor="agent-name">
                Agent name
                <Input
                  id="agent-name"
                  name="name"
                  className="mt-1.5"
                  placeholder="RefundAgent"
                  maxLength={100}
                  required
                />
              </label>
              <label
                className="text-sm font-medium"
                htmlFor="agent-environment"
              >
                Environment
                <select
                  id="agent-environment"
                  name="environment"
                  className="mt-1.5 h-10 w-full rounded-md border bg-white px-3 text-sm"
                  defaultValue="Development"
                >
                  {["Development", "Staging", "Production"].map((value) => (
                    <option key={value}>{value}</option>
                  ))}
                </select>
              </label>
              <label className="text-sm font-medium" htmlFor="agent-version">
                Version
                <Input
                  id="agent-version"
                  name="version"
                  className="mt-1.5"
                  defaultValue="1.0.0"
                  maxLength={50}
                  required
                />
              </label>
              <label
                className="text-sm font-medium"
                htmlFor="agent-description"
              >
                Description
                <Input
                  id="agent-description"
                  name="description"
                  className="mt-1.5"
                  maxLength={2000}
                  placeholder="What does this agent do?"
                />
              </label>
              {create.error && (
                <p
                  role="alert"
                  className="text-sm text-destructive md:col-span-2"
                >
                  {create.error.message}
                </p>
              )}
              <div className="md:col-span-2">
                <Button disabled={create.isPending}>
                  {create.isPending ? "Creating…" : "Register agent"}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle>Registered agents</CardTitle>
          <CardDescription>
            {manager
              ? "Open an agent to edit its details and manage its API keys."
              : "You have read-only access to agents and key metadata."}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {agents.isPending && (
            <p role="status" className="text-sm">
              Loading agents…
            </p>
          )}
          {agents.error && (
            <p role="alert" className="text-sm text-destructive">
              {agents.error.message}
            </p>
          )}
          {agents.data?.length === 0 && (
            <div className="py-12 text-center">
              <Bot className="mx-auto mb-4 size-10 text-primary" />
              <h2 className="text-lg font-semibold">
                No agents registered yet
              </h2>
              <p className="mt-2 text-sm text-muted-foreground">
                {manager
                  ? "Create your first agent to generate its API key."
                  : "An Owner, Admin, or Developer can register agents."}
              </p>
            </div>
          )}
          {!!agents.data?.length && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b text-xs text-muted-foreground">
                  <tr>
                    {[
                      "Agent",
                      "Environment",
                      "Version",
                      "Status",
                      "Last activity",
                      "Created",
                      "",
                    ].map((label, index) => (
                      <th key={index} className="px-3 py-3 font-medium">
                        {label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {agents.data.map((agent) => (
                    <tr key={agent.id}>
                      <td className="px-3 py-4">
                        <Link
                          to={`/agents/${agent.id}`}
                          className="font-medium text-primary hover:underline"
                        >
                          {agent.name}
                        </Link>
                        <p className="mt-1 text-xs text-muted-foreground">
                          {agent.slug}
                        </p>
                      </td>
                      <td className="px-3 py-4">
                        <Badge variant="outline">{agent.environment}</Badge>
                      </td>
                      <td className="px-3 py-4">{agent.version}</td>
                      <td className="px-3 py-4">
                        <Badge
                          variant={
                            agent.status === "Active" ? "secondary" : "outline"
                          }
                        >
                          {agent.status}
                        </Badge>
                      </td>
                      <td className="whitespace-nowrap px-3 py-4 text-xs">
                        {formatDate(agent.lastActivityAt, "No activity yet")}
                      </td>
                      <td className="whitespace-nowrap px-3 py-4 text-xs">
                        {formatDate(agent.createdAt)}
                      </td>
                      <td className="px-3 py-4">
                        <Link
                          to={`/agents/${agent.id}`}
                          aria-label={`View ${agent.name}`}
                        >
                          <ArrowRight className="size-4" />
                        </Link>
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
