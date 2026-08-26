/**
 * What a role's notifications are actually about (design.md §8).
 *
 * These live in a `.ts` file rather than beside `PushPanel` because `react-refresh/only-export-components`
 * — an error here, via `--max-warnings 0` — allows a `.tsx` file to export components and nothing
 * else. Same reason `TestQueryProvider` is a component rather than a render helper.
 *
 * The split exists at all because of slice 4.2's browser pass: `PushPanel` was written for
 * `ExpertLayout` and promised a popup "the moment a claim is assigned to you", which appeared above
 * every garage screen. A garage is never assigned a claim — §8's garage rows are decisions on work
 * the garage filed. The panel is otherwise genuinely role-agnostic, so the words were the only thing
 * that had to become a parameter.
 */
export interface PushCopy {
  /** The one-line promise under the button. */
  invitation: string
  /** What the "already on" line says. */
  enabled: string
  /** What a browser that cannot do push is told, including what still works without it. */
  unsupported: string
}

/** §8's expert row: the BRD's primary trigger, "a popup message will show on the expert mobile". */
/**
 * Shown under the panel in the Android shell only (slice 6.3).
 *
 * **Copy rather than a prompt, and that is a scope decision.** Aggressive OEM battery management on
 * Samsung, Xiaomi, Vivo and Oppo kills the process holding FCM's socket, and delivery degrades
 * sharply when an app has not been opened recently — `research-capacitor.md` §3, and it is the
 * single biggest threat to the BRD's primary trigger in MENA. The in-app fix is an
 * `ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS` intent, which needs a community settings plugin and
 * therefore a licence review the half-day does not cover. So the app says the thing a person can act
 * on, and `docs/oem-push-guidance.md` carries the per-manufacturer steps for the rollout notes.
 *
 * Not role-specific — it is a property of the handset, not of what the person does with it — so it
 * sits beside the role copies rather than inside them.
 */
export const NATIVE_BATTERY_GUIDANCE =
  'If claims stop arriving, check that this app is excluded from battery optimisation — some ' +
  'phones stop background apps after a day or two, and notifications stop with them.'

export const EXPERT_PUSH_COPY: PushCopy = {
  invitation: 'Get a popup on this device the moment a claim is assigned to you.',
  enabled: 'Notifications are on. New claims will pop up on this device.',
  unsupported:
    'This browser cannot show claim notifications. New claims will still appear in My claims.',
}

/** §8's garage rows: approved and rejected, both about a declaration this garage filed. */
export const GARAGE_PUSH_COPY: PushCopy = {
  invitation: 'Get a popup on this device the moment AXA reviews one of your declarations.',
  enabled: 'Notifications are on. AXA’s decisions will pop up on this device.',
  unsupported:
    'This browser cannot show notifications. Decisions will still appear in My declarations.',
}
