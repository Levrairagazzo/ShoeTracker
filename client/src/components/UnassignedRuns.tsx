import { useEffect, useState } from 'react'
import { shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Run, Shoe } from '../api/types'
import { StravaRunTable } from './StravaRunTable'

const PAGE = 20

interface UnassignedRunsProps {
  shoes: Shoe[]
  /** Called after a run moves onto a shoe, so shoe totals reload. */
  onRunsChanged: () => void
}

/**
 * The user's runs that aren't assigned to any shoe, with a move control per run. Rendered
 * whether or not Strava is available: manual runs can be unassigned too.
 */
export function UnassignedRuns({ shoes, onRunsChanged }: UnassignedRunsProps) {
  const [runs, setRuns] = useState<Run[]>([])
  const [shown, setShown] = useState(PAGE)
  const [hasMore, setHasMore] = useState(false)

  // Re-reads whenever the shoes reload: everything that can unassign or move a run (moves,
  // shoe deletion, imports) reloads the shoes too.
  useEffect(() => {
    let cancelled = false
    shoeTrackerClient
      .unassignedRuns(shown + 1) // one extra tells us whether more exist
      .then((result) => {
        if (cancelled) return
        setHasMore(result.length > shown)
        setRuns(result.slice(0, shown))
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [shown, shoes])

  if (runs.length === 0) return null

  return (
    <section className="mb-6 max-w-3xl text-sm">
      <details>
        <summary className="cursor-pointer font-medium text-text-h">
          Unassigned runs ({hasMore ? `${runs.length}+` : runs.length})
        </summary>
        <StravaRunTable runs={runs} shoes={shoes} onMoved={() => onRunsChanged()} />
        {hasMore && (
          <button
            type="button"
            onClick={() => setShown((n) => n + PAGE)}
            className="mt-2 rounded-full px-3 py-1 font-medium text-accent hover:bg-accent-bg"
          >
            Show more
          </button>
        )}
      </details>
    </section>
  )
}
