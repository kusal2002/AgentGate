import { NavLink } from 'react-router-dom'
import { ArrowRight, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'

export function Placeholder({ title }: { title: string }) {
  return <><h1 className="mb-2 text-3xl font-semibold tracking-tight">{title}</h1><p className="mb-8 text-sm text-muted-foreground">Workspace foundation</p><Card><CardContent className="flex flex-col items-center py-16 text-center"><ShieldCheck className="mb-4 size-10 text-primary" /><h2 className="text-lg font-semibold">{title} will be available in a later phase</h2><p className="mt-2 max-w-md text-sm leading-relaxed text-muted-foreground">This phase establishes the project and local services. No {title.toLowerCase()} features or sample records have been created.</p><Button variant="outline" className="mt-6" asChild><NavLink to="/">Back to overview<ArrowRight className="size-4" /></NavLink></Button></CardContent></Card></>
}
