import { useEffect, useRef, useState } from 'react'

/**
 * A seconds counter for the resend throttle, ticking down to zero.
 *
 * Started from the server's `Retry-After` (see `retryAfterSeconds`) rather than from the configured
 * value, so it stays right across a reload — the browser does not know when the code was sent.
 *
 * The deadline is held as an absolute time rather than a decrementing count: an interval that ticks
 * while the tab is backgrounded is throttled by the browser, so a naive `n - 1` per second drifts
 * long and tells the user to wait after the server would already accept them.
 */
export function useCountdown(): [number, (seconds: number) => void] {
  const [remaining, setRemaining] = useState(0)
  const deadline = useRef(0)

  useEffect(() => {
    if (remaining <= 0) return

    const timer = setInterval(() => {
      const left = Math.ceil((deadline.current - Date.now()) / 1000)
      setRemaining(left > 0 ? left : 0)
    }, 1000)

    return () => {
      clearInterval(timer)
    }
  }, [remaining])

  return [
    remaining,
    (seconds: number) => {
      deadline.current = Date.now() + seconds * 1000
      setRemaining(seconds)
    },
  ]
}
