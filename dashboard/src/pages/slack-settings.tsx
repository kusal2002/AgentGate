import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

type SlackSettings = {
  available: boolean;
  organizationId: string;
  teamId: string | null;
  channelId: string;
  enabled: boolean;
  failedDeliveries: number;
  reviewers: { userId: string; name: string; slackUserId: string }[];
};
type Member = { userId: string; name: string; role: string };
export function SlackSettingsCard() {
  const { session } = useAuth();
  const org = session!.organization;
  const manager = org.role === "Owner" || org.role === "Admin";
  const queryClient = useQueryClient();
  const [message, setMessage] = useState("");
  const settings = useQuery({
    queryKey: ["slack", org.id],
    queryFn: ({ signal }) =>
      api<SlackSettings>("/api/integrations/slack", { signal }),
    refetchInterval: 15_000,
  });
  const members = useQuery({
    queryKey: ["members", org.id],
    queryFn: () => api<Member[]>("/api/organizations/current/members"),
    enabled: manager,
  });
  const save = useMutation({
    mutationFn: ({
      path,
      body,
      method = "PUT",
    }: {
      path: string;
      body?: unknown;
      method?: string;
    }) =>
      api<unknown>(path, {
        method,
        ...(body ? { body: JSON.stringify(body) } : {}),
      }),
    onSuccess: async () => {
      setMessage("Slack settings saved.");
      await queryClient.invalidateQueries({ queryKey: ["slack", org.id] });
    },
    onMutate: () => setMessage(""),
  });
  const data = settings.data;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Slack approvals</CardTitle>
        <CardDescription>
          Send approval requests to your channel and let mapped reviewers
          respond from Slack.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {settings.isPending && (
          <p role="status" className="text-sm">
            Loading Slack settings…
          </p>
        )}
        {settings.error && (
          <p role="alert" className="text-sm text-destructive">
            {settings.error.message}
          </p>
        )}
        {save.error && (
          <p role="alert" className="text-sm text-destructive">
            {save.error.message}
          </p>
        )}
        {message && (
          <p role="status" className="text-sm text-primary">
            {message}
          </p>
        )}
        {data && (
          <>
            {!data.available ? (
              <div className="space-y-2 text-sm text-muted-foreground">
                <p>
                  Configure your Slack app on the server to enable this
                  integration. Follow docs/slack-setup.md, then restart the API.
                </p>
                <p className="break-all">
                  Organization ID: <code>{data.organizationId}</code>
                </p>
              </div>
            ) : (
              <>
                <p className="text-sm">
                  Workspace: {data.teamId} ·{" "}
                  {data.enabled ? "Enabled" : "Disabled"}
                </p>
                {data.failedDeliveries > 0 && (
                  <p role="status" className="text-sm text-destructive">
                    {data.failedDeliveries} delivery attempts need attention.
                    Check bot scopes, channel membership, and credentials.
                    Delivery retries automatically.
                  </p>
                )}
                {manager ? (
                  <form
                    key={`${data.channelId}:${data.enabled}`}
                    className="flex flex-wrap items-end gap-3"
                    onSubmit={(event) => {
                      event.preventDefault();
                      const form = new FormData(event.currentTarget);
                      save.mutate({
                        path: "/api/integrations/slack",
                        body: {
                          channelId: String(form.get("channelId")).trim(),
                          enabled: form.get("enabled") === "on",
                        },
                      });
                    }}
                  >
                    <label
                      className="min-w-48 flex-1 text-sm"
                      htmlFor="slack-channel"
                    >
                      Approval channel ID
                      <Input
                        id="slack-channel"
                        name="channelId"
                        defaultValue={data.channelId}
                        placeholder="C0123456789"
                        maxLength={40}
                        required
                        className="mt-1.5"
                      />
                    </label>
                    <label className="flex h-10 items-center gap-2 text-sm">
                      <input
                        name="enabled"
                        type="checkbox"
                        defaultChecked={data.enabled}
                      />{" "}
                      Enable Slack approvals
                    </label>
                    <Button disabled={save.isPending}>Save channel</Button>
                    <p className="w-full text-xs text-muted-foreground">
                      Invite the bot to this channel. Enabling sends outstanding
                      pending approvals as well as new requests. Changing
                      channels applies to new deliveries; existing messages keep
                      their original channel.
                    </p>
                  </form>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    Owner and Admin roles can manage Slack settings.
                  </p>
                )}
                <div className="border-t pt-4">
                  <h3 className="text-sm font-medium">Reviewer mappings</h3>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Map a Slack member ID to the correct AgentGate account.
                    Their current organization role controls which requests they
                    can resolve.
                  </p>
                  {data.reviewers.length === 0 && (
                    <p className="mt-3 text-sm text-muted-foreground">
                      No reviewers mapped yet.
                    </p>
                  )}
                  {data.reviewers.map((reviewer) => (
                    <div
                      key={reviewer.userId}
                      className="flex flex-wrap items-center justify-between gap-3 border-b py-3"
                    >
                      <p className="text-sm">
                        {reviewer.name}{" "}
                        <span className="text-muted-foreground">
                          · {reviewer.slackUserId}
                        </span>
                      </p>
                      {manager && (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={save.isPending}
                          onClick={() =>
                            save.mutate({
                              path: `/api/integrations/slack/reviewers/${reviewer.userId}`,
                              method: "DELETE",
                            })
                          }
                        >
                          Remove mapping
                        </Button>
                      )}
                    </div>
                  ))}
                  {manager && (
                    <form
                      className="mt-4 flex flex-wrap items-end gap-3"
                      onSubmit={(event) => {
                        event.preventDefault();
                        const form = new FormData(event.currentTarget);
                        save.mutate({
                          path: "/api/integrations/slack/reviewers",
                          body: {
                            userId: form.get("userId"),
                            slackUserId: String(form.get("slackUserId")).trim(),
                          },
                        });
                      }}
                    >
                      <label
                        className="min-w-48 flex-1 text-sm"
                        htmlFor="slack-member"
                      >
                        AgentGate member
                        <select
                          id="slack-member"
                          name="userId"
                          className="mt-1.5 block h-10 w-full rounded-md border bg-background px-3"
                          required
                        >
                          {members.data?.map((member) => (
                            <option key={member.userId} value={member.userId}>
                              {member.name} ({member.role})
                            </option>
                          ))}
                        </select>
                      </label>
                      <label
                        className="min-w-48 flex-1 text-sm"
                        htmlFor="slack-user"
                      >
                        Slack member ID
                        <Input
                          id="slack-user"
                          name="slackUserId"
                          className="mt-1.5"
                          placeholder="U0123456789"
                          maxLength={40}
                          required
                        />
                      </label>
                      <Button
                        disabled={save.isPending || !members.data?.length}
                      >
                        Save mapping
                      </Button>
                    </form>
                  )}
                </div>
              </>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
