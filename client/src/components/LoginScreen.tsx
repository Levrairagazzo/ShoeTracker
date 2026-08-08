import { LoginForm } from './LoginForm'
import type { CurrentUser } from '../api/types'

interface LoginScreenProps {
  onSuccess: (user: CurrentUser) => void
}

export function LoginScreen({ onSuccess }: LoginScreenProps) {
  return (
    <main className="mx-auto flex min-h-screen max-w-sm flex-col justify-center px-5 py-8">
      <h1 className="mb-1 text-2xl">Shoe Mileage Tracker</h1>
      <p className="mb-6 text-text">Log in to continue.</p>
      <div className="rounded-2xl border border-border bg-bg p-5 shadow-2xl">
        <LoginForm onSuccess={onSuccess} />
      </div>
    </main>
  )
}
