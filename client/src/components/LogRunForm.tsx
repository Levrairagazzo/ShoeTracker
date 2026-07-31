import { useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Shoe } from '../api/types'

interface LogRunFormProps {
  shoes: Shoe[]
  onRunLogged: () => void
}

export function LogRunForm({ shoes, onRunLogged }: LogRunFormProps) {
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
      onRunLogged()
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
    <form onSubmit={handleSubmit} className="card">
      <h2>Log a run</h2>
      <label>
        Shoe
        <select value={shoeId} onChange={(e) => setShoeId(e.target.value)} required>
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
      <label>
        Date
        <input type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
      </label>
      <label>
        Distance (km)
        <input
          type="number"
          min="0.1"
          step="0.1"
          value={distanceKm}
          onChange={(e) => setDistanceKm(e.target.value)}
          required
        />
      </label>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={submitting || shoes.length === 0}>
        {submitting ? 'Logging…' : 'Log run'}
      </button>
    </form>
  )
}
