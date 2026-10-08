import { AgentGateError, AgentGateHttpError, AgentGateNetworkError, AgentGateValidationError } from './errors.js';
import { abortable, discard, readJson, retryAfter, scope, sleep } from './transport.js';
import { approval, baseUrl, body, evaluation, id, integer } from './validation.js';
import type { AgentGateOptions, ApprovalResult, EvaluateRequest, EvaluationResult, RequestOptions, ResolvedApproval, WaitOptions } from './types.js';

const transient = new Set([408,429,500,502,503,504]);
export class AgentGate {
  #apiKey: string; #baseUrl: URL; #fetch: typeof globalThis.fetch;
  #timeout: number; #retries: number; #retryDelay: number; #maxRetryDelay: number;
  constructor(options: AgentGateOptions) {
    if (typeof document !== 'undefined') throw new AgentGateValidationError('AgentGate API keys must be used in a server-side runtime.');
    if (!options || typeof options.apiKey !== 'string' || !/^[\x21-\x7e]{1,249}$/.test(options.apiKey)) throw new AgentGateValidationError('Provide a nonempty agent API key without spaces or control characters.');
    this.#apiKey = options.apiKey; this.#baseUrl = baseUrl(options.baseUrl);
    this.#fetch = options.fetch ?? globalThis.fetch.bind(globalThis);
    if (typeof this.#fetch !== 'function') throw new AgentGateValidationError('A Fetch-compatible transport is required.');
    this.#timeout = integer(options.requestTimeoutMs ?? 10_000, 'requestTimeoutMs', 1, 120_000);
    this.#retries = integer(options.maxRetries ?? 2, 'maxRetries', 0, 5);
    this.#retryDelay = integer(options.retryDelayMs ?? 500, 'retryDelayMs', 1, 10_000);
    this.#maxRetryDelay = integer(options.maxRetryDelayMs ?? 30_000, 'maxRetryDelayMs', 1, 120_000);
  }
  async evaluate(request: EvaluateRequest, options: RequestOptions = {}): Promise<EvaluationResult> {
    const serialized = body(request); // Snapshot once: retries never observe caller mutations.
    return evaluation(await this.#request('v1/actions/evaluate', serialized, options, 'evaluate'));
  }
  async getApproval(approvalId: string, options: RequestOptions = {}): Promise<ApprovalResult> {
    const normalized = id(approvalId);
    return approval(await this.#request(`v1/approvals/${normalized}`, undefined, options, 'getApproval'), normalized);
  }
  async waitForApproval(approvalId: string, options: WaitOptions = {}): Promise<ResolvedApproval> {
    const normalized = id(approvalId);
    const timeout = integer(options.timeoutMs ?? 600_000, 'timeoutMs', 1, 604_800_000);
    const interval = integer(options.pollIntervalMs ?? 2000, 'pollIntervalMs', 100, 60_000);
    const current = scope(timeout, options.signal, 'waitForApproval');
    try {
      while (true) {
        current.check();
        const result = await abortable(this.getApproval(normalized, {signal:current.signal}), current);
        current.check();
        if (result.status !== 'pending') return result as ResolvedApproval;
        await sleep(interval, current);
      }
    } finally { current.dispose(); }
  }
  toJSON(): { name: string } { return {name:'AgentGate'}; }
  async #request(path: string, serialized: string | undefined, options: RequestOptions, operation: string): Promise<unknown> {
    const timeout = integer(options.timeoutMs ?? this.#timeout, 'timeoutMs', 1, 120_000);
    const current = scope(timeout, options.signal, operation);
    try {
      for (let attempt = 0; ; attempt++) {
        current.check(); let response: Response;
        try {
          const headers = new Headers({Authorization:`Bearer ${this.#apiKey}`,Accept:'application/json'});
          if (serialized !== undefined) headers.set('Content-Type','application/json');
          response = await abortable(this.#fetch(new URL(path,this.#baseUrl), {method:serialized === undefined?'GET':'POST',headers,
            ...(serialized === undefined?{}:{body:serialized}),signal:current.signal,redirect:'error',credentials:'omit',cache:'no-store'}), current);
        } catch (error) {
          current.check();
          if (error instanceof AgentGateError) throw error;
          if (attempt >= this.#retries) throw new AgentGateNetworkError();
          await sleep(Math.min(this.#maxRetryDelay,this.#retryDelay * 2 ** attempt),current); continue;
        }
        current.check();
        if (response.ok) return await readJson(response,current);
        const after = retryAfter(response.headers.get('retry-after')); discard(response);
        const error = new AgentGateHttpError(response.status, after);
        if (!transient.has(response.status) || attempt >= this.#retries) throw error;
        const delay = Math.max(after ?? 0, Math.min(this.#maxRetryDelay,this.#retryDelay * 2 ** attempt));
        if (delay > this.#maxRetryDelay) throw error; // Never retry sooner than Retry-After.
        await sleep(delay,current);
      }
    } finally {current.dispose();}
  }
}
