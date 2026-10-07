import { NavLink, Route, Routes } from 'react-router-dom'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { RequireAuth, useAuth } from '@/auth/auth-provider'
import { Login } from '@/pages/login'
import { SettingsPage } from '@/pages/settings'
import { api, logout, switchOrganization, type Organization } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Activity, BookOpen, Bot, ClipboardCheck, FileClock, LayoutDashboard, Settings, ShieldCheck, SlidersHorizontal } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Overview } from '@/pages/overview'
import { Placeholder } from '@/pages/placeholder'
import { cn } from '@/lib/utils'

const navigation = [
  { path: '/', label: 'Overview', icon: LayoutDashboard },
  { path: '/agents', label: 'Agents', icon: Bot },
  { path: '/approvals', label: 'Approvals', icon: ClipboardCheck },
  { path: '/policies', label: 'Policies', icon: SlidersHorizontal },
  { path: '/audit-log', label: 'Audit log', icon: FileClock },
  { path: '/settings', label: 'Settings', icon: Settings },
]

export default function App() {
  return <Routes><Route path="/login" element={<Login />} /><Route path="/register" element={<Login register />} /><Route element={<RequireAuth />}><Route path="/*" element={<Workspace />} /></Route></Routes>
}

function Workspace() {
  const { session } = useAuth()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const organizations = useQuery({ queryKey: ['organizations'], queryFn: () => api<Organization[]>('/api/organizations') })
  return <div className="min-h-screen md:grid md:grid-cols-[230px_1fr]">
    <aside className="flex flex-col border-b bg-white p-4 md:min-h-screen md:border-r md:border-b-0 md:p-5">
      <NavLink to="/" className="mb-7 flex items-center gap-2 text-xl font-semibold tracking-tight"><span className="rounded-lg bg-primary p-1.5 text-white"><ShieldCheck className="size-5" /></span>AgentGate</NavLink>
      <p className="mb-3 hidden px-3 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted-foreground md:block">Workspace</p>
      <nav aria-label="Main navigation" className="flex flex-wrap gap-1 md:flex-col">{navigation.map(item => <NavLink key={item.path} to={item.path} end={item.path === '/'} className={({ isActive }) => cn('flex items-center gap-3 rounded-md px-3 py-2.5 text-sm transition-colors', isActive ? 'bg-emerald-50 font-medium text-primary' : 'text-muted-foreground hover:bg-secondary hover:text-foreground')}><item.icon className="size-4" />{item.label}</NavLink>)}</nav>
      <div className="mt-auto hidden pt-16 md:block"><div className="rounded-lg border bg-background p-3"><div className="flex items-center gap-2 text-xs font-medium"><BookOpen className="size-4 text-primary" />Local development</div><p className="mt-2 text-xs leading-relaxed text-muted-foreground">Setup instructions and architecture notes are in the repository README.</p></div></div>
    </aside>
    <div className="min-w-0"><header className="flex min-h-16 flex-wrap items-center justify-between gap-3 border-b bg-white px-5 py-3 md:px-9"><select aria-label="Organization" className="max-w-56 rounded-md border bg-white px-2 py-1 text-sm" value={session!.organization.id} disabled={busy} onChange={async event => { setBusy(true); setError(''); try { await switchOrganization(event.target.value) } catch (cause) { setError(cause instanceof Error ? cause.message : 'Switch failed.') } finally { setBusy(false) } }}>{(organizations.data || [session!.organization]).map(org => <option key={org.id} value={org.id}>{org.name}</option>)}</select><div className="flex items-center gap-3"><Badge variant="outline"><Activity className="mr-1 size-3" />{session!.organization.role}</Badge><span className="hidden text-sm sm:inline">{session!.user.name}</span><Button variant="ghost" size="sm" disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await logout() } catch (cause) { setError(cause instanceof Error ? cause.message : 'Sign out failed.') } finally { setBusy(false) } }}>Sign out</Button></div></header>
      <main className="mx-auto max-w-6xl px-5 py-8 md:px-9 md:py-10">{error && <p role="alert" className="mb-4 text-sm text-destructive">{error}</p>}<Routes><Route path="/" element={<Overview />} /><Route path="/settings" element={<SettingsPage key={session!.organization.id} />} />{navigation.slice(1).filter(item => item.path !== '/settings').map(item => <Route key={item.path} path={item.path} element={<Placeholder title={item.label} />} />)}<Route path="*" element={<Placeholder title="Page" />} /></Routes><footer className="mt-10 flex flex-wrap items-center justify-between gap-2 border-t pt-4 text-xs text-muted-foreground"><span>AgentGate · The authorization layer for AI agents.</span><span>Phase 2 / Organizations & authentication</span></footer></main>
    </div>
  </div>
}
