export type DoseStatus = 'Pending' | 'Taken' | 'Skipped' | 'Missed'

export interface Dose {
  scheduleId: string
  medicationId: string
  medicationName: string
  dueUtc: string
  localTime: string
  pills: number
  status: DoseStatus
  late: boolean
}

export interface Medication {
  id: string
  name: string
  strength: string
  pillsOnHand: number
  refillThresholdDays: number
}

export interface RefillForecast {
  medicationId: string
  pillsOnHand: number
  dailyUse: number
  daysLeft: number | null
  runOutDate: string | null
  needsRefill: boolean
}

export interface WeekPoint {
  weekStart: string
  percent: number
  due: number
  taken: number
}

export interface Notification {
  id: string
  kind: 'Reminder' | 'Escalation' | 'Missed' | 'CaregiverAlert' | 'Refill'
  recipient: string
  text: string
  createdUtc: string
}

export interface NewSchedule {
  medicationId: string
  timesOfDay: string[]
  dosePerIntake: number
  lateWindowMinutes: number
  caregiverContact?: string
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api/${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { detail?: string } | null
    throw new Error(problem?.detail ?? `${response.status} ${response.statusText}`)
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

const post = <T>(path: string, body: unknown) => request<T>(path, { method: 'POST', body: JSON.stringify(body) })

export const api = {
  today: () => request<Dose[]>('doses/today'),
  medications: () => request<Medication[]>('medications'),
  refills: () => request<RefillForecast[]>('refills'),
  adherence: (weeks = 6) => request<WeekPoint[]>(`adherence?weeks=${weeks}`),
  notifications: () => request<Notification[]>('notifications'),
  take: (dose: Dose) => post('doses/take', { scheduleId: dose.scheduleId, dueUtc: dose.dueUtc }),
  skip: (dose: Dose) => post('doses/skip', { scheduleId: dose.scheduleId, dueUtc: dose.dueUtc }),
  snooze: (dose: Dose, minutes: number) => post('doses/snooze', { scheduleId: dose.scheduleId, dueUtc: dose.dueUtc, minutes }),
  addMedication: (name: string, strength: string, pillsOnHand: number, refillThresholdDays: number) =>
    post<Medication>('medications', { name, strength, pillsOnHand, refillThresholdDays }),
  addSchedule: (schedule: NewSchedule) => post('schedules', schedule),
  refill: (medicationId: string, pills: number) => post(`medications/${medicationId}/refill`, { pills }),
}
