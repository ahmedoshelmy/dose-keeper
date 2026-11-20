import { useCallback, useState } from 'react'
import { api, type Dose } from './api'
import { AddMedication } from './AddMedication'
import { AdherenceChart } from './AdherenceChart'
import { refillWarnings, splitDoses } from './logic'
import { usePolling } from './usePolling'

const loadDashboard = async () => {
  const [doses, medications, refills, adherence, notifications] = await Promise.all([
    api.today(),
    api.medications(),
    api.refills(),
    api.adherence(6),
    api.notifications(),
  ])
  return { doses, medications, refills, adherence, notifications }
}

export default function App() {
  const { data, error, reload } = usePolling(loadDashboard, 10_000)
  const [actionError, setActionError] = useState<string | null>(null)

  const act = useCallback(
    async (action: () => Promise<unknown>) => {
      setActionError(null)
      try {
        await action()
        reload()
      } catch (e) {
        setActionError(e instanceof Error ? e.message : String(e))
      }
    },
    [reload],
  )

  const groups = splitDoses(data?.doses ?? [], new Date())
  const warnings = refillWarnings(data?.refills ?? [], data?.medications ?? [])
  const latest = data?.adherence.at(-1)

  return (
    <main>
      <header>
        <h1>Dose Keeper</h1>
        <p className="muted">
          Today: {groups.done.filter((d) => d.status === 'Taken').length} taken · {groups.due.length} due · {groups.upcoming.length} later
          {latest && latest.due > 0 && <> · this week {latest.percent}%</>}
        </p>
      </header>
      {(error || actionError) && <p className="error">{error ?? actionError}</p>}

      <section className="grid">
        <div className="card">
          <h2>Due now</h2>
          {groups.due.length === 0 && <p className="muted">Nothing waiting. Nice.</p>}
          {groups.due.map((dose) => (
            <DoseRow key={dose.scheduleId + dose.dueUtc} dose={dose}>
              <button onClick={() => act(() => api.take(dose))}>Take</button>
              <button className="secondary" onClick={() => act(() => api.snooze(dose, 10))}>
                Snooze 10m
              </button>
              <button className="secondary" onClick={() => act(() => api.skip(dose))}>
                Skip
              </button>
            </DoseRow>
          ))}
          <h3>Later today</h3>
          {groups.upcoming.length === 0 && <p className="muted">No more doses today.</p>}
          {groups.upcoming.map((dose) => (
            <DoseRow key={dose.scheduleId + dose.dueUtc} dose={dose} />
          ))}
          {groups.done.length > 0 && <h3>Done</h3>}
          {groups.done.map((dose) => (
            <DoseRow key={dose.scheduleId + dose.dueUtc} dose={dose}>
              <span className={`pill ${dose.status.toLowerCase()}`}>
                {dose.status}
                {dose.late && ' (late)'}
              </span>
            </DoseRow>
          ))}
        </div>

        <div className="card">
          <h2>Adherence, last 6 weeks</h2>
          <AdherenceChart points={data?.adherence ?? []} />
          <h2>Refills</h2>
          {warnings.length === 0 && <p className="muted">Stock is fine for every medication.</p>}
          {warnings.map((w) => (
            <div key={w.medication.id} className="row warn">
              <span>
                <strong>{w.medication.name}</strong> runs out in {w.daysLeft} day(s) ({w.runOutDate}), {w.medication.pillsOnHand} left
              </span>
              <button className="secondary" onClick={() => act(() => api.refill(w.medication.id, 30))}>
                +30 pills
              </button>
            </div>
          ))}
        </div>

        <div className="card">
          <h2>Notifications</h2>
          <p className="muted small">Delivered by the queue-triggered function (stub channel).</p>
          <ul className="feed">
            {(data?.notifications ?? []).slice(0, 8).map((n) => (
              <li key={n.id}>
                <span className={`pill ${n.kind.toLowerCase()}`}>{n.kind}</span> {n.text}
                {n.recipient !== 'user' && <span className="muted small"> → {n.recipient}</span>}
              </li>
            ))}
          </ul>
        </div>

        <AddMedication onAdded={reload} />
      </section>
      <footer className="muted small">Demo app with synthetic data. Not medical advice.</footer>
    </main>
  )
}

function DoseRow({ dose, children }: { dose: Dose; children?: React.ReactNode }) {
  return (
    <div className="row">
      <span className="time">{dose.localTime}</span>
      <span className="grow">
        {dose.medicationName} <span className="muted small">× {dose.pills}</span>
      </span>
      {children}
    </div>
  )
}
