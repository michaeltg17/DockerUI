#!/usr/bin/env node
/**
 * Syncs app icons from the Umbrel app store into the product.
 *
 * 1. Shallow-clones getumbrel/umbrel-apps and reads every app's
 *    docker-compose.yml to learn which container images the app uses.
 * 2. Shallow (sparse) clones getumbrel/umbrel-apps-gallery and copies each
 *    app's icon into ui/public/icons/<app-id>.<ext>.
 * 3. Writes api/Features/Apps/Icons/app-icons.json mapping
 *    container image -> icon file (served at /icons/<file>).
 *
 * Run from the repository root:  yarn --cwd ui icons:sync
 */

import {execFileSync} from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import YAML from 'yaml'

const APPS_REPO = 'https://github.com/getumbrel/umbrel-apps.git'
const GALLERY_REPO = 'https://github.com/getumbrel/umbrel-apps-gallery.git'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(scriptDir, '..', '..')
const iconsOutDir = path.join(repoRoot, 'ui', 'public', 'icons')
const mappingOutFile = path.join(repoRoot, 'api', 'Features', 'Apps', 'Icons', 'app-icons.json')

const ICON_EXTENSIONS = ['.svg', '.png', '.webp', '.jpg', '.jpeg']

function git(args, cwd) {
  // A dev-only sync tool; it shells out to the PATH git by design.
  execFileSync('git', args, {cwd, stdio: 'inherit'}) // NOSONAR
}

function normalizeImage(image) {
  let reference = image.trim().toLowerCase()
  const digest = reference.indexOf('@')
  if (digest !== -1) reference = reference.slice(0, digest)
  const tag = reference.lastIndexOf(':')
  if (tag !== -1 && !reference.slice(tag + 1).includes('/')) reference = reference.slice(0, tag)
  return reference
}

function listSubdirectories(dir) {
  return fs
    .readdirSync(dir, {withFileTypes: true})
    .filter((entry) => entry.isDirectory() && !entry.name.startsWith('.'))
    .map((entry) => entry.name)
    .sort()
}

function cloneAppsRepo(tempDir) {
  const dir = path.join(tempDir, 'umbrel-apps')
  console.log('Cloning umbrel-apps (shallow)...')
  git(['clone', '--depth', '1', '--quiet', APPS_REPO, dir])
  return dir
}

function cloneGalleryRepo(tempDir) {
  const dir = path.join(tempDir, 'umbrel-apps-gallery')
  console.log('Cloning umbrel-apps-gallery (shallow, icons only)...')
  try {
    git(['clone', '--depth', '1', '--filter=blob:none', '--sparse', '--quiet', GALLERY_REPO, dir])
    git(['sparse-checkout', 'set', '--no-cone', '*/icon.*'], dir)
  } catch {
    console.warn('Sparse clone failed, falling back to a full shallow clone...')
    fs.rmSync(dir, {recursive: true, force: true})
    git(['clone', '--depth', '1', '--quiet', GALLERY_REPO, dir])
  }
  return dir
}

const APP_PROXY_HOST_ENV = 'APP_HOST'
const MAIN_SERVICE_NAME = 'app'

function getEnvironmentValue(service, name) {
  const environment = service?.environment
  if (environment === null || typeof environment !== 'object') return undefined
  if (Array.isArray(environment)) {
    const prefix = `${name}=`
    const entry = environment.find((item) => typeof item === 'string' && item.startsWith(prefix))
    return typeof entry === 'string' ? entry.slice(prefix.length) : undefined
  }
  return Object.hasOwn(environment, name) ? String(environment[name]) : undefined
}

// Resolves an app_proxy APP_HOST value (service name, container_name, or the
// compose <app-id>_<service>_<n> container-name form) to a service name.
function resolveHostToService(appId, host, services) {
  let value = host.trim()
  const scheme = value.indexOf('://')
  if (scheme !== -1) value = value.slice(scheme + 3)
  value = value.split('/')[0]
  const port = value.lastIndexOf(':')
  if (port !== -1 && /^\d+$/.test(value.slice(port + 1))) value = value.slice(0, port)
  if (!value) return null

  if (Object.hasOwn(services, value)) return value
  for (const [name, service] of Object.entries(services))
    if (typeof service?.container_name === 'string' && service.container_name === value) return name

  const suffix = value.match(/_\d+$/)
  if (value.startsWith(`${appId}_`) && suffix) {
    const base = value.slice(appId.length + 1, value.length - suffix[0].length)
    if (Object.hasOwn(services, base)) return base
  }
  return null
}

function findMainService(appId, services) {
  const resolved = new Set()
  for (const service of Object.values(services)) {
    const host = getEnvironmentValue(service, APP_PROXY_HOST_ENV)
    if (host === undefined) continue
    const target = resolveHostToService(appId, host, services)
    if (target) resolved.add(target)
  }
  if (resolved.size === 1) return [...resolved][0]
  if (resolved.size > 1) {
    const sorted = [...resolved].sort((a, b) => a.localeCompare(b))
    console.warn(
      `App ${appId} has app_proxy hosts pointing at multiple services (${sorted.join(', ')}); using "${sorted[0]}" as the main service.`,
    )
    return sorted[0]
  }
  return Object.hasOwn(services, MAIN_SERVICE_NAME) ? MAIN_SERVICE_NAME : null
}

