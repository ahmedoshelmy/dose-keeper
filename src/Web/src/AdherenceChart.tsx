import type { WeekPoint } from './api'
import { adherenceBand } from './logic'

/** Plain SVG bars: one per week, 80% target line. No chart library needed for six bars. */
export function AdherenceChart({ points }: { points: WeekPoint[] }) {
  const width = 360
  const height = 160
  const pad = 24
  const barWidth = (width - pad * 2) / Math.max(points.length, 1)
  const y = (percent: number) => height - pad - ((height - pad * 2) * percent) / 100

  return (
    <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Weekly adherence" className="chart">
      <line x1={pad} x2={width - pad} y1={y(80)} y2={y(80)} className="target" />
      <text x={width - pad} y={y(80) - 4} textAnchor="end" className="axis">
        80% target
      </text>
      {points.map((p, i) => {
        const x = pad + i * barWidth + 6
        const empty = p.due === 0
        return (
          <g key={p.weekStart}>
            <rect
              x={x}
              y={empty ? y(0) - 2 : y(p.percent)}
              width={barWidth - 12}
              height={empty ? 2 : Math.max(2, y(0) - y(p.percent))}
              rx={4}
              className={empty ? 'bar empty' : `bar ${adherenceBand(p.percent)}`}
            >
              <title>{empty ? `${p.weekStart}: no doses due` : `${p.weekStart}: ${p.percent}% (${p.taken}/${p.due})`}</title>
            </rect>
            <text x={x + (barWidth - 12) / 2} y={height - 6} textAnchor="middle" className="axis">
              {p.weekStart.slice(5)}
            </text>
          </g>
        )
      })}
    </svg>
  )
}
