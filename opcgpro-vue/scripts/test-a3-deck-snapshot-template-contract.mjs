import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import {
  deckSnapshotGroups,
  deckSnapshotPredicateNames,
  deckSnapshotTemplateContract,
} from './deck-snapshot-template-contract.mjs'

const read = name => readFileSync(new URL(`../src/l12/site/${name}`, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const baselineSources = {
  adminMatches: read('AdminMatchesPanel.vue'),
  adminMasterAnalytics: read('AdminMasterAnalyticsPanel.vue'),
  deckSnapshotViewer: read('DeckSnapshotViewer.vue'),
  deckConstructionBrowser: read('DeckConstructionBrowser.vue'),
}

const replaceOnce = (source, before, after) => {
  const index = source.indexOf(before)
  assert.notEqual(index, -1, `fixture marker missing: ${before.slice(0, 100)}`)
  return source.slice(0, index) + after + source.slice(index + before.length)
}
const changed = (key, before, after, sources = baselineSources) => ({
  ...sources,
  [key]: replaceOnce(sources[key], before, after),
})
const wrapSelfClosing = (source, marker) => {
  const start = source.indexOf(marker)
  assert.notEqual(start, -1, `self-closing marker missing: ${marker}`)
  const end = source.indexOf('/>', start)
  assert.notEqual(end, -1, `self-closing end missing: ${marker}`)
  const element = source.slice(start, end + 2)
  return source.slice(0, start) + `<template>${element}</template>` + source.slice(end + 2)
}

let cases = 0
const accept = (name, sources) => {
  const contract = deckSnapshotTemplateContract(sources)
  assert.deepEqual(Object.entries(contract).filter(([, passed]) => !passed), [], name)
  cases++
}
const reject = (name, predicate, sources) => {
  assert.equal(deckSnapshotTemplateContract(sources)[predicate], false, `${name}: ${predicate} must fail closed`)
  cases++
}

const baseline = deckSnapshotTemplateContract(baselineSources)
assert.equal(deckSnapshotPredicateNames.length, 12)
assert.deepEqual(deckSnapshotGroups(baseline).map(([, predicates]) => predicates.length), [2, 2, 5, 3])
accept('real immutable snapshot chain', baselineSources)

const reorderedClasses = changed('adminMatches', 'class="participant-card"', 'class="selected participant-card archive-row"')
accept('static class order and extra state classes', reorderedClasses)

let spaced = changed('adminMatches', 'v-if="deckViewer"', 'v-if=" ( deckViewer ) "')
spaced = changed('adminMasterAnalytics', 'v-if="popularDeck && selected"', 'v-if=" ( popularDeck && selected ) "', spaced)
spaced = changed('deckSnapshotViewer', ':entries="entries"', ':entries=" ( entries ) "', spaced)
accept('expression whitespace and parentheses', spaced)

let kebab = changed('adminMatches', '<DeckSnapshotViewer ', '<deck-snapshot-viewer ')
kebab = changed('adminMasterAnalytics', '<DeckSnapshotViewer ', '<deck-snapshot-viewer ', kebab)
kebab = changed('deckSnapshotViewer', '<DeckConstructionBrowser ', '<deck-construction-browser ', kebab)
kebab = changed('deckConstructionBrowser', '<CatalogCardDetails ', '<catalog-card-details ', kebab)
accept('four explicit Vue kebab component spellings', kebab)

const transparent = {
  ...baselineSources,
  adminMatches: wrapSelfClosing(baselineSources.adminMatches, '    <DeckSnapshotViewer '),
  adminMasterAnalytics: wrapSelfClosing(baselineSources.adminMasterAnalytics, '    <DeckSnapshotViewer '),
  deckSnapshotViewer: wrapSelfClosing(baselineSources.deckSnapshotViewer, '        <DeckConstructionBrowser '),
}
accept('unconditional component templates are transparent', transparent)

const arrowCallable = changed('deckSnapshotViewer', 'async function copyCode() {', 'const copyCode = async () => {')
accept('async arrow action declaration is equivalent', arrowCallable)

const equivalentEventModifiers = changed('deckSnapshotViewer', '@click="copyCode">复制牌库码',
  '@click.stop.prevent="copyCode">复制牌库码')
accept('stop and prevent click modifiers preserve the action contract', equivalentEventModifiers)

reject('launcher wrong condition', 'archiveLauncher', changed('adminMatches',
  'v-if="participant.deckCards?.length"', 'v-if="participant.deckCards?.length && detail"'))
reject('launcher wrong click target', 'archiveLauncher', changed('adminMatches',
  '@click="deckViewer = participant"', '@click="deckViewer = null"'))
reject('launcher dynamic bind override', 'archiveLauncher', changed('adminMatches',
  'class="view-construction"', 'class="view-construction" v-bind="buttonProps"'))
reject('launcher hidden participant ancestor', 'archiveLauncher', changed('adminMatches',
  'v-for="participant in detail.participants"', 'v-for="participant in detail.participants" v-show="false"'))
reject('launcher hidden participant grid ancestor', 'archiveLauncher', changed('adminMatches',
  '<section class="participant-grid">', '<section v-show="false" class="participant-grid">'))
reject('launcher native-hidden participant grid ancestor', 'archiveLauncher', changed('adminMatches',
  '<section class="participant-grid">', '<section hidden class="participant-grid">'))
reject('launcher unknown participant grid branch', 'archiveLauncher', changed('adminMatches',
  '<section class="participant-grid">', '<section v-if="report" class="participant-grid">'))
reject('launcher hidden archive host', 'archiveLauncher', changed('adminMatches',
  '<section class="match-admin">', '<section hidden class="match-admin">'))
reject('launcher once modifier changes repeat behavior', 'archiveLauncher', changed('adminMatches',
  '@click="deckViewer = participant"', '@click.once="deckViewer = participant"'))

const inlineCards = changed('adminMatches', '</button>\n              <p v-else class="privacy-note">',
  '</button><template v-for="(copy,index) in (participant.deckCards || [])"><CardImage :card-id="copy.cardId"/></template>\n              <p v-else class="privacy-note">')
reject('aliased inline participant deck loop', 'noInlineArchiveCards', inlineCards)
reject('inline participant deck loop outside the card', 'noInlineArchiveCards', changed('adminMatches',
  '</section>\n\n          <section class="fact-timeline">',
  '</section><div v-for="snapshot in participant.deckCards">{{ snapshot.cardId }}</div>\n\n          <section class="fact-timeline">'))
reject('inline participant deck loop using of syntax', 'noInlineArchiveCards', changed('adminMatches',
  '</section>\n\n          <section class="fact-timeline">',
  '</section><div v-for="snapshot of participant.deckCards">{{ snapshot.cardId }}</div>\n\n          <section class="fact-timeline">'))
reject('inline participant deck loop using static bracket access', 'noInlineArchiveCards', changed('adminMatches',
  '</section>\n\n          <section class="fact-timeline">',
  '</section><div v-for="snapshot in participant[\'deckCards\']">{{ snapshot.cardId }}</div>\n\n          <section class="fact-timeline">'))

const commentedLoop = changed('adminMatches', '</button>\n              <p v-else class="privacy-note">',
  '</button><!-- <i v-for="card in participant.deckCards">{{ card.cardId }}</i> -->\n              <p v-else class="privacy-note">')
accept('commented inline loop is not rendered', commentedLoop)

reject('archive consumer wrong import', 'archiveViewerConsumer', changed('adminMatches',
  "import DeckSnapshotViewer from './DeckSnapshotViewer.vue'", "import DeckSnapshotViewer from './FakeViewer.vue'"))
reject('archive consumer wrong entries', 'archiveViewerConsumer', changed('adminMatches',
  ':entries="deckViewer.deckCards"', ':entries="detail.participants[0].deckCards"'))
reject('archive consumer duplicate static entries', 'archiveViewerConsumer', changed('adminMatches',
  ':entries="deckViewer.deckCards"', 'entries="wrong" :entries="deckViewer.deckCards"'))
reject('archive consumer hidden node', 'archiveViewerConsumer', changed('adminMatches',
  'v-if="deckViewer" :entries=', 'v-if="deckViewer" v-show="false" :entries='))
reject('archive consumer object bind override', 'archiveViewerConsumer', changed('adminMatches',
  '<DeckSnapshotViewer v-if="deckViewer"', '<DeckSnapshotViewer v-bind="viewerProps" v-if="deckViewer"'))

reject('analytics consumer concatenated condition', 'analyticsViewerConsumer', changed('adminMasterAnalytics',
  'v-if="popularDeck && selected"', 'v-if="popularDeck && selected && report"'))
reject('analytics consumer wrong notice event', 'analyticsViewerConsumer', changed('adminMasterAnalytics',
  '@notice="emit(\'notice\', $event)"', '@notice="notice = $event"'))

const fakeViewerNode = changed('adminMatches', '    <DeckSnapshotViewer v-if="deckViewer"',
  '    <!-- <DeckSnapshotViewer v-if="deckViewer"')
reject('comment cannot supply archive viewer', 'archiveViewerConsumer', changed('adminMatches', '/>\n  </section>\n</template>', '/> -->\n  </section>\n</template>', fakeViewerNode))

reject('viewer wrong Teleport host', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  '<Teleport to="body">', '<Teleport to="#app">'))
reject('viewer hidden overlay ancestor', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  '<div class="deck-snapshot-viewer"', '<div v-show="false" class="deck-snapshot-viewer"'))
reject('viewer style-hidden overlay ancestor', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  '<div class="deck-snapshot-viewer"', '<div style="display:none" class="deck-snapshot-viewer"'))
reject('viewer dynamic-style overlay ancestor', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  '<div class="deck-snapshot-viewer"', '<div :style="overlayStyle" class="deck-snapshot-viewer"'))
reject('viewer browser wrong entries', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  ':entries="entries"', ':entries="catalog"'))
reject('viewer browser dynamic binding', 'viewerBrowserComposition', changed('deckSnapshotViewer',
  '<DeckConstructionBrowser ', '<DeckConstructionBrowser v-bind="browserProps" '))

