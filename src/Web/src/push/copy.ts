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
