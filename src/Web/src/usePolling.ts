import { useCallback, useEffect, useState } from 'react'

/** Loads data now and every `intervalMs`; `reload` refreshes after a user action. Simple polling instead of push. */
export function usePolling<T>(load: () => Promise<T>, intervalMs: number): { data: T | undefined; error: string | null; reload: () => void } {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const reload = useCallback(() => setTick((t) => t + 1), [])

  useEffect(() => {
    let active = true
    const run = () =>
      load()
        .then((value) => {
          if (active) {
            setData(value)
            setError(null)
          }
        })
        .catch((e: unknown) => active && setError(e instanceof Error ? e.message : String(e)))
    run()
    const id = window.setInterval(run, intervalMs)
    return () => {
      active = false
      window.clearInterval(id)
    }
  }, [load, intervalMs, tick])

  return { data, error, reload }
}
