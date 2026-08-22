import { EXPERT_PUSH_COPY } from './copy'
import { usePushSubscription, type UsePushSubscriptionOptions } from './usePushSubscription'

/**
 * "This browser cannot show claim notifications. New claims will still appear in My claims."
 *
 * Said on the **sign-in** screen (pass 2's `S2CodeRejected`), before anybody is signed in, because a
 * field expert who will never get popups should learn it here rather than at a crash site — §8's
 * assignment popup is the BRD's primary trigger, and the app is a great deal less useful without it.
 *
 * **The message and never the button.** `PushPanel` offers "Enable notifications", and pressing it
 * from a signed-out page would reach `POST /api/push/subscriptions` and take a 401 — an action that
 * cannot succeed is worse than none, which is the same reasoning `PushPanel` already applies to an
 * unsupported browser. Renders nothing at all when push works, so a capable browser sees no notice
 * about a problem it does not have.
 *
 * Safe on an unauthenticated page because it only ever *reads*: `unsupported` comes from feature
 * detection and a permission read, neither of which prompts, touches the network, or needs a token.
 */
export function PushUnsupportedNotice(options: UsePushSubscriptionOptions = {}) {
  const { unsupported } = usePushSubscription(options)

  if (!unsupported) return null

  return (
    <p className="muted" role="status">
      {EXPERT_PUSH_COPY.unsupported}
    </p>
  )
}
