import { EXPERT_PUSH_COPY, type PushCopy } from './copy'
import { usePushSubscription, type UsePushSubscriptionOptions } from './usePushSubscription'

export interface PushPanelProps extends UsePushSubscriptionOptions {
  copy?: PushCopy
}

/**
 * The "Enable notifications" control (design.md §8, slice 3.4). Lives in a layout rather than on a
 * page, so it is on screen wherever the user is — someone who dismissed it once should not have to go
 * looking.
 *
 * The button exists because the permission prompt needs a user gesture; see `usePushSubscription`.
 * The `ArrivedPanel` shape: a section, one button, and `role="alert"` on anything that went wrong.
 */
export function PushPanel({ copy = EXPERT_PUSH_COPY, ...options }: PushPanelProps = {}) {
  const { enable, pending, enabled, unsupported, failed } = usePushSubscription(options)

  if (unsupported) {
    // No button at all rather than one that cannot work: offering an action that is guaranteed to
    // fail is worse than saying plainly that this browser will not do it.
    return (
      <section>
        <p role="status">{copy.unsupported}</p>
      </section>
    )
  }

  if (enabled) {
    return (
      <section>
        <p role="status">{copy.enabled}</p>
      </section>
    )
  }

  return (
    <section>
      <button type="button" onClick={enable} disabled={pending}>
        {pending ? 'Enabling notifications…' : 'Enable notifications'}
      </button>
      <p>{copy.invitation}</p>
      {failed && <p role="alert">{failed}</p>}
    </section>
  )
}
