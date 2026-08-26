/**
 * The native shell seam (slice 6.3).
 *
 * **Why this exists at all.** design.md §7.1 hedged that `capture="environment"` is "a hint, not a
 * guarantee" in a browser. Slice 6.3a measured what that means and the two platforms answer in
 * opposite directions: iOS Safari honours it and goes straight to the camera, while the **Android
 * WebView ignores it outright** and opens `PhotopickerGetContentActivity` — the whole photo library,
 * with no `ACTION_IMAGE_CAPTURE` fired at all. So on Android an expert or a garage can attach a
 * screenshot, an old photograph, or a picture of a picture to a bucket the BRD requires to be taken
 * at the scene, and the row is written `origin = captured` because the web layer believes it
 * captured it. That is precisely the fraud surface capture-only exists to close, and only a native
 * camera call closes it.
 *
 * **The house idiom, twice over.** The shell is a parameter with a default
 * (`decode`'s and `requestPosition`'s shape), and the platform check is a **property probe** rather
 * than an import — `window.Capacitor` is injected by the native bridge and simply is not there in a
 * browser. Nothing in this module imports a Capacitor package at the top level; the real camera call
 * is behind a dynamic `import()`, so jsdom never loads one and a browser build puts it in an async
 * chunk that is fetched only if a native shell asks for it.
 */

/** What the app needs from a native shell. Deliberately tiny: one capability, one call. */
export interface NativeShell {
  /** Whether the shell can take a photograph without offering a gallery. */
  hasCamera: boolean
  /**
   * Opens the system camera and resolves with what was taken.
   *
   * Rejects with {@link NativeCaptureCancelled} if the person backed out, which is not an error and
   * must not be shown as one.
   */
  takePhoto: (qualifier?: string) => Promise<File>
}

/** The person closed the camera without taking anything. Ordinary, and not a failure. */
export class NativeCaptureCancelled extends Error {
  constructor() {
    super('The camera was closed without taking a photo.')
    this.name = 'NativeCaptureCancelled'
  }
}

/**
 * The bridge object Capacitor injects into the WebView. Declared structurally rather than imported
 * from `@capacitor/core`, so that reading it costs nothing in a browser and drags no module in.
 */
interface CapacitorBridge {
  isNativePlatform?: () => boolean
}

/**
 * The running native shell, or `null` in an ordinary browser.
 *
 * `null` rather than a shell whose `hasCamera` is false, so that every call site reads as "is there
 * a native shell" and the browser path stays literally the code that shipped in 2.5 — a flag would
 * have invited a second branch inside the browser branch.
 */
export function detectNativeShell(): NativeShell | null {
  const bridge = (globalThis as { Capacitor?: CapacitorBridge }).Capacitor

  // Optional-called rather than assumed: the property exists on some Capacitor versions as a value
  // and the probe must not throw on a shape it does not recognise.
  if (bridge?.isNativePlatform?.() !== true) {
    return null
  }

  return { hasCamera: true, takePhoto: takeNativePhoto }
}

/**
 * Takes a photograph through `@capacitor/camera`.
 *
 * **`source: CameraSource.Camera` is the entire point of the slice.** The default is `Prompt`, which
 * offers "Photo Library" beside "Take Picture" and would reopen exactly the hole this closes; and
 * `resultType: Uri` rather than `Base64`, because a 12–48 MP photograph as a base64 string is a
 * multi-megabyte string copy on the handset of somebody standing at a crash site — the same reason
 * §7.2's decoder returns a *bounded* buffer.
 *
 * The returned `File` then walks the ordinary pipeline: the clarity gate, the confirm screen, the
 * upload. Nothing downstream knows a native camera was involved, which is what keeps `useCapture`
 * and the §7.2 gate identical on both platforms.
 */
async function takeNativePhoto(qualifier?: string): Promise<File> {
  const { Camera, CameraResultType, CameraSource } = await import('@capacitor/camera')

  let photo
  try {
    photo = await Camera.getPhoto({
      quality: 90,
      allowEditing: false,
      resultType: CameraResultType.Uri,
      source: CameraSource.Camera,
      // Web is unreachable here (this only runs behind the native probe) and is set anyway so a
      // misconfigured build fails loudly at the plugin rather than silently opening a file input.
      promptLabelHeader: qualifier ? `Photograph the ${qualifier}` : 'Take a photo',
    })
  } catch (error) {
    // The plugin reports a back-press as a rejection with a message rather than a typed error, so
    // this is the one place a string match is unavoidable. Treated as cancellation only when it
    // looks like one; anything else keeps its own message and surfaces as a real failure.
    if (looksCancelled(error)) throw new NativeCaptureCancelled()
    throw error
  }

  if (!photo.webPath) {
    throw new Error('The camera returned no image.')
  }

  // `webPath` is a local `capacitor://` URL the WebView can read directly, so this fetch never
  // leaves the device.
  const response = await fetch(photo.webPath)
  const blob = await response.blob()

  const format = photo.format || 'jpeg'
  const type = blob.type || `image/${format}`

  // A real name, because §4's `file_name` is what stops a garage's photograph reaching NEXT3's
  // Survey folder as a bare id — and 6.3a found every iOS capture arriving as `image.jpg` (#46), so
  // naming it here is the one chance to make the file distinguishable at all.
  const name = qualifier
    ? `${qualifier.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.${format}`
    : `photo.${format}`

  return new File([blob], name, { type })
}

function looksCancelled(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error)
  return /cancel/i.test(message) || /no image (picked|selected)/i.test(message)
}
