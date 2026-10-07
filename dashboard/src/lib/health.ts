type Health = { status: string; service: string }
const baseUrl = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '')

export async function getHealth(path: string, signal: AbortSignal): Promise<Health> {
  const response = await fetch(`${baseUrl}${path}`, { signal })
  if (!response.ok) throw new Error('Service unavailable')
  const data: unknown = await response.json()
  if (!data || typeof data !== 'object' || !('status' in data) || !('service' in data)
    || typeof data.status !== 'string' || typeof data.service !== 'string') {
    throw new Error('Invalid health response')
  }
  return { status: data.status, service: data.service }
}
