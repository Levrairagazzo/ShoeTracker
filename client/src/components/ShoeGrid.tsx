import type { Shoe } from '../api/types'
import { PlusIcon } from './icons'
import { ShoeCard } from './ShoeCard'

interface ShoeGridProps {
  shoes: Shoe[]
  onAddShoe: () => void
  onChanged: () => void
}

export function ShoeGrid({ shoes, onAddShoe, onChanged }: ShoeGridProps) {
  if (shoes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-border py-16 text-center">
        <p className="text-lg font-medium text-text-h">No shoes yet</p>
        <p className="max-w-xs text-sm text-text">
          Add your first pair to start tracking mileage.
        </p>
        <button
          type="button"
          onClick={onAddShoe}
          className="mt-2 inline-flex items-center gap-1.5 rounded-full bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90"
        >
          <PlusIcon className="size-4" />
          Add Shoe
        </button>
      </div>
    )
  }

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 sm:gap-6 lg:grid-cols-3">
      {shoes.map((shoe) => (
        <ShoeCard key={shoe.id} shoe={shoe} shoes={shoes} onChanged={onChanged} />
      ))}
    </div>
  )
}
