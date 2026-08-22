import { readFileSync } from 'node:fs'
import { mergeConfig } from 'vite'
import base from './vite.config.ts'

/*
 * The iPhone's dev server (slice 6.3a). Run by `scripts/spike-device.ps1`, never by `npm run dev`:
 *
 *     npm run dev -- --config vite.config.spike.ts --port 5174 --strictPort
 *
 * Safari gives a page no camera, no geolocation, no service worker and no web push without a secure
 * context, and it will not trust the ASP.NET dev certificate — so the phone needs this origin over
 * HTTPS on the LAN with an mkcert leaf whose root it has installed. `base` is imported rather than
 * copied so the `/api` and `/auth` proxy targets have exactly one definition; this file adds the
 * certificate and the LAN binding and changes nothing else.
 *
 * **This is a separate config file rather than a branch inside `vite.config.ts`, and that is the
 * whole point.** The first version of this gated on `SPIKE_HTTPS_CERT` / `SPIKE_HTTPS_KEY` env vars
 * set by the launching script. It worked — until `git checkout` touched `vite.config.ts`, Vite
 * restarted the server in-process, re-evaluated the config *without* those variables, and came back
 * on plain **http** with no LAN binding and no error. A silent HTTPS→HTTP downgrade is the worst
 * possible failure here: Safari drops the secure context, and the camera, the voice note and push all
 * stop working at once, on a phone, looking exactly like the platform limitations this spike exists to
 * measure. Reading from disk instead of from the process environment makes the config a pure function
 * of files that survive a restart.
 *
 * For the same reason a missing certificate **throws** rather than falling back to http. There is no
 * useful degraded mode: an http origin cannot answer a single one of the checks this server is for.
 */

const descriptorPath = new URL('../../demo-artifacts/certs/spike-https.json', import.meta.url)

interface HttpsDescriptor {
  cert: string
  key: string
}

let descriptor: HttpsDescriptor
try {
  descriptor = JSON.parse(readFileSync(descriptorPath, 'utf8')) as HttpsDescriptor
} catch (cause) {
  throw new Error(
    `No HTTPS certificate descriptor at ${descriptorPath.pathname}. ` +
      'This config is only for the device spike — start it with `.\\scripts\\spike-device.ps1`, ' +
      'which mints the certificate and writes that file.',
    { cause },
  )
}

export default mergeConfig(base, {
  server: {
    // `host` binds the LAN interface; the default binds localhost only and the phone cannot reach it.
    host: true,
    https: {
      cert: readFileSync(descriptor.cert),
      key: readFileSync(descriptor.key),
    },
  },
})