reject('copy action wrong handler', 'viewerCopyCodeAction', changed('deckSnapshotViewer',
  '@click="copyCode">复制牌库码', '@click="exportImage">复制牌库码'))
reject('copy action right-click modifier changes activation', 'viewerCopyCodeAction', changed('deckSnapshotViewer',
  '@click="copyCode">复制牌库码', '@click.right="copyCode">复制牌库码'))
reject('copy action middle-click modifier changes activation', 'viewerCopyCodeAction', changed('deckSnapshotViewer',
  '@click="copyCode">复制牌库码', '@click.middle="copyCode">复制牌库码'))
reject('copy action loses deck lock', 'viewerCopyCodeAction', changed('deckSnapshotViewer',
  ':disabled="!deck" @click="copyCode"', ':disabled="false" @click="copyCode"'))
reject('export action wrong text', 'viewerExportImageAction', changed('deckSnapshotViewer',
  '@click="exportImage">导出牌库图', '@click="exportImage">导出图片'))
reject('library action hidden', 'viewerCopyToLibraryAction', changed('deckSnapshotViewer',
  'class="primary" :disabled="!deck"', 'v-show="false" class="primary" :disabled="!deck"'))
reject('callable name only in comment', 'viewerCopyCodeAction', changed('deckSnapshotViewer',
  'async function copyCode() {', '// async function copyCode() {\nasync function copyCodeMissing() {'))
