import type { CreateRunRequest, CreateShoeRequest, Run, Shoe, ValidationProblem } from './types'

const BASE_URL = '/api'

class ApiError extends Error {
  status: number
  problem: ValidationProblem | null

  constructor(status: number, problem: ValidationProblem | null) {
    super(problem?.title ?? `Request failed with status ${status}`)
    this.status = status
    this.problem = problem
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export const shoeTrackerClient = {
  listShoes: () => request<Shoe[]>('/shoes'),

  getShoe: (id: number) => request<Shoe>(`/shoes/${id}`),

  createShoe: (body: CreateShoeRequest) =>
    request<Shoe>('/shoes', { method: 'POST', body: JSON.stringify(body) }),

  logRun: (shoeId: number, body: CreateRunRequest) =>
    request<Run>(`/shoes/${shoeId}/runs`, { method: 'POST', body: JSON.stringify(body) }),
}

export { ApiError }
