import { detectNativeShell } from '../media/nativeShell'
import { Button } from '../ui/Button'
import { EXPERT_PUSH_COPY, NATIVE_BATTERY_GUIDANCE, type PushCopy } from './copy'
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

  // Read from the same seam the hook uses, so the panel and the enable path can never disagree
  // about which platform they are on.
  const native = options.shellIsNative ?? detectNativeShell() !== null

  if (unsupported) {
    // No button at all rather than one that cannot work: offering an action that is guaranteed to
    // fail is worse than saying plainly that this browser will not do it.
    return (
      <section className="push-panel">
        <p className="push-panel__copy" role="status">
          {copy.unsupported}
        </p>
      </section>
    )
  }

  if (enabled) {
    return (
      <section className="push-panel">
        <p className="push-panel__copy" role="status">
          {copy.enabled}
        </p>
        {/* Only once notifications are actually on: told before that, it is advice about a thing
            that is not happening yet, on a screen already asking for one decision. */}
        {native ? <p className="push-panel__copy muted">{NATIVE_BATTERY_GUIDANCE}</p> : null}
      </section>
    )
  }

  return (
    <section className="push-panel">
      <Button variant="primary" onClick={enable} disabled={pending}>
        {pending ? 'Enabling notifications…' : 'Enable notifications'}
      </Button>
      <p className="push-panel__copy">{copy.invitation}</p>
      {failed && (
        <p className="banner banner--alert" role="alert">
          {failed}
        </p>
      )}
    </section>
  )
}
