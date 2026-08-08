import type { CreateRunRequest, CreateShoeRequest, CurrentUser, LoginRequest, Run, Shoe, ValidationProblem } from './types'

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

  updateShoe: (id: number, body: CreateShoeRequest) =>
    request<Shoe>(`/shoes/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

  deleteShoe: (id: number) => request<void>(`/shoes/${id}`, { method: 'DELETE' }),

  listRuns: (shoeId: number) => request<Run[]>(`/shoes/${shoeId}/runs`),

  logRun: (shoeId: number, body: CreateRunRequest) =>
    request<Run>(`/shoes/${shoeId}/runs`, { method: 'POST', body: JSON.stringify(body) }),

  updateRun: (shoeId: number, id: number, body: CreateRunRequest) =>
    request<Run>(`/shoes/${shoeId}/runs/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

  deleteRun: (shoeId: number, id: number) =>
    request<void>(`/shoes/${shoeId}/runs/${id}`, { method: 'DELETE' }),

  login: (body: LoginRequest) =>
    request<CurrentUser>('/auth/login', { method: 'POST', body: JSON.stringify(body) }),

  logout: () => request<void>('/auth/logout', { method: 'POST' }),

  me: () => request<CurrentUser>('/auth/me'),
}

export { ApiError }
