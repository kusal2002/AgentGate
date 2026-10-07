import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Copy, KeyRound } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { RecentAgentActions } from "@/pages/actions";
import {
  formatDate,
  type Agent,
  type ApiKey,
  type GeneratedKey,
} from "@/lib/agents";
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

export function AgentDetailRoute() {
  const { id } = useParams();
  const { session } = useAuth();
  return <AgentDetail key={`${session!.organization.id}:${id}`} id={id!} />;
}
function AgentDetail({ id }: { id: string }) {
  const { session } = useAuth();
  const orgId = session!.organization.id;
  const manager = ["Owner", "Admin", "Developer"].includes(
    session!.organization.role,
  );
  const queryClient = useQueryClient();
  const agent = useQuery({
    queryKey: ["agent", orgId, id],
    queryFn: ({ signal }) => api<Agent>(`/api/agents/${id}`, { signal }),
  });
  const [notice, setNotice] = useState("");
  const update = useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) =>
      api<void>(path, {
        method: body ? "PATCH" : "POST",
        body: body ? JSON.stringify(body) : undefined,
      }),
    onSuccess: async () => {
      setNotice("Agent updated.");
      await queryClient.invalidateQueries({ queryKey: ["agent", orgId, id] });
      await queryClient.invalidateQueries({ queryKey: ["agents", orgId] });
    },
  });
  if (agent.isPending) return <p role="status">Loading agent…</p>;
  if (agent.error)
    return (
      <>
        <Link className="text-sm text-primary underline" to="/agents">
          Back to agents
        </Link>
        <p role="alert" className="mt-4 text-sm text-destructive">
          {agent.error.message}
        </p>
      </>
    );
  const data = agent.data!;
  return (
    <>
      <Link
        className="mb-5 inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-primary"
        to="/agents"
      >
        <ArrowLeft className="size-4" />
        Back to agents
      </Link>
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="break-words text-3xl font-semibold tracking-tight">
            {data.name}
          </h1>
          <div className="mt-3 flex flex-wrap gap-2">
            <Badge variant="outline">{data.environment}</Badge>
            <Badge variant="secondary">{data.status}</Badge>
            <Badge variant="outline">v{data.version}</Badge>
          </div>
        </div>
        {manager && (
          <Button
            variant="outline"
            className={
              data.status === "Active" ? "text-destructive" : "text-primary"
            }
            disabled={update.isPending}
            onClick={() => {
              if (
                window.confirm(
                  data.status === "Active"
                    ? "Disable this agent? All of its API keys will stop authenticating."
                    : "Enable this agent? Its unexpired, unrevoked API keys will work again.",
                )
              )
                update.mutate({
                  path: `/api/agents/${id}/${data.status === "Active" ? "disable" : "enable"}`,
                });
            }}
          >
            {data.status === "Active" ? "Disable agent" : "Enable agent"}
          </Button>
        )}
      </div>
      {notice && (
        <p role="status" className="mb-4 text-sm text-primary">
          {notice}
        </p>
      )}
      {update.error && (
        <p role="alert" className="mb-4 text-sm text-destructive">
          {update.error.message}
        </p>
      )}
      <div className="grid gap-6">
        <Card>
          <CardHeader>
            <CardTitle>Agent details</CardTitle>
            <CardDescription className="break-all">
              ID: {data.id} · {data.slug}
            </CardDescription>
          </CardHeader>
          <CardContent>
            {manager ? (
              <form
                className="grid gap-4 md:grid-cols-2"
                onSubmit={(event) => {
                  event.preventDefault();
                  update.mutate({
                    path: `/api/agents/${id}`,
                    body: Object.fromEntries(new FormData(event.currentTarget)),
                  });
                }}
              >
                <label
                  className="text-sm font-medium"
                  htmlFor="edit-agent-name"
                >
                  Agent name
                  <Input
                    id="edit-agent-name"
                    name="name"
                    className="mt-1.5"
                    defaultValue={data.name}
                    required
                    maxLength={100}
                  />
                </label>
                <label
                  className="text-sm font-medium"
                  htmlFor="edit-agent-version"
                >
                  Version
                  <Input
                    id="edit-agent-version"
                    name="version"
                    className="mt-1.5"
                    defaultValue={data.version}
                    required
                    maxLength={50}
                  />
                </label>
                <label
                  className="text-sm font-medium md:col-span-2"
                  htmlFor="edit-agent-description"
                >
                  Description
                  <Input
                    id="edit-agent-description"
                    name="description"
                    className="mt-1.5"
                    defaultValue={data.description}
                    maxLength={2000}
                  />
                </label>
                <div className="md:col-span-2">
                  <Button disabled={update.isPending}>Save details</Button>
                </div>
              </form>
            ) : (
              <p className="text-sm text-muted-foreground">
                {data.description || "No description provided."}
              </p>
            )}
            <div className="mt-5 grid gap-3 border-t pt-5 text-xs text-muted-foreground sm:grid-cols-2">
              <p>Created: {formatDate(data.createdAt)}</p>
              <p>
                Last activity:{" "}
                {formatDate(data.lastActivityAt, "No activity yet")}
              </p>
            </div>
          </CardContent>
        </Card>
        <AgentKeys agent={data} manager={manager} />
        <RecentAgentActions agentId={data.id} orgId={orgId} />
      </div>
    </>
  );
}

