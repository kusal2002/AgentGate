import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '@/auth/auth-provider'
import { api, createOrganization, refreshAccount, type Role } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { SlackSettingsCard } from '@/pages/slack-settings'

type Member = { id: string; userId: string; email: string; name: string; role: Role }
const selectClass = 'h-10 rounded-md border bg-white px-3 text-sm focus-visible:outline-primary'
export function SettingsPage() {
  const { session } = useAuth()
  const queryClient = useQueryClient()
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const organization = session!.organization
  const manager = organization.role === 'Owner' || organization.role === 'Admin'
  const roles: Role[] = organization.role === 'Owner' ? ['Admin', 'Developer', 'Reviewer', 'Viewer'] : ['Developer', 'Reviewer', 'Viewer']
  const members = useQuery({ queryKey: ['members', organization.id], queryFn: () => api<Member[]>('/api/organizations/current/members') })
  const mutation = useMutation({
    mutationFn: async ({ path, body, method }: { path: string; body: unknown; method: string }) => api<void>(path, { method, body: JSON.stringify(body) }),
    onSuccess: async () => { setMessage('Changes saved.'); setError(''); await refreshAccount(); await queryClient.invalidateQueries() },
    onError: (cause: Error) => { setError(cause.message); setMessage('') },
  })
  return <><h1 className="text-3xl font-semibold tracking-tight">Organization settings</h1><p className="mt-2 mb-8 text-sm text-muted-foreground">Manage your organization and its members.</p>
    {error && <p role="alert" className="mb-4 text-sm text-destructive">{error}</p>}{message && <p role="status" className="mb-4 text-sm text-primary">{message}</p>}
    <div className="grid gap-6"><Card><CardHeader><CardTitle>{organization.name}</CardTitle><CardDescription>Your role: {organization.role} · {organization.slug}</CardDescription></CardHeader><CardContent>{manager ? <form className="flex flex-wrap gap-3" onSubmit={event => { event.preventDefault(); const name = new FormData(event.currentTarget).get('name'); mutation.mutate({ path: '/api/organizations/current', body: { name }, method: 'PATCH' }) }}><label className="min-w-48 flex-1 text-sm" htmlFor="org-name">Organization name<Input id="org-name" name="name" className="mt-1.5" defaultValue={organization.name} maxLength={100} required /></label><Button className="self-end" disabled={mutation.isPending}>Save name</Button></form> : <p className="text-sm text-muted-foreground">Owner and Admin roles can change organization settings.</p>}</CardContent></Card>
    <Card><CardHeader><CardTitle>Members</CardTitle><CardDescription>Roles apply only inside this organization. Ownership changes are not available yet.</CardDescription></CardHeader><CardContent>
      {members.isPending && <p className="text-sm" role="status">Loading members…</p>}{members.error && <p role="alert" className="text-sm text-destructive">{members.error.message}</p>}
      <div className="divide-y">{members.data?.map(member => <div key={member.id} className="flex flex-wrap items-center justify-between gap-3 py-4"><div><p className="text-sm font-medium">{member.name}</p><p className="mt-1 break-all text-xs text-muted-foreground">{member.email}</p></div>{manager && member.role !== 'Owner' && (organization.role === 'Owner' || member.role !== 'Admin') ? <select aria-label={`Role for ${member.email}`} value={member.role} disabled={mutation.isPending} className={selectClass} onChange={event => mutation.mutate({ path: `/api/organizations/current/members/${member.id}`, body: { role: event.target.value }, method: 'PATCH' })}>{roles.map(role => <option key={role}>{role}</option>)}</select> : <span className="text-sm">{member.role}</span>}</div>)}</div>
      {manager && <form className="mt-5 flex flex-wrap items-end gap-3 border-t pt-5" onSubmit={event => { event.preventDefault(); const form = event.currentTarget; const data = Object.fromEntries(new FormData(form)); mutation.mutate({ path: '/api/organizations/current/members', body: data, method: 'POST' }, { onSuccess: () => form.reset() }) }}><label className="min-w-48 flex-1 text-sm" htmlFor="member-email">Add registered user<Input id="member-email" name="email" type="email" className="mt-1.5" placeholder="colleague@company.com" required /></label><label className="text-sm" htmlFor="new-role">Role<select id="new-role" name="role" className={`${selectClass} mt-1.5 block`} defaultValue="Viewer">{roles.map(role => <option key={role}>{role}</option>)}</select></label><Button disabled={mutation.isPending}>Add member</Button><p className="w-full text-xs text-muted-foreground">The user must create an account before you can add them.</p></form>}
    </CardContent></Card>
    <Card><CardHeader><CardTitle>Create another organization</CardTitle><CardDescription>You will become its owner and switch to the new workspace.</CardDescription></CardHeader><CardContent><form className="flex flex-wrap gap-3" onSubmit={async event => { event.preventDefault(); const name = String(new FormData(event.currentTarget).get('name')); setError(''); try { await createOrganization(name); setMessage('Organization created.') } catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not create organization.') } }}><label className="min-w-48 flex-1 text-sm" htmlFor="new-org-name">New organization name<Input id="new-org-name" name="name" className="mt-1.5" maxLength={100} required /></label><Button className="self-end" variant="outline">Create organization</Button></form></CardContent></Card></div>
    <div className="mt-6"><SlackSettingsCard key={organization.id} /></div>
  </>
}
