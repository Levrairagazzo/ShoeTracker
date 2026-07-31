import { useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'

interface AddShoeFormProps {
  onShoeAdded: () => void
}

export function AddShoeForm({ onShoeAdded }: AddShoeFormProps) {
  const [name, setName] = useState('')
  const [brand, setBrand] = useState('')
  const [purchaseDate, setPurchaseDate] = useState('')
  const [thresholdKm, setThresholdKm] = useState('700')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      await shoeTrackerClient.createShoe({
        name,
        brand,
        purchaseDate,
        thresholdKm: thresholdKm ? Number(thresholdKm) : undefined,
      })
      setName('')
      setBrand('')
      setPurchaseDate('')
      setThresholdKm('700')
      onShoeAdded()
    } catch (err) {
      if (err instanceof ApiError) {
        const messages = err.problem ? Object.values(err.problem.errors).flat() : [err.message]
        setError(messages.join(' '))
      } else {
        setError('Something went wrong adding the shoe.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="card">
      <h2>Add a shoe</h2>
      <label>
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} required />
      </label>
      <label>
        Brand
        <input value={brand} onChange={(e) => setBrand(e.target.value)} required />
      </label>
      <label>
        Purchase date
        <input
          type="date"
          value={purchaseDate}
          onChange={(e) => setPurchaseDate(e.target.value)}
          required
        />
      </label>
      <label>
        Retirement threshold (km)
        <input
          type="number"
          min="1"
          value={thresholdKm}
          onChange={(e) => setThresholdKm(e.target.value)}
        />
      </label>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={submitting}>
        {submitting ? 'Adding…' : 'Add shoe'}
      </button>
    </form>
  )
}
