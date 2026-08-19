/** Per-profile-type metadata (design.md §4 field sets) so four CRUDs share one page pair. */

export interface FieldDef {
  name: string
  label: string
  required: boolean
}

export interface ProfileKind {
  slug: string
  label: string
  fields: FieldDef[]
}

export const PROFILE_KINDS: ProfileKind[] = [
  {
    slug: 'experts',
    label: 'Experts',
    fields: [
      { name: 'email', label: 'Email', required: true },
      { name: 'next3Id', label: 'NEXT3 ID', required: false },
    ],
  },
  {
    slug: 'garages',
    label: 'Garages',
    fields: [
      { name: 'contactName', label: 'Contact name', required: true },
      { name: 'contactPhone', label: 'Contact phone', required: false },
      { name: 'mobile', label: 'Mobile', required: false },
      { name: 'email', label: 'Email', required: true },
      { name: 'next3Id', label: 'NEXT3 ID', required: false },
      { name: 'address', label: 'Address', required: false },
      { name: 'openingHours', label: 'Opening hours', required: false },
    ],
  },
  {
    slug: 'claim-officers',
    label: 'Claim officers',
    fields: [
      { name: 'next3User', label: 'NEXT3 user', required: true },
      { name: 'email', label: 'Email', required: true },
    ],
  },
  {
    slug: 'brokers',
    label: 'Brokers',
    fields: [
      { name: 'irisCode', label: 'IRIS code', required: true },
      { name: 'email', label: 'Email', required: true },
    ],
  },
]

export function findKind(slug: string | undefined): ProfileKind | undefined {
  return PROFILE_KINDS.find((k) => k.slug === slug)
}

export function apiBase(kind: ProfileKind): string {
  return `/api/admin/${kind.slug}`
}
