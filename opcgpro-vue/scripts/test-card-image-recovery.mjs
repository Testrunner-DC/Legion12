import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const root = path.resolve(here, '../src/l12')
const require = createRequire(import.meta.url), ts = require('typescript'), vue = require('vue')
const read = name => fs.readFileSync(path.join(root, name), 'utf8')
const compile = text => ts.transpileModule(text, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
const imageSource = read('CardImage.vue'), archiveSource = read('CardArchive.vue')
const back = '/assets/l12/card-back-official.png'
const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); await vue.nextTick() }
const scripts = [...imageSource.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)].map(match => match[1]).join('\n')
const ast = ts.createSourceFile('CardImage.ts', scripts, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const stripped = ast.statements.filter(node => !ts.isImportDeclaration(node)).map(node => node.getText(ast)).join('\n')
const names = ast.statements.flatMap(node => ts.isFunctionDeclaration(node) && node.name ? [node.name.text]
  : ts.isVariableStatement(node) ? node.declarationList.declarations.flatMap(item => ts.isIdentifier(item.name) ? [item.name.text] : []) : [])

function fixture(cardId = 'A', options = {}) {
  let now = 0, wallOffset = 0, timerId = 0, disposed = false
  const timers = new Map(), mounted = [], unmounted = [], listeners = new Map(), observers = new Set()
  const emitted = []
  const add = (name, fn) => { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(fn) }
  const remove = (name, fn) => listeners.get(name)?.delete(fn)
  const dispatch = name => { for (const fn of [...(listeners.get(name) || [])]) fn() }
  const props = vue.reactive({ cardId, legacyUrl: undefined, intent: 'thumb', alt: 'synthetic', eager: true })
  const document = { visibilityState: 'visible', addEventListener: add, removeEventListener: remove }
  class Observer {
    constructor(callback) { this.callback = callback; observers.add(this) }
    observe(target) { this.target = target }
    disconnect() { observers.delete(this) }
    unobserve() { this.disconnect() }
  }
  const source = () => ({ cardId: props.cardId, intent: props.intent, sources: [
    ...(options.cdn ? [{ kind: 'cdn', lowWebp: '/cdn.webp', webp: '/cdn.webp' }] : []),
    ...(props.cardId === 'HIDDEN' ? [] : [{ kind: 'sameOrigin', lowWebp: `/${props.cardId}.webp`, webp: `/${props.cardId}.webp` }]),
    { kind: 'placeholder', lowWebp: back, webp: back },
  ] })
  const window = { location: { href: 'https://synthetic.invalid/' }, innerWidth: 1000, innerHeight: 800,
    addEventListener: add, removeEventListener: remove, IntersectionObserver: Observer,
    setTimeout: (fn, delay = 0) => { const id = ++timerId; timers.set(id, { fn, at: now + delay }); return id }, clearTimeout: id => timers.delete(id) }
  const environment = { ...vue, window, document, navigator: { onLine: true }, IntersectionObserver: Observer,
    defineProps: () => props, withDefaults: value => value, defineEmits: () => (name, ...args) => emitted.push({ name, args }),
    onMounted: fn => mounted.push(fn), onBeforeUnmount: fn => unmounted.push(fn),
    CARD_IMAGE_PLACEHOLDER: back, peekCardAsset: source, fallbackCardAsset: source, resolveCardAsset: async () => source(),
    Date: class extends Date { static now() { return now + wallOffset } }, performance: { now: () => now }, URL, Map, Set, WeakMap, console }
  vm.createContext(environment)
  const scope = vue.effectScope()
  scope.run(() => vm.runInContext(compile(stripped + '\nglobalThis.__state={' + names.join(',') + '}'), environment))
  const state = environment.__state
  const createImage = () => ({ complete: false, naturalWidth: 0,
    currentSrc: new URL(state.imageUrl.value, window.location.href).href, src: new URL(state.imageUrl.value, window.location.href).href })
  let currentImage = createImage()
  if (state.imageRoot) state.imageRoot.value = { getBoundingClientRect: () => ({ top: 0, left: 0, bottom: 100, right: 70 }), querySelector: () => currentImage }
  scope.run(() => {
    vue.watch(state.renderKey, () => { currentImage = createImage() }, { flush: 'sync' })
    vue.watch(state.imageUrl, value => { currentImage.currentSrc = new URL(value, window.location.href).href }, { flush: 'sync' })
  })
  async function advance(ms) {
    const end = now + ms
    for (let count = 0; count < 1000; count++) {
      const entry = [...timers.entries()].filter(([, timer]) => timer.at <= end).sort((a, b) => a[1].at - b[1].at)[0]
      if (!entry) { now = end; await flush(); return }
      now = entry[1].at; timers.delete(entry[0]); entry[1].fn(); await flush()
    }
    throw Error('Timer recovery must stay bounded')
  }
  function visible(value, zeroArea = false) { for (const observer of [...observers]) observer.callback([{ target: observer.target, isIntersecting: value,
    intersectionRect: { width: value && !zeroArea ? 70 : 0, height: value ? 100 : 0 } }]) }
  function error() { currentImage.complete = false; currentImage.naturalWidth = 0; state.onError({ currentTarget: currentImage }) }
  function load(values = {}) { Object.assign(currentImage, { complete: true, naturalWidth: 100 }, values); state.onLoad({ currentTarget: currentImage }) }
  return { state, props, document, dispatch, visible, advance, timers, error, load, emitted, currentImage: () => currentImage, rewindWallClock() { wallOffset -= 1_000_000 },
    async mount() { for (const fn of mounted) await fn(); await flush() },
    async fail() { error(); await advance(300); error(); await advance(600); error(); await flush(); assert.equal(state.activeSource.value.kind, 'placeholder') },
    dispose() { if (!disposed) { disposed = true; unmounted.forEach(fn => fn()); scope.stop() } } }
}

async function renderedFixture() {
  const f = fixture()
  await f.mount()
  const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
  const descriptor = parse(imageSource, { filename: 'CardImage.vue' }).descriptor
  const script = compileScript(descriptor, { id: 'retired-source-contract' })
  const template = compileTemplate({ source: descriptor.template.content, filename: 'CardImage.vue', id: 'retired-source-contract',
    compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  const exports = {}
  vm.runInNewContext(ts.transpileModule(template.code, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText,
    { exports, require: specifier => { assert.equal(specifier, 'vue'); return vue } })
  const removed = []
  const createNode = tag => ({ tag, parent: null, children: [], attributes: new Map(),
    removeAttribute(name) { this.attributes.delete(name) },
    querySelector(selector) { return this.children.find(child => child.tag === selector) || null },
    get src() { return this.attributes.get('src') || '' }, get currentSrc() { return this.src },
    complete: false, naturalWidth: 0 })
  const renderer = vue.createRenderer({
    createElement: createNode, createText: text => ({ ...createNode('#text'), text }), createComment: createNode,
    setText: (node, text) => { node.text = text }, setElementText: (node, text) => { node.text = text },
    patchProp: (node, key, previous, value) => { if (value == null) node.attributes.delete(key); else node.attributes.set(key, value) },
    insert: (node, parent, anchor = null) => {
      if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1)
      const index = anchor ? parent.children.indexOf(anchor) : -1
      parent.children.splice(index < 0 ? parent.children.length : index, 0, node); node.parent = parent
    },
    remove: node => {
      removed.push({ node, src: node.attributes.get('src'), srcset: node.attributes.get('srcset') })
      node.parent?.children.splice(node.parent.children.indexOf(node), 1); node.parent = null
    },
    parentNode: node => node.parent,
    nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] || null,
  })
  const component = { props: Object.keys(f.props).concat('fit', 'objectPosition'), setup() {
    vue.onBeforeUnmount(() => f.dispose())
    return f.state
  }, render: exports.render }
  const root = createNode('root')
  const app = renderer.createApp({ render: () => vue.h(component, { ...f.props }) })
  app.mount(root); await flush()
  return { f, root, removed, picture: () => root.children.find(node => node.tag === 'picture'),
    dispose() { app.unmount(); f.dispose() } }
}

export async function runCardImageRecoveryTests() {
  const cases = []
  async function test(name, run) { try { await run(); cases.push({ name, passed: true }) } catch (error) { cases.push({ name, passed: false, error: error.stack }) } }
  await test('failed real source recovers on online while visible', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true); f.dispatch('online'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  await test('offscreen recovery waits for actual intersection', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(false); f.dispatch('online'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, back); f.visible(true); await f.advance(1500); assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  await test('background recovery waits for document foreground', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true); f.document.visibilityState = 'hidden'; f.dispatch('online'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, back); f.document.visibilityState = 'visible'; f.dispatch('visibilitychange'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  for (const card of ['A', 'HIDDEN']) await test('successful or deliberately concealed image never recovers/' + card, async () => {
    const f = fixture(card); try { await f.mount(); const key = f.state.renderKey.value, url = f.state.imageUrl.value
      for (let i = 0; i < 8; i++) f.dispatch('online'); await f.advance(60000)
      assert.equal(f.state.renderKey.value, key); assert.equal(f.state.imageUrl.value, url) } finally { f.dispose() }
  })
  await test('repeated online is deduplicated and failed recovery does not self-repeat', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true)
      for (let i = 0; i < 8; i++) f.dispatch('online'); await f.advance(1500); assert.equal(f.state.imageUrl.value, '/A.webp')
      await f.fail(); const key = f.state.renderKey.value; for (let i = 0; i < 8; i++) f.dispatch('online')
      await f.advance(20000); assert.equal(f.state.imageUrl.value, back); assert.equal(f.state.renderKey.value, key)
    } finally { f.dispose() }
  })
  await test('old short retry cannot act after card A-B-A', async () => {
    const f = fixture(); try { await f.mount(); f.error()
      f.props.cardId = 'B'; await flush(); f.props.cardId = 'A'; await flush(); const key = f.state.renderKey.value
      await f.advance(2000); assert.equal(f.state.renderKey.value, key) } finally { f.dispose() }
  })
  await test('unmount cancels queued recovery', async () => {
    const f = fixture(); await f.mount(); await f.fail(); f.visible(true); f.dispatch('online'); const key = f.state.renderKey.value
    f.dispose(); await f.advance(60000); assert.equal(f.state.renderKey.value, key)
  })
  await test('unmount cancels an existing short retry', async () => {
    const f = fixture(); await f.mount(); f.error()
    const key = f.state.renderKey.value; f.dispose(); await f.advance(2000); assert.equal(f.state.renderKey.value, key)
  })
  await test('successful recovery unregisters observers and later online does not reread', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true); f.dispatch('online'); await f.advance(1500)
      f.load()
      const key = f.state.renderKey.value; f.dispatch('online'); await f.advance(60000)
      assert.equal(f.state.renderKey.value, key); assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  for (const property of ['legacyUrl', 'intent']) await test('queued recovery cannot cross source identity/' + property, async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true); f.dispatch('online')
      f.props[property] = property === 'intent' ? 'board' : '/changed.webp'; await flush(); const key = f.state.renderKey.value
      await f.advance(1500); assert.equal(f.state.renderKey.value, key) } finally { f.dispose() }
  })
  await test('edge-only intersection has zero visible area and cannot recover', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true, true); f.dispatch('online'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, back); f.visible(true); await f.advance(1500); assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  await test('second online after cooldown recovers despite wall clock rollback', async () => {
    const f = fixture(); try { await f.mount(); await f.fail(); f.visible(true); f.dispatch('online'); await f.advance(1500)
      await f.fail(); await f.advance(31000); f.rewindWallClock(); f.dispatch('online'); await f.advance(1500)
      assert.equal(f.state.imageUrl.value, '/A.webp') } finally { f.dispose() }
  })
  await test('CDN exhaustion still falls back to same-origin and success is not reloaded', async () => {
    const f = fixture('A', { cdn: true }); try { await f.mount(); f.error(); await f.advance(300); f.error(); await f.advance(600); f.error()
      assert.equal(f.state.activeSource.value.kind, 'sameOrigin'); assert.equal(f.state.imageUrl.value, '/A.webp')
      f.load()
      const key = f.state.renderKey.value; f.dispatch('online'); await f.advance(60000); assert.equal(f.state.renderKey.value, key)
    } finally { f.dispose() }
  })
  await test('successful AVIF currentSrc remains stable on network recovery', async () => {
    const f = fixture(); try { await f.mount(); f.props.intent = 'detail'; await flush()
      f.state.resolved.value.sources[0].avif = '/A.avif'; f.state.highRequested.value = true
      f.load({ currentSrc: 'https://synthetic.invalid/A.avif' })
      const key = f.state.renderKey.value; f.dispatch('online'); await f.advance(60000)
      assert.equal(f.state.avifUrl.value, '/A.avif'); assert.equal(f.state.renderKey.value, key)
    } finally { f.dispose() }
  })
  for (const eventName of ['error', 'load']) await test('removed same-URL A node cannot act on successful new A/' + eventName, async () => {
    const f = fixture(); try { await f.mount(); const removed = f.currentImage()
      f.props.cardId = 'B'; await flush(); f.props.cardId = 'A'; await flush(); f.load()
      assert.notEqual(f.currentImage(), removed)
      Object.assign(removed, { complete: eventName === 'load', naturalWidth: eventName === 'load' ? 100 : 0 })
      const key = f.state.renderKey.value, emissions = f.emitted.length
      f.state[eventName === 'error' ? 'onError' : 'onLoad']({ currentTarget: removed })
      assert.equal(f.timers.size, 0); assert.equal(f.emitted.length, emissions)
      await f.advance(1000); assert.equal(f.state.renderKey.value, key)
    } finally { f.dispose() }
  })
  for (const eventName of ['error', 'load']) await test('events after unmount cannot schedule or emit/' + eventName, async () => {
    const f = fixture(); await f.mount(); const removed = f.currentImage(), emissions = f.emitted.length
    f.dispose(); Object.assign(removed, { complete: eventName === 'load', naturalWidth: eventName === 'load' ? 100 : 0 })
    f.state[eventName === 'error' ? 'onError' : 'onLoad']({ currentTarget: removed })
    assert.equal(f.timers.size, 0); assert.equal(f.emitted.length, emissions)
  })
  await test('actual template retires old image sources on card identity change', async () => {
    const r = await renderedFixture(); try {
      const old = r.picture().querySelector('img')
      assert.equal(old.src, '/A.webp')
      r.f.props.cardId = 'B'; await flush()
      assert.notEqual(r.picture().querySelector('img'), old)
      const retired = r.removed.find(entry => entry.node === old)
      assert(retired); assert.equal(retired.src, undefined); assert.equal(retired.srcset, undefined)
      assert.equal(r.picture().querySelector('img').src, '/B.webp')
    } finally { r.dispose() }
  })
  await test('actual template retires AVIF source and image srcset without an empty src assignment', async () => {
    const r = await renderedFixture(); try {
      r.f.props.intent = 'detail'; await flush()
      r.f.state.resolved.value.sources[0].avif = '/A.avif'; r.f.state.highRequested.value = true; await flush()
      const oldSource = r.picture().querySelector('source'), oldImage = r.picture().querySelector('img')
      assert.equal(oldSource.attributes.get('srcset'), '/A.avif')
      oldImage.attributes.set('srcset', '/A.webp 1x')
      r.f.props.cardId = 'B'; await flush()
      for (const node of [oldSource, oldImage]) {
        const retired = r.removed.find(entry => entry.node === node)
        assert(retired); assert.equal(retired.src, undefined); assert.equal(retired.srcset, undefined)
      }
    } finally { r.dispose() }
  })
  await test('actual template clears retiring source attributes on component unmount', async () => {
    const r = await renderedFixture(), old = r.picture().querySelector('img')
    r.dispose()
    assert.equal(old.attributes.has('src'), false); assert.equal(old.attributes.has('srcset'), false)
    assert.equal(r.root.children.length, 0)
  })
  await test('fit and decoded high-resolution updates do not retire or clear the current node', async () => {
    const r = await renderedFixture(); try {
      const old = r.picture().querySelector('img')
      r.f.props.fit = 'cover'; await flush()
      assert.equal(r.picture().querySelector('img'), old); assert.equal(old.src, '/A.webp')
      r.f.props.intent = 'detail'; await flush()
      const detail = r.picture().querySelector('img'), retiredCount = r.removed.length
      r.f.state.resolved.value.sources[0].webp = '/A-high.webp'; r.f.state.highRequested.value = true; await flush()
      assert.equal(r.picture().querySelector('img'), detail); assert.equal(detail.src, '/A-high.webp')
      assert.equal(r.removed.length, retiredCount)
    } finally { r.dispose() }
  })
  const archiveScript = archiveSource.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
  const archiveAst = ts.createSourceFile('CardArchive.ts', archiveScript, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const functions = archiveAst.statements.filter(node => ts.isFunctionDeclaration(node)
    && ['projectedUploadedArt', 'withRegularGalleryMetadata', 'withGalleryMetadata', 'archiveListImageUrl', 'alternateArtArchiveName'].includes(node.name?.text)).map(node => node.getText(archiveAst)).join('\n')
  for (const kind of ['alternate-art', 'gallery-card', 'regular-card', 'built-in']) await test('list thumbnail and unchanged detail original/' + kind, () => {
    const original = '/api/site/media/' + 'a'.repeat(32) + '/desktop/' + 'b'.repeat(64) + '.webp'
    const thumb = '/api/site/media/' + 'a'.repeat(32) + '/thumbnail/' + 'c'.repeat(64) + '.webp'
    const base = { id: 'BASE', nameZh: 'base', imageUrl: '/base.webp', products: [] }
    const art = { id: 'uploaded', kind, displayName: 'alternate', baseCardId: 'BASE', cardImageId: 'BASE', artCode: 'BASE',
      imageUrl: original, thumbnailUrl: thumb, builtIn: kind === 'built-in' }
    const env = { regularDisplayCards: { value: [base] }, regularGalleryCards: { value: [art] }, galleryArts: { value: [art] } }
    vm.createContext(env); vm.runInContext(compile(functions), env)
    assert.equal(typeof env.archiveListImageUrl, 'function')
    const card = kind === 'regular-card' ? env.withRegularGalleryMetadata(base) : kind === 'built-in' ? env.withGalleryMetadata(base) : env.projectedUploadedArt(art, true)
    assert.equal(card.imageUrl, original); assert.equal(env.archiveListImageUrl(card), thumb)
    assert.equal(env.archiveListImageUrl({ ...card, thumbnailUrl: undefined }), original)
    assert.equal(env.archiveListImageUrl({ ...card, thumbnailUrl: 'https://untrusted.invalid/track.webp' }), original)
    assert.equal(env.archiveListImageUrl({ ...card, thumbnailUrl: '/api/site/media/not-canonical' }), original)
  })
  await test('both archive lists select thumbnails but shared detail keeps original', () => {
    assert(archiveSource.includes(':legacy-url="archiveListImageUrl(displayedVersion(entry))"'))
    assert(archiveSource.includes(':legacy-url="archiveListImageUrl(card)"'))
    assert(read('CardDetailContent.vue').includes(':legacy-url="card.imageUrl"'))
  })
  await test('actual image and archive SFC scripts and templates compile without emitting files', () => {
    const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
    for (const [filename, content] of [['CardImage.vue', imageSource], ['CardArchive.vue', archiveSource]]) {
      const parsed = parse(content, { filename }); assert.deepEqual(parsed.errors, [])
      const script = compileScript(parsed.descriptor, { id: 'image-recovery-contract' })
      const template = compileTemplate({ source: parsed.descriptor.template.content, filename, id: 'image-recovery-contract',
        compilerOptions: { bindingMetadata: script.bindings } })
      assert.deepEqual(template.errors, [])
    }
  })
  await test('shared portrait and landscape image layers have one overridable corner outlet', () => {
    const css = imageSource.match(/<style[^>]*>([\s\S]*?)<\/style>/)[1]
    for (const selector of ['.l12-card-image', '.l12-card-image__img', '.l12-card-image__resolving']) {
      const rule = css.slice(css.indexOf(selector + '{')).split('}')[0]
      assert(rule.includes('border-radius:var(--l12-card-image-radius,var(--l12-card-image-default-radius))'), selector)
      if (selector !== '.l12-card-image') {
        assert(rule.includes('clip-path:inset(0 round var(--l12-card-image-radius,var(--l12-card-image-default-radius)))'), selector)
      }
    }
    assert(css.includes('--l12-card-image-default-radius:4% / 2.86%'))
    assert(css.includes('--l12-card-image-default-radius:4% / 6.4%'))
    assert(read('RoundCardImage.vue').includes('border-radius:50%'))
    const cover = read('DeckCover.vue')
    assert(cover.includes('<CardImage')); assert(cover.includes('.l12-card-image__img){overflow:visible'))
  })
  return cases
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const cases = await runCardImageRecoveryTests()
  console.log(JSON.stringify({ suite: 'actual card image recovery and archive thumbnail', total: cases.length,
    passed: cases.filter(test => test.passed).length, failed: cases.filter(test => !test.passed).length, cases }, null, 2))
  if (cases.some(test => !test.passed)) process.exitCode = 1
}
