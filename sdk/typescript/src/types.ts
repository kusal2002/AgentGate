export type JsonValue = string | number | boolean | null | JsonObject | JsonValue[];
export interface JsonObject { [key: string]: JsonValue }
export type Decision = 'allow' | 'review' | 'deny';
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';
export type ActionStatus = 'created' | 'awaiting_approval' | 'approved' | 'rejected' | 'denied' | 'executing' | 'executed' | 'failed' | 'cancelled';
export type ApprovalStatus = 'pending' | 'approved' | 'rejected' | 'expired' | 'cancelled';
export interface EvaluateRequest {
  action: string;
  resource: { type: string; id: string };
  parameters: JsonObject;
  idempotencyKey: string;
  context?: JsonObject;
}
interface EvaluationBase {
  actionId: string; status: ActionStatus; reason: string; testEvaluation: false;
  matchedPolicyId: string | null; matchedPolicyName: string | null; reviewerRole: string | null;
  riskLevel: RiskLevel | null; policyUpdatedAt: string | null;
}
export interface AllowEvaluation extends EvaluationBase {
  decision: 'allow'; approvalId: null; approvalStatus: null; approvalExpiresAt: null;
}
export interface DenyEvaluation extends EvaluationBase {
  decision: 'deny'; approvalId: null; approvalStatus: null; approvalExpiresAt: null;
}
export interface ReviewEvaluation extends EvaluationBase {
  decision: 'review'; approvalId: string; approvalStatus: ApprovalStatus; approvalExpiresAt: string;
}
export type EvaluationResult = AllowEvaluation | DenyEvaluation | ReviewEvaluation;
export interface ApprovalResult {
  id: string; actionId: string; status: ApprovalStatus; actionStatus: ActionStatus;
  reviewerRole: string; requestedAt: string; expiresAt: string; resolvedAt: string | null;
}
export type ResolvedApproval = Omit<ApprovalResult, 'status'> & { status: Exclude<ApprovalStatus, 'pending'> };
export interface RequestOptions { signal?: AbortSignal; timeoutMs?: number }
export interface WaitOptions extends RequestOptions { pollIntervalMs?: number }
export interface AgentGateOptions {
  apiKey: string; baseUrl: string;
  requestTimeoutMs?: number; maxRetries?: number; retryDelayMs?: number; maxRetryDelayMs?: number;
  /** A trusted Fetch-compatible transport; it must honor redirect and credential settings. */
  fetch?: typeof globalThis.fetch;
}
