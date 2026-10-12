import { useState } from 'react'
import { shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Run, Shoe } from '../api/types'

interface MoveRunSelectProps {
  run: Run
  shoes: Shoe[]
  onMoved: (run: Run) => void
}

const UNASSIGNED = ''

/** One-click control to move a run to another shoe, or back to unassigned. */
export function MoveRunSelect({ run, shoes, onMoved }: MoveRunSelectProps) {
  const [moving, setMoving] = useState(false)
  const [error, setError] = useState(false)
  // Active shoes first; retired ones stay available but labelled.
  const ordered = [...shoes].sort((a, b) => Number(a.isRetired) - Number(b.isRetired))

  async function handleChange(value: string) {
    setMoving(true)
    setError(false)
    try {
      onMoved(await shoeTrackerClient.moveRun(run.id, value === UNASSIGNED ? null : Number(value)))
    } catch {
      setError(true)
    } finally {
      setMoving(false)
    }
  }

  return (
    <select
      aria-label="Shoe"
      value={run.shoeId?.toString() ?? UNASSIGNED}
      onChange={(e) => handleChange(e.target.value)}
      disabled={moving}
      title={error ? 'Could not move this run. Please try again.' : undefined}
      className={`max-w-40 rounded-md border bg-bg px-1.5 py-0.5 text-xs text-text-h disabled:opacity-60 ${
        error ? 'border-red-400' : 'border-border'
      }`}
    >
      <option value={UNASSIGNED}>Unassigned</option>
      {ordered.map((shoe) => (
        <option key={shoe.id} value={shoe.id}>
          {shoe.name}
          {shoe.isRetired ? ' (retired)' : ''}
        </option>
      ))}
    </select>
  )
}