function collectImages(appsRepoDir) {
  const apps = [] // { appId, services, mainService } in alphabetical order

  for (const appId of listSubdirectories(appsRepoDir)) {
    const composeFile = path.join(appsRepoDir, appId, 'docker-compose.yml')
    if (!fs.existsSync(composeFile)) continue

    let doc
    try {
      doc = YAML.parse(fs.readFileSync(composeFile, 'utf8'))
    } catch (error) {
      console.warn(`Skipping ${appId}: could not parse docker-compose.yml (${error.message})`)
      continue
    }

    const services = doc?.services
    if (services === null || typeof services !== 'object' || Array.isArray(services)) continue

    apps.push({appId, services, mainService: findMainService(appId, services)})
  }

  // normalized image -> { main: [], deps: [] } of { appId, serviceName }
  const claims = new Map()
  const addClaim = (normalized, claim, tier) => {
    if (!claims.has(normalized)) claims.set(normalized, {main: [], deps: []})
    claims.get(normalized)[tier].push(claim)
  }

  // Two passes so every main-service claim is known before any dependency claim.
  for (const {appId, services, mainService} of apps) {
    if (!mainService) continue
    const image = services[mainService]?.image
    if (typeof image !== 'string' || image.includes('${')) continue
    const normalized = normalizeImage(image)
    if (normalized) addClaim(normalized, {appId, serviceName: mainService}, 'main')
  }
  for (const {appId, services, mainService} of apps) {
    for (const [serviceName, service] of Object.entries(services)) {
      if (serviceName === mainService) continue
      const image = service?.image
      if (typeof image !== 'string' || image.includes('${')) continue
      const normalized = normalizeImage(image)
      if (normalized) addClaim(normalized, {appId, serviceName}, 'deps')
    }
  }

  const images = new Map() // normalized image -> app id
  for (const [normalized, {main, deps}] of claims) {
    // A main-service (app_proxy) claim owned by exactly one app wins outright.
    if (main.length === 1) {
      images.set(normalized, main[0].appId)
      continue
    }

    const seen = new Set()
    const candidates = [...main, ...deps].filter((claim) => !seen.has(claim.appId) && seen.add(claim.appId))
    candidates.sort((a, b) => a.appId.localeCompare(b.appId))

    // Otherwise an app whose id is the image's last path segment wins
    // (searxng beats lobe-chat for ghcr.io/searxng/searxng); failing that the
    // first app id (alphabetical) claiming an image wins.
    const lastSegment = normalized.split('/').pop()
    const winner = candidates.find((claim) => claim.appId === lastSegment) ?? candidates[0]
    images.set(normalized, winner.appId)

    if (winner === candidates[0])
      for (const other of candidates.slice(1))
        console.warn(
          `Image ${normalized} is used by both "${winner.appId}" and "${other.appId}" (service "${other.serviceName}"); keeping the first.`,
        )
  }

  return images
}

function copyIcons(galleryRepoDir) {
  // The icons directory is fully generated; start clean.
  fs.rmSync(iconsOutDir, {recursive: true, force: true})
  fs.mkdirSync(iconsOutDir, {recursive: true})

  const iconFiles = new Map() // app id -> file name in ui/public/icons

  for (const appId of listSubdirectories(galleryRepoDir)) {
    const source = ICON_EXTENSIONS.map((ext) => path.join(galleryRepoDir, appId, `icon${ext}`)).find(
      (candidate) => fs.existsSync(candidate),
    )

    if (!source) continue

    const targetName = `${appId}${path.extname(source)}`
    fs.copyFileSync(source, path.join(iconsOutDir, targetName))
    iconFiles.set(appId, targetName)
  }

  return iconFiles
}

function writeMapping(images, iconFiles) {
  const entries = [...images.entries()]
    .filter(([, appId]) => iconFiles.has(appId))
    .map(([image, appId]) => ({image, icon: iconFiles.get(appId)}))
    .sort((a, b) => a.image.localeCompare(b.image) || a.icon.localeCompare(b.icon))

  fs.mkdirSync(path.dirname(mappingOutFile), {recursive: true})
  fs.writeFileSync(mappingOutFile, `${JSON.stringify(entries, null, 2)}\n`)

  return entries
}

function main() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'DockerUI-icons-'))

  try {
    const appsRepoDir = cloneAppsRepo(tempDir)
    const galleryRepoDir = cloneGalleryRepo(tempDir)

    const images = collectImages(appsRepoDir)
    const iconFiles = copyIcons(galleryRepoDir)
    const entries = writeMapping(images, iconFiles)

    console.log(`Done: ${iconFiles.size} icons, ${entries.length} image mappings written to ${mappingOutFile}`)
  } finally {
    fs.rmSync(tempDir, {recursive: true, force: true})
  }
}

main()
