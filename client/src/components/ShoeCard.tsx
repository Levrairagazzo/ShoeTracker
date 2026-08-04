import type { Shoe } from '../api/types'
import { ChevronDownIcon } from './icons'

interface ShoeCardProps {
  shoe: Shoe
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

export function ShoeCard({ shoe }: ShoeCardProps) {
  const percentUsed = Math.min(100, (shoe.totalDistanceKm / shoe.thresholdKm) * 100)
  const kmRemaining = Math.max(0, shoe.thresholdKm - shoe.totalDistanceKm)
  const pill = statusPill(shoe)

  return (
    <details className="group rounded-2xl border border-border bg-bg shadow-sm transition-shadow open:shadow-md">
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

      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 border-t border-border px-5 py-4 text-sm">
        <dt className="text-text">Purchased</dt>
        <dd className="text-right text-text-h">{formatDate(shoe.purchaseDate)}</dd>

        <dt className="text-text">Threshold</dt>
        <dd className="text-right text-text-h">{shoe.thresholdKm} km</dd>

        <dt className="text-text">Remaining</dt>
        <dd className="text-right text-text-h">{kmRemaining.toFixed(1)} km</dd>

        <dt className="text-text">Used</dt>
        <dd className="text-right text-text-h">{percentUsed.toFixed(0)}%</dd>
      </dl>
    </details>
  )
}
