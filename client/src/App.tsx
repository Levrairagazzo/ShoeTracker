import { useCallback, useEffect, useState } from 'react'
import { ShoeForm } from './components/ShoeForm'
import { LogRunForm } from './components/LogRunForm'
import { LoginScreen } from './components/LoginScreen'
import { Modal } from './components/Modal'
import { ShoeGrid } from './components/ShoeGrid'
import { PlusIcon } from './components/icons'
import { shoeTrackerClient } from './api/shoeTrackerClient'
import type { CurrentUser, Shoe } from './api/types'

function SkeletonGrid() {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 sm:gap-6 lg:grid-cols-3">
      {[0, 1, 2].map((i) => (
        <div
          key={i}
          className="h-44 animate-pulse rounded-2xl border border-border bg-border/30"
        />
      ))}
    </div>
  )
}

function App() {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [sessionLoading, setSessionLoading] = useState(true)
  const [shoes, setShoes] = useState<Shoe[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [isAddShoeOpen, setIsAddShoeOpen] = useState(false)
  const [isLogRunOpen, setIsLogRunOpen] = useState(false)

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
    async function checkSession() {
      try {
        const result = await shoeTrackerClient.me()
        setUser(result)
      } catch {
        setUser(null)
      } finally {
        setSessionLoading(false)
      }
    }

    checkSession()
  }, [])

  useEffect(() => {
    if (user) loadShoes()
  }, [user, loadShoes])

  async function handleLogout() {
    await shoeTrackerClient.logout()
    setUser(null)
  }

  if (sessionLoading) {
    return null
  }

  if (!user) {
    return <LoginScreen onSuccess={setUser} />
  }

  return (
    <main className="mx-auto max-w-5xl px-5 py-8 sm:py-10">
      <header className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1>Shoe Mileage Tracker</h1>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setIsLogRunOpen(true)}
            className="rounded-full border border-accent-border px-4 py-2 text-sm font-medium text-accent hover:bg-accent-bg"
          >
            Log Run
          </button>
          <button
            type="button"
            onClick={() => setIsAddShoeOpen(true)}
            className="inline-flex items-center gap-1.5 rounded-full bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90"
          >
            <PlusIcon className="size-4" />
            Add Shoe
          </button>
          <button
            type="button"
            onClick={handleLogout}
            className="rounded-full px-4 py-2 text-sm font-medium text-text hover:bg-border/50"
          >
            Log out
          </button>
        </div>
      </header>

      {loading && <SkeletonGrid />}

      {!loading && loadError && (
        <div className="flex items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-red-700">
          <p>{loadError}</p>
          <button
            type="button"
            onClick={loadShoes}
            className="shrink-0 rounded-full border border-red-300 px-3 py-1 text-sm font-medium hover:bg-red-100"
          >
            Retry
          </button>
        </div>
      )}

      {!loading && !loadError && (
        <ShoeGrid shoes={shoes} onAddShoe={() => setIsAddShoeOpen(true)} onChanged={loadShoes} />
      )}

      <Modal open={isAddShoeOpen} onClose={() => setIsAddShoeOpen(false)} title="Add a shoe">
        <ShoeForm
          onSuccess={() => {
            loadShoes()
            setIsAddShoeOpen(false)
          }}
          onCancel={() => setIsAddShoeOpen(false)}
        />
      </Modal>

      <Modal open={isLogRunOpen} onClose={() => setIsLogRunOpen(false)} title="Log a run">
        <LogRunForm
          shoes={shoes}
          onSuccess={() => {
            loadShoes()
            setIsLogRunOpen(false)
          }}
          onCancel={() => setIsLogRunOpen(false)}
        />
      </Modal>
    </main>
  )
}

export default App