reject('special section omitted from reconstructed deck', 'viewerSpecialExpansion', changed('deckSnapshotViewer',
  "specialIds: expand('special')", "specialIds: expand('extra')"))

reject('teleported filter wrong accessible name', 'browserFilterSemantics', changed('deckConstructionBrowser',
  'class="construction-filter-rail" aria-label="构筑筛选"', 'class="construction-filter-rail" aria-label="卡牌筛选"'))
reject('local filter hidden', 'browserFilterSemantics', changed('deckConstructionBrowser',
  '<nav v-else aria-label="构筑筛选">', '<nav v-else v-show="false" aria-label="构筑筛选">'))
reject('quantity reads the wrong value', 'browserQuantityRendering', changed('deckConstructionBrowser',
  '×{{ entry.quantity }}', '×{{ visible.length }}'))
reject('quantity text overridden', 'browserQuantityRendering', changed('deckConstructionBrowser',
  '<strong>×{{ entry.quantity }}</strong>', '<strong v-text="entry.cardId">×{{ entry.quantity }}</strong>'))
reject('card selection handler uses display index', 'browserQuantityRendering', changed('deckConstructionBrowser',
  '@click="selectCard(entry.cardId)"', '@click="selectCard(String(index))"'))
reject('selected computed uses the wrong identity', 'browserSelectedComputed', changed('deckConstructionBrowser',
  'computed(() => byId.value.get(selectedId.value))', 'computed(() => byId.value.get(visible.value[0]?.cardId))'))
reject('details component uses the wrong branch', 'browserSelectedComputed', changed('deckConstructionBrowser',
  'v-if="selected && !externalDetails"', 'v-if="selected"'))

const fakeSources = {
  adminMatches: `<script setup>const fake = '<DeckSnapshotViewer v-if="deckViewer"/>'</script><template><!-- <button data-ui-contract="match-snapshot-view-construction">查看构筑</button> --></template>`,
  adminMasterAnalytics: `<script setup>const fake = 'DeckSnapshotViewer'</script><template><script>const hidden = '<DeckSnapshotViewer/>'</script></template>`,
  deckSnapshotViewer: `<script setup>const copyCode=async()=>{};const exportImage=async()=>{};const copyToLibrary=async()=>{}</script><template><!-- <DeckConstructionBrowser :entries="entries"/> --></template>`,
  deckConstructionBrowser: `<script setup>const selected = computed(() => byId.value.get(selectedId.value))</script><template><style>.fake{content:'entry.quantity'}</style></template>`,
}
assert.deepEqual(Object.entries(deckSnapshotTemplateContract(fakeSources)).filter(([, passed]) => passed), [],
  'comments, scripts and styles cannot lend rendered snapshot nodes')
cases++

console.log(`A3 deck snapshot template contract passed: 12 migrated predicates, 4 groups, ${cases} focused cases`)
