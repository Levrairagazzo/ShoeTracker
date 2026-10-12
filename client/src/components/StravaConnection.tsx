import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Run, Shoe, StravaStatus } from '../api/types'
import { StravaRunTable } from './StravaRunTable'

const LATEST_RUNS_SHOWN = 10
const UNASSIGNED_PAGE = 20

interface StravaConnectionProps {
  shoes: Shoe[]
  /** Called after runs move between shoes or the default shoe changes, so shoe totals reload. */
  onRunsChanged: () => void
}

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

function pluralRuns(count: number) {
  return `${count} run${count === 1 ? '' : 's'}`
}

export function StravaConnection({ shoes, onRunsChanged }: StravaConnectionProps) {
  const [status, setStatus] = useState<StravaStatus | null>(null)
  const [latestRuns, setLatestRuns] = useState<Run[]>([])
  const [unassignedRuns, setUnassignedRuns] = useState<Run[]>([])
  const [unassignedShown, setUnassignedShown] = useState(UNASSIGNED_PAGE)
  const [result] = useState(takeResultFromUrl)
  const [notice, setNotice] = useState<Notice | null>(result ? (RESULT_NOTICES[result] ?? RESULT_NOTICES.error) : null)
  const [busy, setBusy] = useState(false)
  const autoImportStarted = useRef(false)

  const refreshStatus = useCallback(async () => {
    const next = await shoeTrackerClient.stravaStatus()
    setStatus(next)
    setLatestRuns(next.importedRuns > 0 ? await shoeTrackerClient.latestStravaRuns(LATEST_RUNS_SHOWN) : [])
    setUnassignedRuns(next.unassignedRuns > 0 ? await shoeTrackerClient.unassignedRuns(unassignedShown) : [])
  }, [unassignedShown])

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
        onRunsChanged()
      } catch (error) {
        const detail = error instanceof ApiError ? error.problem?.detail : undefined
        setNotice({ text: detail ?? 'Importing from Strava failed. Please try again.', isError: true })
      } finally {
        setBusy(false)
        await refreshStatus().catch(() => {})
      }
    },
    [refreshStatus, onRunsChanged],
  )

  // Re-reads the lists whenever the shoes reload too, since that's how moves made from a
  // shoe card show up here.
  useEffect(() => {
    refreshStatus().catch(() => setStatus(null))
  }, [refreshStatus, shoes])

  useEffect(() => {
    // Import the history straight away after connecting. The ref stops StrictMode's
    // double-run of effects in dev from starting two imports at once.
    if (result === 'connected' && !autoImportStarted.current) {
      autoImportStarted.current = true
      runImport('Strava connected. ')
    }
  }, [result, runImport])

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

  async function handleDefaultShoeChange(value: string) {
    try {
      await shoeTrackerClient.setDefaultShoe(value === '' ? null : Number(value))
      onRunsChanged()
    } catch {
      setNotice({ text: 'Could not change the default shoe. Please try again.', isError: true })
    }
  }

  if (!status?.available && !notice) return null

  const defaultShoe = shoes.find((s) => s.isDefault)
  const activeShoes = shoes.filter((s) => !s.isRetired)

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

      {status?.connected && (
        <label className="flex flex-wrap items-center gap-2 text-sm">
          New Strava runs go to
          <select
            value={defaultShoe?.id.toString() ?? ''}
            onChange={(e) => handleDefaultShoeChange(e.target.value)}
            className="rounded-md border border-border bg-bg px-2 py-1 text-sm text-text-h"
          >
            <option value="">No shoe (leave unassigned)</option>
            {activeShoes.map((shoe) => (
              <option key={shoe.id} value={shoe.id}>
                {shoe.name}
              </option>
            ))}
          </select>
        </label>
      )}

      {status && status.unassignedRuns > 0 && (
        <details className="max-w-3xl text-sm">
          <summary className="cursor-pointer font-medium text-text-h">
            Unassigned runs ({status.unassignedRuns})
          </summary>
          <StravaRunTable
            runs={unassignedRuns}
            shoes={shoes}
            onMoved={() => {
              refreshStatus().catch(() => {})
              onRunsChanged()
            }}
          />
          {unassignedRuns.length < status.unassignedRuns && (
            <button
              type="button"
              onClick={() => setUnassignedShown((n) => n + UNASSIGNED_PAGE)}
              className="mt-2 rounded-full px-3 py-1 font-medium text-accent hover:bg-accent-bg"
            >
              Show more
            </button>
          )}
        </details>
      )}

      {latestRuns.length > 0 && (
        <details className="max-w-3xl text-sm">
          <summary className="cursor-pointer font-medium text-text-h">Latest imported runs</summary>
          <StravaRunTable
            runs={latestRuns}
            shoes={shoes}
            onMoved={() => {
              refreshStatus().catch(() => {})
              onRunsChanged()
            }}
          />
        </details>
      )}
    </section>
  )
}
