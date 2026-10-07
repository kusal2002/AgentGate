export type Agent = {
  id: string;
  organizationId: string;
  name: string;
  slug: string;
  description: string;
  environment: "Development" | "Staging" | "Production";
  status: "Active" | "Disabled";
  version: string;
  createdAt: string;
  updatedAt: string;
  lastActivityAt: string | null;
};
export type ApiKey = {
  id: string;
  name: string;
  keyPrefix: string;
  environment: string;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  lastUsedAt: string | null;
  status: "Active" | "Expired" | "Revoked";
};
export type GeneratedKey = { key: string; apiKey: ApiKey };
export function formatDate(value: string | null, empty = "Never") {
  return value ? new Date(value).toLocaleString() : empty;
}
