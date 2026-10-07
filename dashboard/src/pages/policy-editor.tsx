import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { operators, type Policy, type PolicyCondition } from "@/lib/policies";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

type ConditionDraft = {
  field: string;
  operator: string;
  type: string;
  value: string;
  key: string;
};
const selectClass =
  "mt-1.5 h-9 w-full rounded-md border bg-background px-3 text-sm";
const conditionDraft = (value?: PolicyCondition): ConditionDraft => ({
  field: value?.field || "parameters.amount",
  operator: value?.operator || "greater_than",
  type: value
    ? Array.isArray(value.value)
      ? "array"
      : typeof value.value
    : "number",
  value: value
    ? typeof value.value === "string"
      ? value.value
      : JSON.stringify(value.value)
    : "100",
  key: crypto.randomUUID(),
});

export function PolicyEditorRoute() {
  const { id } = useParams();
  const { session } = useAuth();
  const policy = useQuery({
    queryKey: ["policy", session!.organization.id, id],
    queryFn: ({ signal }) => api<Policy>(`/api/policies/${id}`, { signal }),
    enabled: !!id,
  });
  if (id && policy.isPending) return <p role="status">Loading policy…</p>;
  if (policy.error)
    return (
      <p role="alert" className="text-sm text-destructive">
        {policy.error.message}
      </p>
    );
  return (
    <PolicyEditor
      key={`${session!.organization.id}:${id || "new"}`}
      initial={policy.data}
    />
  );
}

