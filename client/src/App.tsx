import { useCallback, useEffect, useState } from 'react'
import { AddShoeForm } from './components/AddShoeForm'
import { LogRunForm } from './components/LogRunForm'
import { ShoeList } from './components/ShoeList'
import { shoeTrackerClient } from './api/shoeTrackerClient'
import type { Shoe } from './api/types'
import './App.css'

function App() {
  const [shoes, setShoes] = useState<Shoe[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const loadShoes = useCallback(async () => {
    try {
      setLoadError(null)
      const result = await shoeTrackerClient.listShoes()
      setShoes(result)
    } catch {
      setLoadError('Could not load shoes from the API.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadShoes()
  }, [loadShoes])

  return (
    <main>
      <h1>Shoe Mileage Tracker</h1>

      {loading && <p>Loading…</p>}
      {loadError && <p className="error">{loadError}</p>}
      {!loading && !loadError && <ShoeList shoes={shoes} />}

      <div className="forms">
        <AddShoeForm onShoeAdded={loadShoes} />
        <LogRunForm shoes={shoes} onRunLogged={loadShoes} />
      </div>
    </main>
  )
}

export default App
