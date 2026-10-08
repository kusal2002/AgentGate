export type AgentGateErrorCode = 'invalid_input' | 'http_error' | 'network_error' | 'invalid_response' | 'timeout' | 'aborted';
export class AgentGateError extends Error {
  readonly code: AgentGateErrorCode;
  constructor(code: AgentGateErrorCode, message: string) { super(message); this.name = new.target.name; this.code = code; }
}
export class AgentGateValidationError extends AgentGateError {
  constructor(message: string) { super('invalid_input', message); }
}
export class AgentGateHttpError extends AgentGateError {
  readonly statusCode: number;
  readonly retryAfterMs: number | undefined;
  constructor(statusCode: number, retryAfterMs?: number) {
    const messages: Record<number, string> = {
      400: 'AgentGate rejected the request. Check its fields and limits.',
      401: 'AgentGate authentication failed. Check the agent API key and agent status.',
      403: 'This agent is not authorized to access the requested result.',
      404: 'The requested AgentGate resource was not found.',
      409: 'The idempotency key was used for a different action. Use a new key only for a new action.',
      413: 'The AgentGate request exceeds the server size limit.',
      429: 'AgentGate rate limit reached. Retry after the indicated delay.'
    };
    super('http_error', messages[statusCode] ?? `AgentGate HTTP request failed with status ${statusCode}.`);
    this.statusCode = statusCode; this.retryAfterMs = retryAfterMs;
  }
}
export class AgentGateNetworkError extends AgentGateError {
  constructor() { super('network_error', 'AgentGate could not be reached. Authorization was not granted.'); }
}
export class AgentGateProtocolError extends AgentGateError {
  constructor() { super('invalid_response', 'AgentGate returned an invalid or inconsistent response. Authorization was not granted.'); }
}
export class AgentGateTimeoutError extends AgentGateError {
  readonly operation: string;
  constructor(operation: string) { super('timeout', `AgentGate ${operation} timed out. Inspect the stored outcome before proceeding.`); this.operation = operation; }
}
export class AgentGateAbortError extends AgentGateError {
  constructor() { super('aborted', 'AgentGate operation was cancelled. Inspect the stored outcome before proceeding.'); }
}
