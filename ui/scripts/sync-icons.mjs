#!/usr/bin/env node
/**
 * Syncs app icons from the Umbrel app store into the product.
 *
 * 1. Shallow-clones getumbrel/umbrel-apps and reads every app's
 *    docker-compose.yml to learn which container images the app uses.
 * 2. Shallow (sparse) clones getumbrel/umbrel-apps-gallery and copies each
 *    app's icon into ui/public/icons/<app-id>.<ext>.
 * 3. Writes api/src/Api/Features/Apps/Icons/app-icons.json mapping
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
const mappingOutFile = path.join(repoRoot, 'api', 'src', 'Api', 'Features', 'Apps', 'Icons', 'app-icons.json')

const ICON_EXTENSIONS = ['.svg', '.png', '.webp', '.jpg', '.jpeg']

function git(args, cwd) {
  execFileSync('git', args, {cwd, stdio: 'inherit'})
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

function collectImages(appsRepoDir) {
  const images = new Map() // normalized image -> app id

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

    for (const [serviceName, service] of Object.entries(services)) {
      const image = service?.image
      if (typeof image !== 'string' || image.includes('${')) continue

      const normalized = normalizeImage(image)
      if (!normalized) continue

      // First app id (alphabetical) claiming an image wins.
      if (!images.has(normalized)) images.set(normalized, appId)
      else if (images.get(normalized) !== appId)
        console.warn(
          `Image ${normalized} is used by both "${images.get(normalized)}" and "${appId}" (service "${serviceName}"); keeping the first.`,
        )
    }
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
    .filter(([image, appId]) => iconFiles.has(appId))
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
