export interface Shoe {
  id: number
  name: string
  brand: string
  purchaseDate: string
  thresholdKm: number
  isRetired: boolean
  totalDistanceKm: number
  isOverThreshold: boolean
  /** newly imported Strava runs are assigned to this shoe */
  isDefault: boolean
}

export interface Run {
  id: number
  date: string
  distanceKm: number
  /** null while the run isn't assigned to a shoe (e.g. an imported Strava run) */
  shoeId: number | null
  source: RunSource
  /** null for manual runs */
  type: RunType | null
  isRace: boolean
  /** longer than a marathon */
  isUltra: boolean
  /** where the run started, e.g. "Oakland, California"; null if unknown or not looked up yet */
  location: string | null
}

export type RunSource = 'Manual' | 'Strava'

export type RunType = 'Run' | 'Trail' | 'Treadmill'

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
  /** human-readable explanation, set on non-validation problems */
  detail?: string
  status: number
  errors: Record<string, string[]>
}

export interface CurrentUser {
  id: number
  email: string
}

export interface StravaStatus {
  /** false when the server has no Strava API credentials configured */
  available: boolean
  connected: boolean
  athleteName: string | null
  /** how many of the user's runs came from Strava */
  importedRuns: number
  /** how many of the user's runs aren't assigned to a shoe */
  unassignedRuns: number
}

export interface StravaImportResult {
  imported: number
}

export interface LoginRequest {
  email: string
  password: string
}
