import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import NotFoundPage from './NotFoundPage'

/**
 * The catch-all (browser-pass finding 9's second half, slice 7.2). An unmatched path used to paint
 * a blank white page — no header, no shell, no way back, nothing in the console. The first person
 * likely to hit it is a member of the public who mistyped a `/p/{token}` link a broker sent them.
 */
describe('the catch-all page', () => {
  it('says the page does not exist and offers one way out', () => {
    render(
      <MemoryRouter initialEntries={['/PLACEHOLDER-nonsense']}>
        <Routes>
          <Route path="/officer" element={<p>officer</p>} />
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Not found')).toBeDefined()
    expect(screen.getByRole('link', { name: /Go to the start/i }).getAttribute('href')).toBe('/')
  })

  /**
   * It must not shadow a real route. React Router matches the most specific pattern regardless of
   * order, so this is a property of the router rather than of where the route is written — which is
   * exactly why it is worth pinning: the next person to add a route will put it after `*`.
   */
  it('does not swallow a route that exists', () => {
    render(
      <MemoryRouter initialEntries={['/officer']}>
        <Routes>
          <Route path="*" element={<NotFoundPage />} />
          <Route path="/officer" element={<p>officer</p>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('officer')).toBeDefined()
    expect(screen.queryByText('Not found')).toBeNull()
  })
})
