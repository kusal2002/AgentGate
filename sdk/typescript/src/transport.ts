import { AgentGateAbortError, AgentGateProtocolError, AgentGateTimeoutError } from './errors.js';
import { maxBodyBytes } from './validation.js';

export interface Scope { signal: AbortSignal; check(): void; dispose(): void }
export function scope(timeoutMs: number, user: AbortSignal | undefined, operation: string): Scope {
  const timer = new AbortController();
  const handle = setTimeout(() => timer.abort(), timeoutMs);
  const signal = user ? AbortSignal.any([user, timer.signal]) : timer.signal;
  return { signal, check() { if (user?.aborted) throw new AgentGateAbortError(); if (timer.signal.aborted) throw new AgentGateTimeoutError(operation); }, dispose() { clearTimeout(handle); } };
}
export function abortable<T>(promise: Promise<T>, current: Scope): Promise<T> {
  return new Promise((resolve, reject) => {
    const abort = () => { current.signal.removeEventListener('abort', abort); try { current.check(); } catch (error) { reject(error); } };
    current.signal.addEventListener('abort', abort, {once:true});
    promise.then(value => { current.signal.removeEventListener('abort', abort); resolve(value); }, error => { current.signal.removeEventListener('abort', abort); reject(error); });
    if (current.signal.aborted) abort();
  });
}
export async function sleep(ms: number, current: Scope): Promise<void> {
  current.check(); let handle: ReturnType<typeof setTimeout> | undefined;
  try { await abortable(new Promise<void>(resolve => { handle = setTimeout(resolve, ms); }), current); }
  finally { clearTimeout(handle); }
}
export function retryAfter(value: string | null): number | undefined {
  if (value === null || value.length > 128) return undefined;
  if (/^\d+$/.test(value)) return Math.min(Number.MAX_SAFE_INTEGER, Number(value) * 1000);
  const parsed = Date.parse(value); return Number.isFinite(parsed) ? Math.max(0, parsed - Date.now()) : undefined;
}
export function discard(response: Response): void { void response.body?.cancel().catch(() => {}); }
export async function readJson(response: Response, current: Scope): Promise<unknown> {
  if (!response.body || !(response.headers.get('content-type') || '').toLowerCase().includes('json')) { discard(response); throw new AgentGateProtocolError(); }
  const reader = response.body.getReader(); const chunks: Uint8Array[] = []; let length = 0;
  try {
    while (true) {
      const chunk = await abortable(reader.read(), current); current.check();
      if (chunk.done) break;
      length += chunk.value.length;
      if (length > maxBodyBytes) throw new AgentGateProtocolError();
      chunks.push(chunk.value);
    }
    const bytes = new Uint8Array(length); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    return JSON.parse(new TextDecoder('utf-8', {fatal:true}).decode(bytes)) as unknown;
  } catch (error) {
    void reader.cancel().catch(() => {}); current.check();
    if (error instanceof AgentGateProtocolError) throw error;
    throw new AgentGateProtocolError();
  } finally { reader.releaseLock(); }
}
