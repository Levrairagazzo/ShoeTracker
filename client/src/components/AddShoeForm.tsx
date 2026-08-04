import { useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'

interface AddShoeFormProps {
  onSuccess: () => void
  onCancel: () => void
}

const inputClass =
  'rounded-lg border border-border bg-bg px-3 py-2 text-text-h outline-none focus:border-accent-border'

export function AddShoeForm({ onSuccess, onCancel }: AddShoeFormProps) {
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
      onSuccess()
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
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <label className="flex flex-col gap-1 text-sm text-text">
        Name
        <input
          className={inputClass}
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Brand
        <input
          className={inputClass}
          value={brand}
          onChange={(e) => setBrand(e.target.value)}
          required
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Purchase date
        <input
          className={inputClass}
          type="date"
          value={purchaseDate}
          onChange={(e) => setPurchaseDate(e.target.value)}
          required
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Retirement threshold (km)
        <input
          className={inputClass}
          type="number"
          min="1"
          value={thresholdKm}
          onChange={(e) => setThresholdKm(e.target.value)}
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
          disabled={submitting}
          className="rounded-full bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {submitting ? 'Adding…' : 'Add shoe'}
        </button>
      </div>
    </form>
  )
}
