export interface Shoe {
  id: number
  name: string
  brand: string
  purchaseDate: string
  thresholdKm: number
  isRetired: boolean
  totalDistanceKm: number
  isOverThreshold: boolean
}

export interface Run {
  id: number
  date: string
  distanceKm: number
  /** null while the run isn't assigned to a shoe (e.g. an imported Strava run) */
  shoeId: number | null
  source: RunSource
}

export type RunSource = 'Manual' | 'Strava'

export interface CreateShoeRequest {
  name: string
  brand: string
  purchaseDate: string
  thresholdKm?: number
}

export interface CreateRunRequest {
  date: string
  distanceKm: number
}

export interface ValidationProblem {
  title: string
  status: number
  errors: Record<string, string[]>
}

export interface CurrentUser {
  id: number
  email: string
}

export interface LoginRequest {
  email: string
  password: string
}
