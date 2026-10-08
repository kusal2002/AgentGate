import { AgentGateProtocolError, AgentGateValidationError } from './errors.js';
import type { ApprovalResult, EvaluationResult, EvaluateRequest } from './types.js';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const statuses = ['created', 'awaiting_approval', 'approved', 'rejected', 'denied', 'executing', 'executed', 'failed', 'cancelled'];
const approvals = ['pending', 'approved', 'rejected', 'expired', 'cancelled'];
export const maxBodyBytes = 65_536;
export function integer(value: number, name: string, min: number, max: number): number {
  if (!Number.isInteger(value) || value < min || value > max) throw new AgentGateValidationError(`${name} must be an integer from ${min} to ${max}.`);
  return value;
}
export function id(value: string): string {
  if (typeof value !== 'string' || !uuid.test(value)) throw new AgentGateValidationError('Provide a full approval UUID.');
  return value.toLowerCase();
}
export function baseUrl(value: string): URL {
  let url: URL;
  try { url = new URL(value); } catch { throw new AgentGateValidationError('Provide a valid AgentGate base URL.'); }
  const local = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && local)) || url.username || url.password || url.search || url.hash)
    throw new AgentGateValidationError('Use HTTPS or loopback HTTP, without URL credentials, query strings, or fragments.');
  url.pathname = url.pathname.replace(/\/+$/, '') + '/';
  return url;
}
function object(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value); }
function plain(value: unknown): value is Record<string, unknown> {
  return object(value) && [Object.prototype, null].includes(Object.getPrototypeOf(value))
    && Object.values(Object.getOwnPropertyDescriptors(value)).every(descriptor => !descriptor.get && !descriptor.set);
}
function member(value: unknown, choices: string[]): value is string { return typeof value === 'string' && choices.includes(value); }
function text(value: unknown, max: number): value is string { return typeof value === 'string' && value.length > 0 && value.length <= max && value === value.trim() && !Array.from(value).some(char => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127); }
function identifier(value: unknown): boolean { return text(value, 100) && /^[a-z][a-z0-9_.:-]*$/.test(value); }
function json(value: unknown, depth = 0, parents = new Set<unknown>()): void {
  if (depth > 16) throw new AgentGateValidationError('JSON nesting exceeds 16 levels.');
  if (value === null || typeof value === 'boolean' || typeof value === 'string' && !value.includes('\0') || typeof value === 'number' && Number.isFinite(value)) return;
  if (object(value) || Array.isArray(value)) {
    const array = Array.isArray(value);
    const validArray = array && Object.getPrototypeOf(value) === Array.prototype
      && Reflect.ownKeys(value).every(key => typeof key === 'string' && (key === 'length' || /^(0|[1-9]\d*)$/.test(key)))
      && Object.values(Object.getOwnPropertyDescriptors(value)).every(descriptor => !descriptor.get && !descriptor.set);
    if (parents.has(value) || (array ? !validArray : !plain(value))) throw new AgentGateValidationError('Parameters and context must contain plain JSON values without cycles or accessors.');
    parents.add(value);
    for (const child of Array.isArray(value) ? value : Object.values(value)) json(child, depth + 1, parents);
    parents.delete(value); return;
  }
  throw new AgentGateValidationError('Parameters and context must contain finite JSON values.');
}
export function body(request: EvaluateRequest): string {
  try {
    if (!plain(request) || Object.keys(request).some(key => !['action','resource','parameters','idempotencyKey','context'].includes(key))
        || !identifier(request.action) || !plain(request.resource) || Object.keys(request.resource).some(key => !['type','id'].includes(key))
        || !identifier(request.resource.type) || !text(request.resource.id, 200) || !text(request.idempotencyKey, 200)
        || !object(request.parameters) || request.context !== undefined && !object(request.context))
      throw new AgentGateValidationError('Provide action, resource, JSON parameters, and a nonempty idempotency key within their limits.');
    json(request.parameters); if (request.context !== undefined) json(request.context);
    if (new TextEncoder().encode(JSON.stringify(request.parameters)).length > 32_768 || request.context !== undefined && new TextEncoder().encode(JSON.stringify(request.context)).length > 32_768)
      throw new AgentGateValidationError('Parameters and context are limited to 32 KiB each.');
    const serialized = JSON.stringify(request);
    if (new TextEncoder().encode(serialized).length > maxBodyBytes) throw new AgentGateValidationError('The action request exceeds 64 KiB.');
    return serialized;
  } catch (error) {
    if (error instanceof AgentGateValidationError) throw error;
    throw new AgentGateValidationError('The action request could not be serialized as JSON.');
  }
}
function timestamp(value: unknown): boolean { return typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value)); }
function optionalText(value: unknown): boolean { return value === null || typeof value === 'string'; }
function consistent(status: string, actionStatus: string): boolean {
  return status === 'pending' ? actionStatus === 'awaiting_approval' : status === 'approved' ? ['approved','executing','executed','failed'].includes(actionStatus)
    : status === 'rejected' ? actionStatus === 'rejected' : actionStatus === 'cancelled';
}
export function evaluation(value: unknown): EvaluationResult {
  if (!object(value) || typeof value.actionId !== 'string' || !uuid.test(value.actionId) || !member(value.decision, ['allow','review','deny'])
    || !member(value.status, statuses) || typeof value.reason !== 'string' || value.testEvaluation !== false
    || !(value.matchedPolicyId === null || typeof value.matchedPolicyId === 'string' && uuid.test(value.matchedPolicyId))
    || !optionalText(value.matchedPolicyName) || !optionalText(value.reviewerRole)
    || !(value.riskLevel === null || member(value.riskLevel, ['Low','Medium','High','Critical']))
    || !(value.policyUpdatedAt === null || timestamp(value.policyUpdatedAt))) throw new AgentGateProtocolError();
  if (value.decision === 'review') {
    if (typeof value.approvalId !== 'string' || !uuid.test(value.approvalId) || !member(value.approvalStatus, approvals)
      || !timestamp(value.approvalExpiresAt) || !text(value.reviewerRole, 20) || !consistent(value.approvalStatus, value.status)) throw new AgentGateProtocolError();
  } else if (value.approvalId !== null || value.approvalStatus !== null || value.approvalExpiresAt !== null
    || (value.decision === 'deny' ? value.status !== 'denied' : !['approved','executing','executed','failed'].includes(value.status))) throw new AgentGateProtocolError();
  return value as unknown as EvaluationResult;
}
export function approval(value: unknown, expectedId: string): ApprovalResult {
  if (!object(value) || typeof value.id !== 'string' || value.id.toLowerCase() !== expectedId
    || typeof value.actionId !== 'string' || !uuid.test(value.actionId) || !member(value.status, approvals)
    || !member(value.actionStatus, statuses) || !consistent(value.status, value.actionStatus)
    || !text(value.reviewerRole, 20) || !timestamp(value.requestedAt) || !timestamp(value.expiresAt)
    || (value.status === 'pending' ? value.resolvedAt !== null : !timestamp(value.resolvedAt))) throw new AgentGateProtocolError();
  return value as unknown as ApprovalResult;
}
