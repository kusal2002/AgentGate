import { createContext, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Navigate, Outlet } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { refreshSession, subscribeSession, type Session } from '@/lib/api'

const AuthContext = createContext<{ session: Session | null; ready: boolean }>({ session: null, ready: false })
export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null)
  const [ready, setReady] = useState(false)
  const queryClient = useQueryClient()
  useEffect(() => {
    let mounted = true
    let previousOrganization: string | undefined
    const unsubscribe = subscribeSession(value => {
      if (previousOrganization !== value?.organization.id) queryClient.clear()
      previousOrganization = value?.organization.id
      setSession(value)
    })
    void refreshSession().catch(() => {}).finally(() => { if (mounted) setReady(true) })
    return () => { mounted = false; unsubscribe() }
  }, [queryClient])
  return <AuthContext.Provider value={{ session, ready }}>{children}</AuthContext.Provider>
}
export function useAuth() { return useContext(AuthContext) }
export function RequireAuth() {
  const { session, ready } = useAuth()
  if (!ready) return <div className="flex min-h-screen items-center justify-center text-sm text-muted-foreground" role="status">Loading your workspace…</div>
  return session ? <Outlet /> : <Navigate to="/login" replace />
}
