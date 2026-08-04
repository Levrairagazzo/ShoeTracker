import { useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Shoe } from '../api/types'

interface LogRunFormProps {
  shoes: Shoe[]
  onSuccess: () => void
  onCancel: () => void
}

const inputClass =
  'rounded-lg border border-border bg-bg px-3 py-2 text-text-h outline-none focus:border-accent-border'

export function LogRunForm({ shoes, onSuccess, onCancel }: LogRunFormProps) {
  const [shoeId, setShoeId] = useState('')
  const [date, setDate] = useState('')
  const [distanceKm, setDistanceKm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      await shoeTrackerClient.logRun(Number(shoeId), { date, distanceKm: Number(distanceKm) })
      setDate('')
      setDistanceKm('')
      onSuccess()
    } catch (err) {
      if (err instanceof ApiError) {
        const messages = err.problem ? Object.values(err.problem.errors).flat() : [err.message]
        setError(messages.join(' '))
      } else {
        setError('Something went wrong logging the run.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <label className="flex flex-col gap-1 text-sm text-text">
        Shoe
        <select
          className={inputClass}
          value={shoeId}
          onChange={(e) => setShoeId(e.target.value)}
          required
        >
          <option value="" disabled>
            Select a shoe
          </option>
          {shoes.map((shoe) => (
            <option key={shoe.id} value={shoe.id}>
              {shoe.name} ({shoe.brand})
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Date
        <input
          className={inputClass}
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          required
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Distance (km)
        <input
          className={inputClass}
          type="number"
          min="0.1"
          step="0.1"
          value={distanceKm}
          onChange={(e) => setDistanceKm(e.target.value)}
          required
        />
      </label>
      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="mt-2 flex justify-end gap-2">
        <button
          type="button"
          onClick={onCancel}
          className="rounded-full px-4 py-2 text-sm font-medium text-text hover:bg-border/50"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={submitting || shoes.length === 0}
          className="rounded-full bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {submitting ? 'Logging…' : 'Log run'}
        </button>
      </div>
    </form>
  )
}
