export type ActionResource = { type: string; id: string };
export type ActionSummary = {
  id: string;
  agentId: string;
  agentName: string;
  action: string;
  resource: ActionResource;
  decision: string;
  status: string;
  riskLevel: string | null;
  createdAt: string;
  testEvaluation: boolean;
};
export type ActionDetail = ActionSummary & {
  updatedAt: string;
  executedAt: string | null;
  matchedPolicyId: string | null;
  matchedPolicyName: string | null;
  reviewerRole: string | null;
  policyUpdatedAt: string | null;
  approvalId: string | null;
  approvalStatus: string | null;
  idempotencyKey: string;
  reason: string;
  parameters: Record<string, unknown>;
  context: Record<string, unknown>;
};
export type ActionPage = {
  items: ActionSummary[];
  total: number;
  page: number;
  pageSize: number;
};

// Payloads may contain credentials. Mask conventional secret fields in the UI.
export function maskSecrets(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(maskSecrets);
  if (value && typeof value === "object")
    return Object.fromEntries(
      Object.entries(value).map(([key, child]) => [
        key,
        /^(password|secret|token|access_token|refresh_token|api_key|authorization|credit_card|card_number)$/i.test(
          key.replace(/([a-z])([A-Z])/g, "$1_$2"),
        )
          ? "[redacted]"
          : maskSecrets(child),
      ]),
    );
  return value;
}
