import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..', '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const decks = read('opcgpro-vue/src/l12/decks.ts')
const assets = read('opcgpro-vue/src/l12/cardAssets.ts')
const cardLoader = read('opcgpro-vue/src/data/CardLoader.ts')
const platform = read('opcgpro-vue/src/l12/platform.ts')
const gallery = read('服务端WebSocket/TwelveLegions/L12WebSocketServer.Gallery.cs')
const server = read('服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const webNginx = read('ops/server/nginx-l12-web-assets.conf')
const testNginx = read('ops/server/legion12-testrun.nginx')
const pathNginx = read('ops/server/legion12-testrun-path.nginx')

for (const file of ['cards.s1.json', 'cards.lookup.json', 'cards.st.json']) {
  assert.match(decks, new RegExp(`fetch\\(deploymentPath\\('/data/l12/${file.replace('.', '\\.')}'\\), \\{ cache: 'default' \\}\\)`),
    `${file} must use the browser's bounded cache`)
}
assert.match(decks, /let officialPresetDecksPromise: Promise<OfficialL12PresetDeck\[\]> \| null = null/,
  'official preset decks must share a route-level in-flight/fulfilled promise')
assert.match(decks, /fetch\(deploymentPath\('\/data\/l12\/preset-decks\.s1\.json'\), \{ cache: 'default' \}\)/,
  'season one preset decks must use the browser cache')
assert.match(decks, /fetch\(deploymentPath\('\/data\/l12\/preset-decks\.s2\.json'\), \{ cache: 'default' \}\)/,
  'season two preset decks must use the browser cache')
assert.match(assets, /cache: force \? 'reload' : 'default'/,
  'card asset manifest must use local cache normally and force reload only for a missing entry')
assert.match(cardLoader, /const cardSetPromises = new Map<string, Promise<CardData\[\]>>\(\)/,
  'battle card sets must share in-flight requests across components')
assert.match(cardLoader, /fetch\(deploymentPath\(path\), \{ cache: "default" \}\)/,
  'battle card sets must allow the browser cache')
assert.match(cardLoader, /MANIFEST_RETRY_DELAY_MS = 30_000/,
  'card image manifest failures must retry with a bounded backoff')
assert.match(platform, /const publicReadCache = new Map/, 'public read cache must be bounded in memory')
assert.match(platform, /const PUBLIC_READ_CACHE_TTL_MS = 60_000/, 'public read cache TTL must remain bounded')
assert.match(platform, /browserCacheablePublicRead = path === '\/api\/alternate-arts' \\|\\| path === '\/api\/gallery\/presentation'/,
  'only the public gallery projections may opt into browser caching')
assert.match(platform, /browserCacheablePublicRead \? \{ cache: 'default' as const \}/,
  'public gallery requests must allow the browser cache')
assert.match(platform, /cachedPublicRead\('alternate-arts'/, 'gallery list must use the shared cache')
assert.match(platform, /cachedPublicRead\('gallery-presentation'/, 'gallery presentation must use the shared cache')
assert.match(gallery, /CacheControl = "public,max-age=60,stale-while-revalidate=300"/,
  'public gallery endpoints must publish a bounded cache policy')
assert.match(server, /viewer is null[\s\S]*?public,max-age=30,stale-while-revalidate=120[\s\S]*?no-store/,
  'deck library must cache anonymous projections only and keep viewer-specific reads no-store')
for (const config of [webNginx, testNginx, pathNginx]) {
  assert.match(config, /data\/l12\//, 'catalog JSON must have a static cache route')
  assert.match(config, /imageManifest\|allCards/, 'battle card JSON must have a static cache route')
  assert.match(config, /public, max-age=300, must-revalidate/, 'catalog route must use a bounded revalidation window')
}
assert.match(pathNginx, /<l12_catalog_file>cards\\\.\(\?:s1\|lookup\|st\)\|preset-decks\\\.s\[12\]/,
  'mounted test catalog route must capture the filename for regex alias resolution')
assert.match(pathNginx, /alias \/opt\/legion12-testrun\/opcgpro-vue\/dist-testrun\/data\/l12\/\$l12_catalog_file\\?\.json;/,
  'mounted test catalog route must alias the captured file directly')
assert.match(pathNginx, /<l12_data_file>\(\?:imageManifest\|allCards\|/,
  'mounted test card-data route must capture the filename for regex alias resolution')
assert.match(pathNginx, /alias \/opt\/legion12-testrun\/opcgpro-vue\/dist-testrun\/data\/\$l12_data_file;/,
  'mounted test card-data route must alias the captured file directly')
assert.ok(!pathNginx.includes('alias /opt/legion12-testrun/opcgpro-vue/dist-testrun/data/l12/;\r\n    try_files $uri =404;')
  && !pathNginx.includes('alias /opt/legion12-testrun/opcgpro-vue/dist-testrun/data/l12/;\n    try_files $uri =404;'),
  'mounted test regex alias must not reapply the full URI through try_files')
console.log('Public read cache contract passed: static catalogs, card manifest, gallery projections and anonymous deck directory are bounded; account/match/admin reads remain uncached')
