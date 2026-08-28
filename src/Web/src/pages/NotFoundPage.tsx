import { Link } from 'react-router-dom'

/**
 * The catch-all (browser-pass finding 9's second half, slice 7.2).
 *
 * Until now an unmatched path rendered a **blank white page** — no header, no shell, no way back,
 * no console line. That is bad on any route and worse on this product's: B3 hands a broker a
 * `/p/{token}` URL to give to a customer, so the first person likely to mistype one is a member of
 * the public who has never seen the app, has no account to sign in to, and cannot tell a dead link
 * from a broken product.
 *
 * **Role-agnostic on purpose.** It is mounted outside every layout, so it renders for a signed-in
 * expert and an anonymous customer alike — and it must, because the paths that reach it include the
 * public link. It therefore says nothing about who the visitor is and offers one way out: `/`, which
 * `App.tsx` already redirects to whichever home the session has, or to the login screen when there
 * is no session. Nothing here reveals whether a token, a claim or a user exists (§9.1's rule about
 * saying as little as the surface can).
 */
export default function NotFoundPage() {
  return (
    // **Its own chrome, found in the browser pass.** Rendered as a bare `<section className="page">`
    // it came out flush against the top-left corner of the viewport with no padding, no header and
    // no width — technically the fix for a blank page, and visually still one. It cannot use
    // `AppShell`, which needs a role and a session, so it borrows `PublicRequestPage`'s wrapper: the
    // wordmark and a narrow main, which is all a page reachable by an anonymous visitor may show.
    <div className="app-shell app-shell--touch">
      <header className="app-header">
        <span className="app-header__brand">AXA Motor Claims</span>
      </header>
      <main className="app-main app-main--narrow">
        <section className="page">
          <h2 className="page__title">Not found</h2>
          <p className="muted">
            This page does not exist. If you followed a link from an email or a message, it may
            have expired or been mistyped.
          </p>
          <p>
            <Link to="/">Go to the start</Link>
          </p>
        </section>
      </main>
    </div>
  )
}
