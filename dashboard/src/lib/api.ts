export type Role = 'Owner' | 'Admin' | 'Developer' | 'Reviewer' | 'Viewer'
export type Organization = { id: string; name: string; slug: string; status: string; role: Role }
export type Account = { user: { id: string; email: string; name: string }; organization: Organization }
export type Session = Account & { accessToken: string; expiresAt: string }
const baseUrl = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '')
let session: Session | null = null
let refreshing: Promise<Session> | null = null
const listeners = new Set<(value: Session | null) => void>()

export function subscribeSession(listener: (value: Session | null) => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}
function publish(value: Session | null) {
  session = value
  for (const listener of listeners) listener(value)
}
async function raw<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('X-AgentGate-Client', 'dashboard')
  if (init.body) headers.set('Content-Type', 'application/json')
  if (session) headers.set('Authorization', `Bearer ${session.accessToken}`)
  const response = await fetch(`${baseUrl}${path.replace(/^\/api(?=\/)/, '')}`, { ...init, headers, credentials: 'same-origin' })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string; errors?: Record<string, string[]> }
    const message = problem.detail || Object.values(problem.errors || {}).flat().join(' ') || problem.title || 'Request failed. Please try again.'
    throw new ApiError(response.status, message)
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}
export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) { super(message); this.status = status }
}
export function refreshSession() {
  if (!refreshing) refreshing = raw<Session>('/api/auth/refresh', { method: 'POST' })
    .then(value => { publish(value); return value })
    .catch((error: unknown) => { publish(null); throw error })
    .finally(() => { refreshing = null })
  return refreshing
}
export async function authenticate(mode: 'login' | 'register', data: Record<string, string>) {
  const value = await raw<Session>(`/api/auth/${mode}`, { method: 'POST', body: JSON.stringify(data) })
  publish(value)
}
export async function logout() {
  await raw<void>('/api/auth/logout', { method: 'POST' })
  publish(null)
}
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  try { return await raw<T>(path, init) }
  catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) throw error
    await refreshSession()
    return raw<T>(path, init)
  }
}
export async function switchOrganization(organizationId: string) {
  const value = await api<Session>('/api/auth/switch-organization', { method: 'POST', body: JSON.stringify({ organizationId }) })
  publish(value)
}
export async function createOrganization(name: string) {
  const value = await api<Session>('/api/auth/organizations', { method: 'POST', body: JSON.stringify({ name }) })
  publish(value)
}
export async function refreshAccount() {
  const account = await api<Account>('/api/auth/me')
  if (session) publish({ ...session, ...account })
}
