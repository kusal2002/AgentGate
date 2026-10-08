export type AuditEvent = {
  id: string;
  organizationId: string;
  agentId: string | null;
  actionId: string | null;
  approvalRequestId: string | null;
  eventType: string;
  actorType: string;
  actorId: string | null;
  metadata: Record<string, unknown>;
  ipAddress: string | null;
  createdAt: string;
  agentName: string | null;
  actionType: string | null;
  resourceType: string | null;
  resourceId: string | null;
  decision: string | null;
  riskLevel: string | null;
  reviewerId: string | null;
  reviewerName: string | null;
  actorName: string | null;
};
export type AuditPage = {
  items: AuditEvent[];
  total: number;
  page: number;
  pageSize: number;
};
export type AuditOptions = {
  actions: string[];
  reviewers: { id: string; name: string }[];
};
export const auditTypes = [
  "agent.action_requested",
  "policy.evaluated",
  "action.allowed",
  "action.denied",
  "action.review_required",
  "approval.created",
  "approval.approved",
  "approval.rejected",
  "approval.expired",
  "approval.slack_sent",
  "approval.slack_updated",
  "agent.created",
  "agent.updated",
  "agent.disabled",
  "agent.enabled",
  "agent.key_created",
  "agent.key_revoked",
  "policy.created",
  "policy.updated",
  "action.imported",
];
export function eventLabel(type: string) {
  return type.replaceAll(".", " · ").replaceAll("_", " ");
}
