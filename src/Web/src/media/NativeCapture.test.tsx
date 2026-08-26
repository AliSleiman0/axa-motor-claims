import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import { CapturePanel } from './CapturePanel'
import type { MediaConfig } from './config'
import { NativeCaptureCancelled, type NativeShell } from './nativeShell'

/**
 * `CapturePanel` inside the Android shell (slice 6.3).
 *
 * **What this file is really about is a file input that must not exist.** On Android the WebView
 * ignores `capture="environment"` and opens the whole photo library (6.3a, observed twice), so on
 * that platform the capture input *is* the gallery route — a picker wearing a camera's label, with
 * `origin = captured` written against whatever it returns. Every assertion below that a control is
 * absent is §7.1's capture-only rule, which is the BRD's hard rule and a fraud surface.
 *
 * The shell is injected. The real one reaches for `@capacitor/camera`, which jsdom cannot load and
 * `nativeImports.test.ts` pins as unreachable; the camera itself is a device-pass item.
 */
const PATH = '/api/expert/assignments/00000000-0000-0000-0000-00000000a11c/documents'

const CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'insured_car_photo', allowUpload: false, contentTypes: ['image/jpeg', 'image/png'] },
    {
      bucket: 'insured_documents',
      allowUpload: true,
      contentTypes: ['image/jpeg', 'image/png', 'application/pdf'],
    },
  ],
}

/** A PDF, so the clarity gate takes its `not_applicable` path without needing a canvas. */
function aFile() {
  return new File(['PLACEHOLDER'], 'front-of-the-car.pdf', { type: 'application/pdf' })
}

function nativeShell(takePhoto: NativeShell['takePhoto']): NativeShell {
  return { hasCamera: true, takePhoto }
}

function show(bucket: string, shell: NativeShell | null, qualifier?: string) {
  return render(
    <TestQueryProvider>
      <CapturePanel
        path={PATH}
        bucket={bucket}
        label="PLACEHOLDER bucket"
        config={CONFIG}
        shell={shell}
        qualifier={qualifier}
      />
    </TestQueryProvider>,
  )
}

describe('CapturePanel in the native shell', () => {
  it('replaces the capture input with a camera button', async () => {
    show('insured_car_photo', nativeShell(vi.fn()))

    // The accessible name is unchanged, so `docs/demo-week4.md`'s script and every existing query
    // read identically on both platforms — only the element behind it changed.
    const control = screen.getByRole('button', { name: 'Take a photo' })
    expect(control).toBeDefined()
    expect(control.tagName).toBe('BUTTON')

    // **The assertion that matters.** A `<input type="file">` here is the gallery on Android,
    // whatever its `capture` attribute claims.
    expect(document.querySelector('input[type="file"]')).toBeNull()
  })

  it('keeps the file picker for a bucket that allows upload', async () => {
    show('insured_documents', nativeShell(vi.fn()))

    // A picker is *allowed* on these buckets — §7.1 says documents may be uploaded or captured — so
    // the native camera replaces only the capture control. Taking the upload input away would be a
    // silent narrowing of what a garage can attach.
    expect(screen.getByRole('button', { name: 'Take a photo' })).toBeDefined()
    expect(screen.getByLabelText('Choose a file')).toBeDefined()
  })

  it('keeps the capture input in a plain browser', () => {
    show('insured_car_photo', null)

    // The regression guard for every desktop role and the iOS PWA, where Safari honours `capture`
    // and the input is correct. `shell` is null there, and this path must be the code that shipped.
    expect(screen.queryByRole('button', { name: 'Take a photo' })).toBeNull()
    expect(screen.getByLabelText('Take a photo')).toBeDefined()
    expect(document.querySelector('input[type="file"]')).not.toBeNull()
  })

  it('walks a native photo through the clarity confirm screen', async () => {
    const user = userEvent.setup({ delay: null })
    show('insured_car_photo', nativeShell(() => Promise.resolve(aFile())))

    await user.click(screen.getByRole('button', { name: 'Take a photo' }))

    // The whole point of returning a `File`: nothing downstream knows a native camera was involved,
    // so §7.2's gate and the confirm screen are one implementation on both platforms.
    expect(await screen.findByRole('button', { name: 'Retake' })).toBeDefined()
    expect(screen.getByRole('button', { name: /confirm/i })).toBeDefined()
  })

  it('passes the bucket qualifier to the camera', async () => {
    const user = userEvent.setup({ delay: null })
    const takePhoto = vi.fn(() => Promise.resolve(aFile()))
    show('insured_car_photo', nativeShell(takePhoto), 'insured car')

    await user.click(screen.getByRole('button', { name: 'Take a photo — insured car' }))

    // E2 shows five of these panels. The qualifier is what names the file — 6.3a found every iOS
    // capture arriving as `image.jpg` (#46), so a photograph that reaches NEXT3 named for its bucket
    // is the difference between five distinguishable attachments and five identical ones.
    await waitFor(() => expect(takePhoto).toHaveBeenCalledWith('insured car'))
  })

  it('says nothing when the camera is closed without taking anything', async () => {
    const user = userEvent.setup({ delay: null })
    show('insured_car_photo', nativeShell(() => Promise.reject(new NativeCaptureCancelled())))

    await user.click(screen.getByRole('button', { name: 'Take a photo' }))

    // Backing out is an ordinary thing to do. An error banner here would train an expert at a crash
    // site to ignore the one element on the screen that reports real failures.
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(screen.getByRole('button', { name: 'Take a photo' })).toBeDefined()
  })

  it('reports a camera that genuinely failed', async () => {
    const user = userEvent.setup({ delay: null })
    show('insured_car_photo', nativeShell(() => Promise.reject(new Error('no camera'))))

    await user.click(screen.getByRole('button', { name: 'Take a photo' }))

    // A denied permission or a missing camera is not cancellation, and the expert has to be told
    // something happened — otherwise the button simply appears not to work.
    expect(await screen.findByRole('alert')).toBeDefined()
  })
})
