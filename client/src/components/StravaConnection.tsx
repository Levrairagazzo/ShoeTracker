import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Run, RunType, StravaStatus } from '../api/types'

const LATEST_RUNS_SHOWN = 10

interface Notice {
  text: string
  isError: boolean
}

// The API's /strava/callback redirects back to "/?strava=<result>".
const RESULT_NOTICES: Record<string, Notice> = {
  connected: { text: 'Strava connected.', isError: false },
  denied: { text: 'Strava connection cancelled.', isError: false },
  'missing-scope': {
    text: 'Strava was connected without access to all your activities. Connect again and leave that permission checked.',
    isError: true,
  },
  error: { text: 'Could not connect to Strava. Please try again.', isError: true },
}

function takeResultFromUrl(): string | null {
  const url = new URL(window.location.href)
  const result = url.searchParams.get('strava')
  if (result === null) return null
  url.searchParams.delete('strava')
  window.history.replaceState(null, '', url)
  return result
}

function formatDate(date: string) {
  return new Date(date).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const TYPE_LABELS: Record<RunType, string> = { Run: 'Run', Trail: 'Trail', Treadmill: 'Treadmill' }

function Badge({ children }: { children: string }) {
  return <span className="rounded-full bg-accent-bg px-2 py-0.5 text-xs font-medium text-accent">{children}</span>
}

function pluralRuns(count: number) {
  return `${count} run${count === 1 ? '' : 's'}`
}

export function StravaConnection() {
  const [status, setStatus] = useState<StravaStatus | null>(null)
  const [latestRuns, setLatestRuns] = useState<Run[]>([])
  const [result] = useState(takeResultFromUrl)
  const [notice, setNotice] = useState<Notice | null>(result ? (RESULT_NOTICES[result] ?? RESULT_NOTICES.error) : null)
  const [busy, setBusy] = useState(false)
  const autoImportStarted = useRef(false)

  const refreshStatus = useCallback(async () => {
    const next = await shoeTrackerClient.stravaStatus()
    setStatus(next)
    setLatestRuns(next.importedRuns > 0 ? await shoeTrackerClient.latestStravaRuns(LATEST_RUNS_SHOWN) : [])
  }, [])

  const runImport = useCallback(
    async (prefix: string) => {
      setBusy(true)
      setNotice({ text: `${prefix}Importing your Strava runs…`, isError: false })
      try {
        const { imported } = await shoeTrackerClient.importStrava()
        setNotice({
          text: `${prefix}${imported === 0 ? 'No new runs to import.' : `Imported ${pluralRuns(imported)}.`}`,
          isError: false,
        })
      } catch (error) {
        const detail = error instanceof ApiError ? error.problem?.detail : undefined
        setNotice({ text: detail ?? 'Importing from Strava failed. Please try again.', isError: true })
      } finally {
        setBusy(false)
        await refreshStatus().catch(() => {})
      }
    },
    [refreshStatus],
  )

  useEffect(() => {
    refreshStatus().catch(() => setStatus(null))
    // Import the history straight away after connecting. The ref stops StrictMode's
    // double-run of effects in dev from starting two imports at once.
    if (result === 'connected' && !autoImportStarted.current) {
      autoImportStarted.current = true
      runImport('Strava connected. ')
    }
  }, [result, refreshStatus, runImport])

  async function handleDisconnect() {
    setBusy(true)
    try {
      await shoeTrackerClient.disconnectStrava()
      setNotice(null)
      await refreshStatus()
    } finally {
      setBusy(false)
    }
  }

  if (!status?.available && !notice) return null

  return (
    <section className="mb-6 flex flex-col gap-3">
      {notice && (
        <div
          role="status"
          className={`flex items-center justify-between gap-3 rounded-xl border px-4 py-3 text-sm ${
            notice.isError ? 'border-red-200 bg-red-50 text-red-700' : 'border-border bg-border/30 text-text-h'
          }`}
        >
          <p>{notice.text}</p>
          {!busy && (
            <button type="button" onClick={() => setNotice(null)} className="shrink-0 font-medium hover:underline">
              Dismiss
            </button>
          )}
        </div>
      )}

      {status?.available && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
          {status.connected ? (
            <>
              <span>
                Strava: connected{status.athleteName ? ` as ${status.athleteName}` : ''} ·{' '}
                {pluralRuns(status.importedRuns)} imported
              </span>
              <button
                type="button"
                onClick={() => runImport('')}
                disabled={busy}
                className="rounded-full border border-accent-border px-3 py-1 font-medium text-accent hover:bg-accent-bg disabled:opacity-50"
              >
                Import new runs
              </button>
              <button
                type="button"
                onClick={handleDisconnect}
                disabled={busy}
                className="rounded-full px-3 py-1 font-medium text-text hover:bg-border/50 disabled:opacity-50"
              >
                Disconnect
              </button>
            </>
          ) : (
            <a
              href={shoeTrackerClient.stravaConnectUrl}
              className="rounded-full bg-[#FC4C02] px-4 py-2 font-medium text-white hover:opacity-90"
            >
              Connect Strava
            </a>
          )}
        </div>
      )}

      {latestRuns.length > 0 && (
        <details className="max-w-2xl text-sm">
          <summary className="cursor-pointer font-medium text-text-h">Latest imported runs</summary>
          <div className="mt-2 overflow-x-auto">
            <table className="w-full text-left">
              <thead className="text-xs text-text">
                <tr className="border-b border-border">
                  <th className="py-1.5 pr-4 font-medium">Date</th>
                  <th className="py-1.5 pr-4 text-right font-medium">Distance</th>
                  <th className="py-1.5 pr-4 font-medium">Type</th>
                  <th className="py-1.5 font-medium">Location</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border text-text-h">
                {latestRuns.map((run) => (
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
                    <td className="py-1.5">{run.location ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </section>
  )
}
