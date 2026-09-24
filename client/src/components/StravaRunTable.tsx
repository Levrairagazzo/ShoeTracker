import type { Run, RunType, Shoe } from '../api/types'
import { MoveRunSelect } from './MoveRunSelect'

interface StravaRunTableProps {
  runs: Run[]
  shoes: Shoe[]
  onMoved: (run: Run) => void
}

const TYPE_LABELS: Record<RunType, string> = { Run: 'Run', Trail: 'Trail', Treadmill: 'Treadmill' }

function formatDate(date: string) {
  return new Date(date).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function Badge({ children }: { children: string }) {
  return <span className="rounded-full bg-accent-bg px-2 py-0.5 text-xs font-medium text-accent">{children}</span>
}

export function StravaRunTable({ runs, shoes, onMoved }: StravaRunTableProps) {
  return (
    <div className="mt-2 overflow-x-auto">
      <table className="w-full text-left">
        <thead className="text-xs text-text">
          <tr className="border-b border-border">
            <th className="py-1.5 pr-4 font-medium">Date</th>
            <th className="py-1.5 pr-4 text-right font-medium">Distance</th>
            <th className="py-1.5 pr-4 font-medium">Type</th>
            <th className="py-1.5 pr-4 font-medium">Location</th>
            <th className="py-1.5 font-medium">Shoe</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border text-text-h">
          {runs.map((run) => (
            <tr key={run.id}>
              <td className="py-1.5 pr-4 whitespace-nowrap">{formatDate(run.date)}</td>
              <td className="py-1.5 pr-4 text-right whitespace-nowrap">{run.distanceKm.toFixed(1)} km</td>
              <td className="py-1.5 pr-4">
                <span className="flex flex-wrap items-center gap-1.5">
                  {run.type ? TYPE_LABELS[run.type] : '—'}
                  {run.isUltra && <Badge>Ultra</Badge>}
                  {run.isRace && <Badge>Race</Badge>}
                </span>
              </td>
              <td className="py-1.5 pr-4">{run.location ?? '—'}</td>
              <td className="py-1.5">
                <MoveRunSelect run={run} shoes={shoes} onMoved={onMoved} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