function PolicyEditor({ initial }: { initial?: Policy }) {
  const { session } = useAuth();
  const manager = ["Owner", "Admin"].includes(session!.organization.role);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [conditions, setConditions] = useState<ConditionDraft[]>(
    initial
      ? initial.conditions.map((value) => conditionDraft(value))
      : [conditionDraft()],
  );
  const [decision, setDecision] = useState(initial?.decision || "review");
  const [version] = useState(initial?.version);
  const [error, setError] = useState("");
  const save = useMutation({
    mutationFn: (body: unknown) =>
      api<Policy>(initial ? `/api/policies/${initial.id}` : "/api/policies", {
        method: initial ? "PUT" : "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["policies", session!.organization.id],
      });
      await queryClient.invalidateQueries({
        queryKey: ["policy", session!.organization.id],
      });
      navigate("/policies");
    },
  });
  function update(index: number, field: keyof ConditionDraft, value: string) {
    setConditions((items) =>
      items.map((item, i) =>
        i === index ? { ...item, [field]: value } : item,
      ),
    );
  }
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
        {initial
          ? manager
            ? "Edit policy"
            : "Policy details"
          : "Create policy"}
      </h1>
      {!manager && !initial ? (
        <p className="text-sm text-muted-foreground">
          Only Owners and Admins can create policies.
        </p>
      ) : (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            setError("");
            try {
              const data = Object.fromEntries(
                new FormData(event.currentTarget),
              );
              const parsed = conditions.map((condition) => {
                const value =
                  condition.type === "string"
                    ? condition.value
                    : JSON.parse(condition.value);
                if (
                  (condition.type === "number" &&
                    (typeof value !== "number" || !Number.isFinite(value))) ||
                  (condition.type === "boolean" &&
                    typeof value !== "boolean") ||
                  (condition.type === "array" && !Array.isArray(value))
                )
                  throw new Error(
                    'Condition values must match their selected type. Arrays use JSON, for example ["USD", "EUR"].',
                  );
                return {
                  field: condition.field,
                  operator: condition.operator,
                  value,
                };
              });
              save.mutate({
                name: data.name,
                description: data.description,
                actionType: data.actionType,
                priority: Number(data.priority),
                enabled: data.enabled === "on",
                conditions: parsed,
                decision,
                reviewerRole: decision === "review" ? data.reviewerRole : null,
                riskLevel: data.riskLevel,
                version,
              });
            } catch (cause) {
              setError(
                cause instanceof Error
                  ? cause.message
                  : "Check the condition values.",
              );
            }
          }}
          className="space-y-6"
        >
          <fieldset
            disabled={!manager || save.isPending}
            className="min-w-0 space-y-6"
          >
            <Card>
              <CardHeader>
                <CardTitle>Rule details</CardTitle>
                <CardDescription>
                  Rules apply to this organization. Add agent.environment
                  conditions to limit environments.
                </CardDescription>
              </CardHeader>
              <CardContent className="grid gap-4 sm:grid-cols-2">
                <label className="text-sm" htmlFor="policy-name">
                  Policy name
                  <Input
                    className="mt-1.5"
                    id="policy-name"
                    name="name"
                    defaultValue={initial?.name}
                    maxLength={100}
                    required
                  />
                </label>
                <label className="text-sm" htmlFor="policy-action">
                  Action type
                  <Input
                    className="mt-1.5"
                    id="policy-action"
                    name="actionType"
                    defaultValue={initial?.actionType || "refund"}
                    maxLength={100}
                    required
                  />
                </label>
                <label
                  className="text-sm sm:col-span-2"
                  htmlFor="policy-description"
                >
                  Description
                  <Input
                    className="mt-1.5"
                    id="policy-description"
                    name="description"
                    defaultValue={initial?.description || ""}
                    maxLength={2000}
                  />
                </label>
                <label className="text-sm" htmlFor="policy-priority">
                  Priority
                  <Input
                    className="mt-1.5"
                    id="policy-priority"
                    name="priority"
                    type="number"
                    min={0}
                    max={10000}
                    step={1}
                    defaultValue={initial?.priority ?? 100}
                    required
                  />
                  <span className="mt-1 block text-xs text-muted-foreground">
                    Higher numbers are evaluated first.
                  </span>
                </label>
                <label
                  className="flex items-center gap-2 text-sm"
                  htmlFor="policy-enabled"
                >
                  <input
                    id="policy-enabled"
                    name="enabled"
                    type="checkbox"
                    defaultChecked={initial?.enabled ?? false}
                  />
                  Enabled for new requests
                </label>
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>When all conditions match</CardTitle>
                <CardDescription>
                  Use parameters.amount, context.customerTier, resource.type/id,
                  or agent.environment/id. Values are compared by type and case.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                {conditions.map((condition, index) => (
                  <div
                    key={condition.key}
                    className="grid min-w-0 gap-3 rounded-lg border bg-background p-4 sm:grid-cols-2 lg:grid-cols-[1.2fr_1.2fr_.7fr_1fr_auto]"
                  >
                    <label
                      className="min-w-0 text-xs"
                      htmlFor={`field-${condition.key}`}
                    >
                      Field
                      <Input
                        className="mt-1.5"
                        id={`field-${condition.key}`}
                        value={condition.field}
                        maxLength={200}
                        required
                        onChange={(event) =>
                          update(index, "field", event.target.value)
                        }
                      />
                    </label>
                    <label
                      className="min-w-0 text-xs"
                      htmlFor={`operator-${condition.key}`}
                    >
                      Operator
                      <select
                        className={selectClass}
                        id={`operator-${condition.key}`}
                        value={condition.operator}
                        onChange={(event) =>
                          update(index, "operator", event.target.value)
                        }
                      >
                        {operators.map((operator) => (
                          <option key={operator} value={operator}>
                            {operator.replaceAll("_", " ")}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label
                      className="min-w-0 text-xs"
                      htmlFor={`type-${condition.key}`}
                    >
                      Value type
                      <select
                        className={selectClass}
                        id={`type-${condition.key}`}
                        value={condition.type}
                        onChange={(event) =>
                          update(index, "type", event.target.value)
                        }
                      >
                        {["number", "string", "boolean", "array"].map(
                          (type) => (
                            <option key={type}>{type}</option>
                          ),
                        )}
                      </select>
                    </label>
                    <label
                      className="min-w-0 text-xs"
                      htmlFor={`value-${condition.key}`}
                    >
                      Value
                      <Input
                        className="mt-1.5"
                        id={`value-${condition.key}`}
                        value={condition.value}
                        required
                        maxLength={4000}
                        onChange={(event) =>
                          update(index, "value", event.target.value)
                        }
                      />
                    </label>
                    {manager && (
                      <Button
                        type="button"
                        className="self-end"
                        variant="ghost"
                        size="sm"
                        aria-label={`Remove condition ${index + 1}`}
                        onClick={() =>
                          setConditions((items) =>
                            items.filter((_, i) => i !== index),
                          )
                        }
                      >
                        <Trash2 className="size-4" />
                      </Button>
                    )}
                  </div>
                ))}
                {conditions.length === 0 && (
                  <p className="rounded-md bg-amber-50 p-3 text-sm">
                    With no conditions, this rule matches every valid request
                    for its action type.
                  </p>
                )}
                {manager && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={conditions.length >= 32}
                    onClick={() =>
                      setConditions((items) => [...items, conditionDraft()])
                    }
                  >
                    <Plus className="size-4" />
                    Add condition
                  </Button>
                )}
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Then return</CardTitle>
                <CardDescription>
                  Review holds the action for human approval. Approval handling
                  comes in Phase 6.
                </CardDescription>
              </CardHeader>
              <CardContent className="grid gap-4 sm:grid-cols-3">
                <label className="text-sm" htmlFor="policy-decision">
                  Decision
                  <select
                    id="policy-decision"
                    className={selectClass}
                    value={decision}
                    onChange={(event) => setDecision(event.target.value)}
                  >
                    {["allow", "review", "deny"].map((value) => (
                      <option key={value}>{value}</option>
                    ))}
                  </select>
                </label>
                <label className="text-sm" htmlFor="policy-risk">
                  Risk level
                  <select
                    id="policy-risk"
                    name="riskLevel"
                    className={selectClass}
                    defaultValue={initial?.riskLevel || "Medium"}
                  >
                    {["Low", "Medium", "High", "Critical"].map((value) => (
                      <option key={value}>{value}</option>
                    ))}
                  </select>
                </label>
                {decision === "review" && (
                  <label className="text-sm" htmlFor="policy-reviewer">
                    Reviewer role
                    <select
                      id="policy-reviewer"
                      name="reviewerRole"
                      className={selectClass}
                      defaultValue={initial?.reviewerRole || "Reviewer"}
                    >
                      {["Reviewer", "Admin", "Owner"].map((value) => (
                        <option key={value}>{value}</option>
                      ))}
                    </select>
                  </label>
                )}
              </CardContent>
            </Card>
          </fieldset>
          {(error || save.error) && (
            <p role="alert" className="text-sm text-destructive">
              {error || save.error?.message}
            </p>
          )}
          {manager && (
            <Button disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Save policy"}
            </Button>
          )}
        </form>
      )}
    </>
  );
}
