export const operators = [
  "equals",
  "not_equals",
  "greater_than",
  "greater_than_or_equal",
  "less_than",
  "less_than_or_equal",
  "contains",
  "not_contains",
  "in",
  "not_in",
] as const;
export type PolicyCondition = {
  field: string;
  operator: string;
  value: string | number | boolean | (string | number | boolean)[];
};
export type Policy = {
  id: string;
  name: string;
  description: string;
  actionType: string;
  priority: number;
  enabled: boolean;
  conditions: PolicyCondition[];
  decision: string;
  reviewerRole: string | null;
  riskLevel: string;
  version: number;
  createdAt: string;
  updatedAt: string;
};
export type PolicyResult = {
  decision: string;
  status: string;
  matchedPolicyId: string | null;
  matchedPolicyName: string | null;
  reviewerRole: string | null;
  riskLevel: string;
  reason: string;
  policyUpdatedAt: string | null;
};
