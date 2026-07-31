import type { Shoe } from '../api/types'

interface ShoeListProps {
  shoes: Shoe[]
}

export function ShoeList({ shoes }: ShoeListProps) {
  if (shoes.length === 0) {
    return <p>No shoes yet — add one to get started.</p>
  }

  return (
    <table className="shoe-table">
      <thead>
        <tr>
          <th>Name</th>
          <th>Brand</th>
          <th>Total km</th>
          <th>Threshold km</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {shoes.map((shoe) => (
          <tr key={shoe.id} className={shoe.isOverThreshold ? 'over-threshold' : undefined}>
            <td>{shoe.name}</td>
            <td>{shoe.brand}</td>
            <td>{shoe.totalDistanceKm.toFixed(1)}</td>
            <td>{shoe.thresholdKm}</td>
            <td>{shoe.isOverThreshold ? 'Retire me!' : 'OK'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
