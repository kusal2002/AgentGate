import { useQuery } from '@tanstack/react-query'
import { ArrowRight, Check, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { getHealth } from '@/lib/health'

export function Overview() {
  const api = useQuery({ queryKey: ['health'], queryFn: ({ signal }) => getHealth('/health', signal), retry: false, refetchInterval: 30_000 })
  const database = useQuery({ queryKey: ['readiness'], queryFn: ({ signal }) => getHealth('/health/ready', signal), retry: false, refetchInterval: 30_000 })
  const apiHealthy = api.data?.status === 'ok' && !api.isError
  const databaseHealthy = database.data?.status === 'healthy' && !database.isError
  const checking = api.isFetching || database.isFetching
  const services = [
    { title: 'API service', state: api.isPending ? 'Checking' : apiHealthy ? 'Online' : 'Unavailable', detail: apiHealthy ? 'AgentGate is responding.' : 'Start the backend on port 5000.', good: apiHealthy },
    { title: 'PostgreSQL', state: database.isPending ? 'Checking' : databaseHealthy ? 'Connected' : 'Unavailable', detail: databaseHealthy ? 'Database readiness check passed.' : 'Start PostgreSQL and configure its credentials.', good: databaseHealthy },
    { title: 'Dashboard', state: 'Online', detail: 'React workspace is running.', good: true },
  ]

  return <>
    <div className="mb-8 flex flex-wrap items-end justify-between gap-4">
      <div><p className="mb-2 text-xs font-semibold uppercase tracking-widest text-primary">Workspace overview</p><h1 className="text-3xl font-semibold tracking-tight">Your agents. Your rules.</h1><p className="mt-2 text-sm text-muted-foreground">Control what your AI agents can do, with approval and accountability.</p></div>
      <Button variant="outline" disabled={checking} onClick={() => { void api.refetch(); void database.refetch() }}><RefreshCw className={cn('size-4', checking && 'animate-spin')} />Refresh status</Button>
    </div>
    <Card className="mb-6 border-primary/20 bg-gradient-to-br from-white to-emerald-50/60">
      <CardHeader><div className="flex items-center gap-2"><Badge variant="secondary">Phase 1</Badge><span className="text-xs text-muted-foreground">Foundation</span></div><CardTitle className="mt-3 text-xl">A foundation for safe agent actions</CardTitle><CardDescription className="max-w-2xl leading-relaxed">The workspace is ready for development. Agent identity, deterministic policies, human approvals, and audit history will be added in the next phases.</CardDescription></CardHeader>
      <CardContent><div className="flex flex-wrap items-center gap-3 text-sm"><span className="rounded-md border bg-white px-3 py-2">Agent request</span><ArrowRight className="size-4 text-muted-foreground" /><span className="rounded-md border border-primary/30 bg-white px-3 py-2 font-medium text-primary">AgentGate</span><ArrowRight className="size-4 text-muted-foreground" /><div className="flex gap-2"><Badge className="bg-emerald-100 text-emerald-800">Allow</Badge><Badge className="bg-amber-100 text-amber-800">Review</Badge><Badge className="bg-red-100 text-red-800">Deny</Badge></div></div><p className="mt-4 text-xs text-muted-foreground">Planned authorization flow · Business features are not enabled yet.</p></CardContent>
    </Card>
    <div className="mb-6 grid gap-4 md:grid-cols-3">{services.map(item => <Card key={item.title}><CardHeader className="pb-2"><CardDescription>{item.title}</CardDescription><CardTitle className="flex items-center gap-2 text-xl"><span className={cn('size-2 rounded-full', item.good ? 'bg-emerald-600' : 'bg-amber-600')} />{item.state}</CardTitle></CardHeader><CardContent className="text-xs text-muted-foreground">{item.detail}</CardContent></Card>)}</div>
    <div className="grid gap-6 lg:grid-cols-2">
      <Card><CardHeader><CardTitle>Development checklist</CardTitle><CardDescription>The building blocks of this workspace.</CardDescription></CardHeader><CardContent className="space-y-4">{['Clean Architecture backend on .NET 10', 'PostgreSQL persistence configuration', 'React, TypeScript, and Tailwind dashboard', 'Health endpoints and local environment setup'].map(label => <div key={label} className="flex items-center gap-3 text-sm"><span className="rounded-full bg-emerald-50 p-1 text-primary"><Check className="size-3" /></span>{label}</div>)}</CardContent></Card>
      <Card><CardHeader><CardTitle>What comes next</CardTitle><CardDescription>Build the authorization layer one phase at a time.</CardDescription></CardHeader><CardContent className="space-y-4">{[{ step: '02', title: 'Organizations & authentication', detail: 'Users, roles, and tenant boundaries.' }, { step: '03', title: 'Agent identity', detail: 'Register agents and issue secure API keys.' }, { step: '04–06', title: 'Actions, policies & approvals', detail: 'Evaluate requests and store approval decisions.' }].map(item => <div key={item.step} className="flex gap-3"><span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-secondary font-mono text-xs">{item.step}</span><div><p className="text-sm font-medium">{item.title}</p><p className="mt-1 text-xs text-muted-foreground">{item.detail}</p></div></div>)}</CardContent></Card>
    </div>
  </>
}
