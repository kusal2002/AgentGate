import type { ActionDetail } from "@/lib/actions";
export type ApprovalSummary = {
  id: string;
  actionId: string;
  agentId: string;
  agentName: string;
  action: string;
  resource: { type: string; id: string };
  status: string;
  reviewerRole: string;
  riskLevel: string | null;
  matchedPolicyName: string | null;
  requestedAt: string;
  expiresAt: string;
  resolvedAt: string | null;
  amount: number | null;
  currency: string | null;
};
export type ApprovalDetail = {
  approval: ApprovalSummary;
  action: ActionDetail;
  canReview: boolean;
  resolvedByUserId: string | null;
  reviewerComment: string;
  decisions: {
    id: string;
    reviewerUserId: string;
    reviewerName: string;
    decision: string;
    comment: string;
    createdAt: string;
  }[];
};
export type ApprovalPage = {
  items: ApprovalSummary[];
  total: number;
  page: number;
  pageSize: number;
};
