import { useState } from 'react'
import { ApiError, shoeTrackerClient } from '../api/shoeTrackerClient'
import type { CurrentUser } from '../api/types'

interface LoginFormProps {
  onSuccess: (user: CurrentUser) => void
}

const inputClass =
  'rounded-lg border border-border bg-bg px-3 py-2 text-text-h outline-none focus:border-accent-border'

export function LoginForm({ onSuccess }: LoginFormProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      const user = await shoeTrackerClient.login({ email, password })
      onSuccess(user)
    } catch (err) {
      if (err instanceof ApiError) {
        const messages = err.problem?.errors ? Object.values(err.problem.errors).flat() : [err.message]
        setError(messages.join(' '))
      } else {
        setError('Something went wrong logging in.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <label className="flex flex-col gap-1 text-sm text-text">
        Email
        <input
          className={inputClass}
          type="text"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />
      </label>
      <label className="flex flex-col gap-1 text-sm text-text">
        Password
        <input
          className={inputClass}
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
      </label>
      {error && <p className="text-sm text-red-600">{error}</p>}
      <button
        type="submit"
        disabled={submitting}
        className="mt-2 rounded-full bg-accent px-4 py-2 text-sm font-medium text-white hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {submitting ? 'Logging in…' : 'Log in'}
      </button>
    </form>
  )
}
