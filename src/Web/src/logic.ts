import type { Dose, Medication, RefillForecast } from './api'

/** Splits today's doses into what needs an answer now and what is already settled. */
export function splitDoses(doses: Dose[], now: Date): { due: Dose[]; upcoming: Dose[]; done: Dose[] } {
  const due: Dose[] = []
  const upcoming: Dose[] = []
  const done: Dose[] = []
  for (const dose of doses) {
    if (dose.status !== 'Pending') done.push(dose)
    else if (new Date(dose.dueUtc) <= now) due.push(dose)
    else upcoming.push(dose)
  }
  return { due, upcoming, done }
}

export interface RefillWarning {
  medication: Medication
  daysLeft: number
  runOutDate: string
}

/** Medications that run out within their threshold, soonest first. */
export function refillWarnings(forecasts: RefillForecast[], medications: Medication[]): RefillWarning[] {
  const byId = new Map(medications.map((m) => [m.id, m]))
  return forecasts
    .filter((f) => f.needsRefill && f.daysLeft !== null && f.runOutDate !== null && byId.has(f.medicationId))
    .map((f) => ({ medication: byId.get(f.medicationId)!, daysLeft: f.daysLeft!, runOutDate: f.runOutDate! }))
    .sort((a, b) => a.daysLeft - b.daysLeft)
}

/** Bar colour band for an adherence percentage (WHO-style 80% "good adherence" line). */
export function adherenceBand(percent: number): 'good' | 'fair' | 'poor' {
  if (percent >= 80) return 'good'
  if (percent >= 50) return 'fair'
  return 'poor'
}

/** Parses "08:00, 20:00" into sorted, de-duplicated HH:mm strings; null if any part is not a time. */
export function parseTimes(input: string): string[] | null {
  const parts = input.split(/[\s,]+/).filter(Boolean)
  if (parts.length === 0) return null
  const times: string[] = []
  for (const part of parts) {
    const match = /^([01]?\d|2[0-3]):([0-5]\d)$/.exec(part)
    if (!match) return null
    times.push(`${match[1]!.padStart(2, '0')}:${match[2]}`)
  }
  return [...new Set(times)].sort()
}
