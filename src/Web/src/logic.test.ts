import { describe, expect, it } from 'vitest'
import type { Dose, Medication, RefillForecast } from './api'
import { adherenceBand, parseTimes, refillWarnings, splitDoses } from './logic'

const dose = (dueUtc: string, status: Dose['status'] = 'Pending'): Dose => ({
  scheduleId: 'sch-a',
  medicationId: 'med-a',
  medicationName: 'Metformin 500 mg',
  dueUtc,
  localTime: '08:00',
  pills: 1,
  status,
  late: false,
})

describe('splitDoses', () => {
  it('separates due, upcoming and answered doses', () => {
    const now = new Date('2026-01-10T12:00:00Z')
    const result = splitDoses(
      [dose('2026-01-10T06:00:00Z'), dose('2026-01-10T18:00:00Z'), dose('2026-01-10T07:00:00Z', 'Taken')],
      now,
    )
    expect(result.due).toHaveLength(1)
    expect(result.upcoming).toHaveLength(1)
    expect(result.done[0]?.status).toBe('Taken')
  })
})

describe('refillWarnings', () => {
  const meds: Medication[] = [
    { id: 'a', name: 'Metformin', strength: '500 mg', pillsOnHand: 6, refillThresholdDays: 7 },
    { id: 'b', name: 'Vitamin D', strength: '1000 IU', pillsOnHand: 2, refillThresholdDays: 7 },
    { id: 'c', name: 'Iron', strength: '65 mg', pillsOnHand: 90, refillThresholdDays: 7 },
  ]
  const forecast = (medicationId: string, daysLeft: number | null, needsRefill: boolean): RefillForecast => ({
    medicationId,
    pillsOnHand: 0,
    dailyUse: 1,
    daysLeft,
    runOutDate: daysLeft === null ? null : '2026-01-20',
    needsRefill,
  })

  it('lists only low-stock medications, soonest first', () => {
    const warnings = refillWarnings([forecast('a', 3, true), forecast('b', 2, true), forecast('c', 90, false)], meds)
    expect(warnings.map((w) => w.medication.name)).toEqual(['Vitamin D', 'Metformin'])
  })

  it('ignores forecasts for unknown medications', () => {
    expect(refillWarnings([forecast('zzz', 1, true)], meds)).toEqual([])
  })
})

describe('adherenceBand', () => {
  it.each([
    [100, 'good'],
    [80, 'good'],
    [79.9, 'fair'],
    [50, 'fair'],
    [10, 'poor'],
  ])('%s%% is %s', (percent, band) => {
    expect(adherenceBand(percent)).toBe(band)
  })
})

describe('parseTimes', () => {
  it('normalises, sorts and de-duplicates', () => {
    expect(parseTimes('20:00, 8:00 08:00')).toEqual(['08:00', '20:00'])
  })

  it('rejects anything that is not HH:mm', () => {
    expect(parseTimes('8pm')).toBeNull()
    expect(parseTimes('24:00')).toBeNull()
    expect(parseTimes('  ')).toBeNull()
  })
})
