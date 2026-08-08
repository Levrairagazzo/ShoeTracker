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
  shoeId: number
}

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
