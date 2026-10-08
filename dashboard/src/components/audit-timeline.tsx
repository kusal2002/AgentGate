import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/auth/auth-provider";
import { api } from "@/lib/api";
import { eventLabel, type AuditEvent, type AuditPage } from "@/lib/audit";
import { formatDate } from "@/lib/agents";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function AuditEventRow({ event }: { event: AuditEvent }) {
  return (
    <li className="min-w-0 border-l-2 border-emerald-200 pb-6 pl-4 last:pb-0">
      <div className="flex flex-wrap items-center gap-2">
        <Link
          to={`/audit-log/${event.id}`}
          className="break-words text-sm font-medium text-primary hover:underline"
        >
          {eventLabel(event.eventType)}
        </Link>
        <Badge variant="secondary">{event.actorType}</Badge>
      </div>
      <p className="mt-1 text-xs text-muted-foreground">
        {formatDate(event.createdAt)}
      </p>
      {event.actorId && (
        <p className="mt-1 break-all font-mono text-xs text-muted-foreground">
          Actor: {event.actorId}
        </p>
      )}
      {event.actionId && (
        <Link
          className="mt-2 inline-block break-all text-xs text-primary hover:underline"
          to={`/actions/${event.actionId}`}
        >
          Action {event.actionId}
        </Link>
      )}
      <details className="mt-2 text-xs">
        <summary className="cursor-pointer text-muted-foreground">
          Event details
        </summary>
        <pre className="mt-2 overflow-auto whitespace-pre-wrap break-all rounded-md bg-secondary p-3">
          {JSON.stringify(event.metadata, null, 2)}
        </pre>
      </details>
    </li>
  );
}
export function AuditTimeline({ actionId }: { actionId: string }) {
  const { session } = useAuth();
  const [page, setPage] = useState(1);
  const timeline = useQuery({
    queryKey: ["audit-timeline", session!.organization.id, actionId, page],
    queryFn: ({ signal }) =>
      api<AuditPage>(`/api/actions/${actionId}/timeline?page=${page}`, {
        signal,
      }),
    refetchInterval: 5000,
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle role="heading" aria-level={2}>
          Audit timeline
        </CardTitle>
        <CardDescription>
          Oldest first. Earlier requests contain an imported snapshot; new
          requests have a full event timeline.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {timeline.isPending && (
          <p role="status" className="text-sm">
            Loading timeline…
          </p>
        )}
        {timeline.error && (
          <p role="alert" className="text-sm text-destructive">
            {timeline.error.message}
          </p>
        )}
        {timeline.data && (
          <>
            <ol className="space-y-1">
              {timeline.data.items.map((event) => (
                <AuditEventRow key={event.id} event={event} />
              ))}
            </ol>
            {timeline.data.total === 0 && (
              <p className="text-sm text-muted-foreground">
                No audit events recorded for this action yet.
              </p>
            )}
            <div className="mt-5 flex flex-wrap items-center gap-3">
              <Button
                variant="outline"
                size="sm"
                disabled={page === 1}
                onClick={() => setPage(page - 1)}
              >
                Previous
              </Button>
              <span className="text-xs text-muted-foreground">
                Page {page} · {timeline.data.total} events
              </span>
              <Button
                variant="outline"
                size="sm"
                disabled={page * 25 >= timeline.data.total}
                onClick={() => setPage(page + 1)}
              >
                Next
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  void timeline.refetch();
                }}
              >
                Refresh
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
