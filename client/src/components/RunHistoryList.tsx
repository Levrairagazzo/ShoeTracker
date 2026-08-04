import { useEffect, useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Run } from '../api/types'

interface RunHistoryListProps {
  shoeId: number
  onChanged: () => void
}

const inputClass =
  'w-full rounded-md border border-border bg-bg px-2 py-1 text-sm text-text-h outline-none focus:border-accent-border'

function formatDate(date: string) {
  return new Date(date).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

interface RunRowProps {
  shoeId: number
  run: Run
  onChanged: () => void
  onUpdated: (run: Run) => void
  onDeleted: () => void
}

function RunRow({ shoeId, run, onChanged, onUpdated, onDeleted }: RunRowProps) {
  const [isEditing, setIsEditing] = useState(false)
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false)
  const [date, setDate] = useState(run.date)
  const [distanceKm, setDistanceKm] = useState(String(run.distanceKm))
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      const updated = await shoeTrackerClient.updateRun(shoeId, run.id, {
        date,
        distanceKm: Number(distanceKm),
      })
      onUpdated(updated)
      onChanged()
      setIsEditing(false)
    } catch (err) {
      if (err instanceof ApiError) {
        const messages = err.problem ? Object.values(err.problem.errors).flat() : [err.message]
        setError(messages.join(' '))
      } else {
        setError('Something went wrong updating this run.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  async function handleDelete() {
    setSubmitting(true)
    try {
      await shoeTrackerClient.deleteRun(shoeId, run.id)
      onDeleted()
      onChanged()
    } finally {
      setSubmitting(false)
    }
  }

  if (isEditing) {
    return (
      <form onSubmit={handleSave} className="flex flex-col gap-2 rounded-lg border border-border p-2">
        <div className="flex gap-2">
          <input
            className={inputClass}
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            required
          />
          <input
            className={inputClass}
            type="number"
            min="0.1"
            step="0.1"
            value={distanceKm}
            onChange={(e) => setDistanceKm(e.target.value)}
            required
          />
        </div>
        {error && <p className="text-xs text-red-600">{error}</p>}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={() => setIsEditing(false)}
            className="rounded-full px-3 py-1 text-xs font-medium text-text hover:bg-border/50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={submitting}
            className="rounded-full bg-accent px-3 py-1 text-xs font-medium text-white hover:opacity-90 disabled:opacity-60"
          >
            Save
          </button>
        </div>
      </form>
    )
  }

  return (
    <div className="flex items-center justify-between gap-2 py-1.5 text-sm">
      <span className="text-text-h">{formatDate(run.date)}</span>
      <span className="text-text-h">{run.distanceKm.toFixed(1)} km</span>
      {isConfirmingDelete ? (
        <div className="flex items-center gap-2">
          <span className="text-xs text-text">Delete?</span>
          <button
            type="button"
            onClick={handleDelete}
            disabled={submitting}
            className="rounded-full px-2 py-0.5 text-xs font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
          >
            Confirm
          </button>
          <button
            type="button"
            onClick={() => setIsConfirmingDelete(false)}
            className="rounded-full px-2 py-0.5 text-xs font-medium text-text hover:bg-border/50"
          >
            Cancel
          </button>
        </div>
      ) : (
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => setIsEditing(true)}
            className="rounded-full px-2 py-0.5 text-xs font-medium text-accent hover:bg-accent-bg"
          >
            Edit
          </button>
          <button
            type="button"
            onClick={() => setIsConfirmingDelete(true)}
            className="rounded-full px-2 py-0.5 text-xs font-medium text-text hover:bg-border/50"
          >
            Delete
          </button>
        </div>
      )}
    </div>
  )
}

export function RunHistoryList({ shoeId, onChanged }: RunHistoryListProps) {
  const [runs, setRuns] = useState<Run[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    shoeTrackerClient
      .listRuns(shoeId)
      .then((result) => {
        if (!cancelled) setRuns(result)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load run history.')
      })

    return () => {
      cancelled = true
    }
  }, [shoeId])

  if (error) {
    return <p className="text-sm text-red-600">{error}</p>
  }

  if (runs === null) {
    return <p className="text-sm text-text">Loading runs…</p>
  }

  if (runs.length === 0) {
    return <p className="text-sm text-text">No runs logged yet.</p>
  }

  return (
    <div className="flex flex-col divide-y divide-border">
      {runs.map((run) => (
        <RunRow
          key={run.id}
          shoeId={shoeId}
          run={run}
          onChanged={onChanged}
          onUpdated={(updated) =>
            setRuns((prev) => prev?.map((r) => (r.id === updated.id ? updated : r)) ?? null)
          }
          onDeleted={() => setRuns((prev) => prev?.filter((r) => r.id !== run.id) ?? null)}
        />
      ))}
    </div>
  )
}