function AgentKeys({ agent, manager }: { agent: Agent; manager: boolean }) {
  const queryClient = useQueryClient();
  const orgId = agent.organizationId;
  const [secret, setSecret] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [expiryPreset, setExpiryPreset] = useState("oneMonth");
  const keys = useQuery({
    queryKey: ["agent-keys", orgId, agent.id],
    queryFn: ({ signal }) =>
      api<ApiKey[]>(`/api/agents/${agent.id}/keys`, { signal }),
    refetchInterval: 30_000,
  });
  const refresh = async () => {
    await queryClient.invalidateQueries({
      queryKey: ["agent-keys", orgId, agent.id],
    });
    await queryClient.invalidateQueries({
      queryKey: ["agent", orgId, agent.id],
    });
  };
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <KeyRound className="size-4 text-primary" />
          API keys
        </CardTitle>
        <CardDescription>
          Full keys are shown once when generated. Only their prefixes are
          available afterward.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {agent.status === "Disabled" && (
          <p className="mb-4 rounded-md bg-muted p-3 text-sm">
            This agent is disabled. Its keys cannot authenticate, and new keys
            cannot be generated.
          </p>
        )}
        {error && (
          <p role="alert" className="mb-4 text-sm text-destructive">
            {error}
          </p>
        )}
        {notice && (
          <p role="status" className="mb-4 text-sm text-primary">
            {notice}
          </p>
        )}
        {secret && (
          <div className="mb-5 rounded-lg border border-amber-300 bg-amber-50 p-4">
            <h2 className="text-sm font-semibold">Save your API key now</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Store it securely in your agent's environment. You cannot retrieve
              this full key again.
            </p>
            <Input
              aria-label="Generated API key"
              className="mt-3 font-mono text-xs"
              value={secret}
              readOnly
              autoComplete="off"
            />
            <div className="mt-3 flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={async () => {
                  try {
                    await navigator.clipboard.writeText(secret);
                    setNotice("Key copied.");
                  } catch {
                    setError(
                      "Could not copy. Select and copy the key manually.",
                    );
                  }
                }}
              >
                <Copy className="size-3" />
                Copy key
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setSecret("");
                  setNotice("");
                }}
              >
                Dismiss key
              </Button>
            </div>
          </div>
        )}
        {manager && agent.status === "Active" && (
          <form
            className="mb-5 flex flex-wrap items-end gap-3 rounded-lg border bg-background p-4"
            onSubmit={async (event) => {
              event.preventDefault();
              const form = event.currentTarget;
              const data = new FormData(form);
              const expiry = String(data.get("expiresAt") || "");
              setBusy(true);
              setError("");
              setNotice("");
              setSecret("");
              try {
                const generated = await api<GeneratedKey>(
                  `/api/agents/${agent.id}/keys`,
                  {
                    method: "POST",
                    body: JSON.stringify({
                      name: data.get("name"),
                      ...(expiryPreset === "custom"
                        ? { expiresAt: new Date(expiry).toISOString() }
                        : { expiryPreset }),
                    }),
                  },
                );
                setSecret(generated.key);
                form.reset();
                setExpiryPreset("oneMonth");
                await refresh();
              } catch (cause) {
                setError(
                  cause instanceof Error
                    ? cause.message
                    : "Key generation failed.",
                );
              } finally {
                setBusy(false);
              }
            }}
          >
            <label className="min-w-44 flex-1 text-sm" htmlFor="key-name">
              Key name
              <Input
                id="key-name"
                name="name"
                className="mt-1.5"
                placeholder="Local refund agent"
                maxLength={100}
                required
              />
            </label>
            <label className="text-sm" htmlFor="key-expiry">
              Expiry
              <select
                id="key-expiry"
                value={expiryPreset}
                onChange={(event) => setExpiryPreset(event.target.value)}
                className="mt-1.5 flex h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value="oneWeek">Expires in 1 week</option>
                <option value="oneMonth">Expires in 1 month</option>
                <option value="sixMonths">Expires in 6 months</option>
                <option value="never">No expiry</option>
                <option value="custom">Custom date</option>
              </select>
            </label>
            {expiryPreset === "custom" && (
              <label className="text-sm" htmlFor="key-custom-expiry">
                Expiry date and time
                <Input
                  id="key-custom-expiry"
                  name="expiresAt"
                  type="datetime-local"
                  className="mt-1.5"
                  required
                />
              </label>
            )}
            <Button disabled={busy}>
              {busy ? "Working…" : "Generate API key"}
            </Button>
          </form>
        )}
        {keys.isPending && (
          <p role="status" className="text-sm">
            Loading keys…
          </p>
        )}
        {keys.error && (
          <p role="alert" className="text-sm text-destructive">
            {keys.error.message}
          </p>
        )}
        {keys.data?.length === 0 && (
          <p className="py-5 text-sm text-muted-foreground">
            No API keys generated yet.
          </p>
        )}
        <div className="divide-y">
          {keys.data?.map((key) => (
            <div
              key={key.id}
              className="flex flex-wrap items-start justify-between gap-3 py-4"
            >
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-sm font-medium">{key.name}</p>
                  <Badge variant="outline">{key.status}</Badge>
                </div>
                <p className="mt-2 break-all font-mono text-xs text-muted-foreground">
                  {key.keyPrefix}…
                </p>
                <p className="mt-2 text-xs text-muted-foreground">
                  Created: {formatDate(key.createdAt)} · Expires:{" "}
                  {formatDate(key.expiresAt, "No expiry")} · Last used:{" "}
                  {formatDate(key.lastUsedAt)}
                </p>
              </div>
              {manager && key.status === "Active" && (
                <Button
                  variant="outline"
                  size="sm"
                  disabled={busy}
                  aria-label={`Revoke ${key.name}`}
                  onClick={async () => {
                    if (
                      !window.confirm(
                        `Revoke ${key.name}? This key will stop authenticating immediately.`,
                      )
                    )
                      return;
                    setBusy(true);
                    setError("");
                    try {
                      await api<void>(
                        `/api/agents/${agent.id}/keys/${key.id}/revoke`,
                        { method: "POST" },
                      );
                      setSecret("");
                      setNotice("Key revoked.");
                      await refresh();
                    } catch (cause) {
                      setError(
                        cause instanceof Error
                          ? cause.message
                          : "Revocation failed.",
                      );
                    } finally {
                      setBusy(false);
                    }
                  }}
                >
                  Revoke
                </Button>
              )}
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}
