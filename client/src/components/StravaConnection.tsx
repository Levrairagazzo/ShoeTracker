import { useEffect, useState } from 'react'
import { shoeTrackerClient } from '../api/shoeTrackerClient'
import type { StravaStatus } from '../api/types'

// The API's /strava/callback redirects back to "/?strava=<result>".
const RESULT_MESSAGES: Record<string, { text: string; isError: boolean }> = {
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

export function StravaConnection() {
  const [status, setStatus] = useState<StravaStatus | null>(null)
  const [result] = useState(takeResultFromUrl)
  const [dismissed, setDismissed] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    shoeTrackerClient.stravaStatus().then(setStatus).catch(() => setStatus(null))
  }, [])

  async function handleDisconnect() {
    setBusy(true)
    try {
      await shoeTrackerClient.disconnectStrava()
      setStatus(await shoeTrackerClient.stravaStatus())
    } finally {
      setBusy(false)
    }
  }

  const message = result && !dismissed ? (RESULT_MESSAGES[result] ?? RESULT_MESSAGES.error) : null

  if (!status?.available && !message) return null

  return (
    <section className="mb-6 flex flex-col gap-3">
      {message && (
        <div
          role="status"
          className={`flex items-center justify-between gap-3 rounded-xl border px-4 py-3 text-sm ${
            message.isError ? 'border-red-200 bg-red-50 text-red-700' : 'border-border bg-border/30 text-text-h'
          }`}
        >
          <p>{message.text}</p>
          <button type="button" onClick={() => setDismissed(true)} className="shrink-0 font-medium hover:underline">
            Dismiss
          </button>
        </div>
      )}

      {status?.available && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
          {status.connected ? (
            <>
              <span>
                Strava: connected{status.athleteName ? ` as ${status.athleteName}` : ''}
              </span>
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
    </section>
  )
}
