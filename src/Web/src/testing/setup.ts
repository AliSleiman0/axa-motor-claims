import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// @testing-library/react registers its own cleanup only when a global `afterEach` exists, and this
// project runs Vitest without globals (so tsconfig's `types` and the ESLint globals list stay as
// they are). Registered here instead: without it every render stacks into the same document and
// role queries start finding two of everything.
afterEach(cleanup)
