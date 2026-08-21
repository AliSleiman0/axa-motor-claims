import { usePushSubscription, type UsePushSubscriptionOptions } from './usePushSubscription'

/**
 * The "Enable notifications" control (design.md §8, slice 3.4). Lives in `ExpertLayout`, so it is on
 * screen on both E1 and E2 — an expert who dismissed it once should not have to go looking.
 *
 * The button exists because the permission prompt needs a user gesture; see `usePushSubscription`.
 * The `ArrivedPanel` shape: a section, one button, and `role="alert"` on anything that went wrong.
 */
export function PushPanel(options: UsePushSubscriptionOptions = {}) {
  const { enable, pending, enabled, unsupported, failed } = usePushSubscription(options)

  if (unsupported) {
    // No button at all rather than one that cannot work: offering an action that is guaranteed to
    // fail is worse than saying plainly that this browser will not do it.
    return (
      <section>
        <p role="status">
          This browser cannot show claim notifications. New claims will still appear in My claims.
        </p>
      </section>
    )
  }

  if (enabled) {
    return (
      <section>
        <p role="status">Notifications are on. New claims will pop up on this device.</p>
      </section>
    )
  }

  return (
    <section>
      <button type="button" onClick={enable} disabled={pending}>
        {pending ? 'Enabling notifications…' : 'Enable notifications'}
      </button>
      <p>Get a popup on this device the moment a claim is assigned to you.</p>
      {failed && <p role="alert">{failed}</p>}
    </section>
  )
}
