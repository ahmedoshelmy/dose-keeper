import { useState, type FormEvent } from 'react'
import { api } from './api'
import { parseTimes } from './logic'

/** One form creates the medication and its schedule; the schedule starts its reminder orchestration server-side. */
export function AddMedication({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('')
  const [strength, setStrength] = useState('')
  const [pills, setPills] = useState(30)
  const [times, setTimes] = useState('08:00, 20:00')
  const [lateWindow, setLateWindow] = useState(30)
  const [caregiver, setCaregiver] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const parsed = parseTimes(times)
    if (!parsed) {
      setError('Times must look like 08:00, 20:00')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const medication = await api.addMedication(name, strength, pills, 7)
      await api.addSchedule({
        medicationId: medication.id,
        timesOfDay: parsed,
        dosePerIntake: 1,
        lateWindowMinutes: lateWindow,
        caregiverContact: caregiver || undefined,
      })
      setName('')
      setStrength('')
      onAdded()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>Add a medication</h2>
      <label>
        Name <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Metformin" required />
      </label>
      <label>
        Strength <input value={strength} onChange={(e) => setStrength(e.target.value)} placeholder="500 mg" />
      </label>
      <label>
        Pills on hand <input type="number" min={0} value={pills} onChange={(e) => setPills(Number(e.target.value))} />
      </label>
      <label>
        Times (Cairo) <input value={times} onChange={(e) => setTimes(e.target.value)} />
      </label>
      <label>
        Late window (min) <input type="number" min={1} max={720} value={lateWindow} onChange={(e) => setLateWindow(Number(e.target.value))} />
      </label>
      <label>
        Caregiver (optional) <input type="email" value={caregiver} onChange={(e) => setCaregiver(e.target.value)} placeholder="caregiver@example.com" />
      </label>
      {error && <p className="error">{error}</p>}
      <button type="submit" disabled={busy}>
        {busy ? 'Saving…' : 'Add and start reminders'}
      </button>
    </form>
  )
}
