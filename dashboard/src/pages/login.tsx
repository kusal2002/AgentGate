import { useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { ShieldCheck } from 'lucide-react'
import { useAuth } from '@/auth/auth-provider'
import { authenticate } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

export function Login({ register = false }: { register?: boolean }) {
  const { session, ready } = useAuth()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  if (ready && session) return <Navigate to="/" replace />
  return <main className="flex min-h-screen items-center justify-center px-5 py-10"><div className="w-full max-w-md"><div className="mb-7 flex items-center justify-center gap-2 text-2xl font-semibold"><ShieldCheck className="size-7 text-primary" />AgentGate</div><Card><CardHeader><CardTitle className="text-xl">{register ? 'Create your workspace' : 'Welcome back'}</CardTitle><CardDescription>{register ? 'Create an account and become the owner of your organization.' : 'Sign in to your agent authorization workspace.'}</CardDescription></CardHeader><CardContent><form className="space-y-4" onSubmit={async event => {
    event.preventDefault(); setError(''); setBusy(true)
    const data = Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>
    try { await authenticate(register ? 'register' : 'login', data); navigate('/', { replace: true }) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to sign in.') }
    finally { setBusy(false) }
  }}>
    {register && <><label className="block text-sm font-medium" htmlFor="name">Your name<Input className="mt-1.5" id="name" name="name" autoComplete="name" maxLength={100} required /></label><label className="block text-sm font-medium" htmlFor="organizationName">Organization name<Input className="mt-1.5" id="organizationName" name="organizationName" maxLength={100} required /></label></>}
    <label className="block text-sm font-medium" htmlFor="email">Email<Input className="mt-1.5" id="email" name="email" type="email" autoComplete="email" maxLength={254} required /></label>
    <label className="block text-sm font-medium" htmlFor="password">Password<Input className="mt-1.5" id="password" name="password" type="password" autoComplete={register ? 'new-password' : 'current-password'} minLength={register ? 12 : 1} maxLength={128} required /></label>
    {register && <p className="text-xs text-muted-foreground">Use at least 12 characters.</p>}
    {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    <Button className="w-full" disabled={busy || !ready}>{busy ? 'Please wait…' : register ? 'Create account' : 'Sign in'}</Button>
  </form><p className="mt-5 text-center text-sm text-muted-foreground">{register ? 'Already have an account?' : 'New to AgentGate?'}{' '}<Link className="font-medium text-primary underline underline-offset-4" to={register ? '/login' : '/register'}>{register ? 'Sign in' : 'Create an account'}</Link></p></CardContent></Card><p className="mt-6 text-center text-xs text-muted-foreground">The authorization layer for AI agents.</p></div></main>
}
