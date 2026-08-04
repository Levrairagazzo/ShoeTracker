import { useState } from 'react'
import { shoeTrackerClient } from '../api/shoeTrackerClient'
import type { Shoe } from '../api/types'
import { ChevronDownIcon } from './icons'
import { Modal } from './Modal'
import { RunHistoryList } from './RunHistoryList'
import { ShoeForm } from './ShoeForm'

interface ShoeCardProps {
  shoe: Shoe
  onChanged: () => void
}

function formatDate(date: string) {
  return new Date(date).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function statusPill(shoe: Shoe) {
  if (shoe.isRetired) {
    return { label: 'Retired', className: 'bg-border/60 text-text' }
  }
  if (shoe.isOverThreshold) {
    return { label: 'Retire me!', className: 'bg-accent-bg text-accent' }
  }
  return { label: 'On track', className: 'bg-emerald-500/10 text-emerald-600' }
}

export function ShoeCard({ shoe, onChanged }: ShoeCardProps) {
  const [isOpen, setIsOpen] = useState(false)
  const [isEditOpen, setIsEditOpen] = useState(false)
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const percentUsed = Math.min(100, (shoe.totalDistanceKm / shoe.thresholdKm) * 100)
  const kmRemaining = Math.max(0, shoe.thresholdKm - shoe.totalDistanceKm)
  const pill = statusPill(shoe)

  async function handleDelete() {
    setDeleting(true)
    try {
      await shoeTrackerClient.deleteShoe(shoe.id)
      onChanged()
    } finally {
      setDeleting(false)
    }
  }

  return (
    <details
      className="group rounded-2xl border border-border bg-bg shadow-sm transition-shadow open:shadow-md"
      onToggle={(e) => setIsOpen(e.currentTarget.open)}
    >
      <summary className="flex cursor-pointer list-none items-start justify-between gap-3 p-5 [&::-webkit-details-marker]:hidden">
        <div className="min-w-0">
          <p className="truncate text-base font-medium text-text-h">{shoe.name}</p>
          <p className="truncate text-sm text-text">{shoe.brand}</p>

          <p className="mt-3 text-3xl font-semibold tabular-nums text-text-h">
            {shoe.totalDistanceKm.toFixed(1)}
            <span className="ml-1 text-sm font-normal text-text">km</span>
          </p>

          <span
            className={`mt-2 inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${pill.className}`}
          >
            {pill.label}
          </span>

          <div className="mt-3 h-1.5 w-full max-w-40 overflow-hidden rounded-full bg-border">
            <div
              className="h-full rounded-full bg-accent transition-[width]"
              style={{ width: `${percentUsed}%` }}
            />
          </div>
        </div>

        <ChevronDownIcon className="mt-1 size-5 shrink-0 text-text transition-transform group-open:rotate-180" />
      </summary>

      <div className="border-t border-border px-5 py-4">
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
          <dt className="text-text">Purchased</dt>
          <dd className="text-right text-text-h">{formatDate(shoe.purchaseDate)}</dd>

          <dt className="text-text">Threshold</dt>
          <dd className="text-right text-text-h">{shoe.thresholdKm} km</dd>

          <dt className="text-text">Remaining</dt>
          <dd className="text-right text-text-h">{kmRemaining.toFixed(1)} km</dd>

          <dt className="text-text">Used</dt>
          <dd className="text-right text-text-h">{percentUsed.toFixed(0)}%</dd>
        </dl>

        <div className="mt-3 flex items-center gap-2">
          <button
            type="button"
            onClick={() => setIsEditOpen(true)}
            className="rounded-full px-3 py-1 text-xs font-medium text-accent hover:bg-accent-bg"
          >
            Edit shoe
          </button>
          {isConfirmingDelete ? (
            <>
              <span className="text-xs text-text">Delete this shoe and its runs?</span>
              <button
                type="button"
                onClick={handleDelete}
                disabled={deleting}
                className="rounded-full px-3 py-1 text-xs font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
              >
                Confirm
              </button>
              <button
                type="button"
                onClick={() => setIsConfirmingDelete(false)}
                className="rounded-full px-3 py-1 text-xs font-medium text-text hover:bg-border/50"
              >
                Cancel
              </button>
            </>
          ) : (
            <button
              type="button"
              onClick={() => setIsConfirmingDelete(true)}
              className="rounded-full px-3 py-1 text-xs font-medium text-text hover:bg-border/50"
            >
              Delete shoe
            </button>
          )}
        </div>

        <div className="mt-4 border-t border-border pt-3">
          <p className="mb-2 text-xs font-medium text-text">Run history</p>
          {isOpen && <RunHistoryList shoeId={shoe.id} onChanged={onChanged} />}
        </div>
      </div>

      <Modal open={isEditOpen} onClose={() => setIsEditOpen(false)} title="Edit shoe">
        <ShoeForm
          shoe={shoe}
          onSuccess={() => {
            onChanged()
            setIsEditOpen(false)
          }}
          onCancel={() => setIsEditOpen(false)}
        />
      </Modal>
    </details>
  )
}
