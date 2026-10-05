import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import {
  publicDeckConsumerGroups,
  publicDeckConsumerPredicateNames,
  publicDeckConsumerTemplateContract,
} from './public-deck-consumer-template-contract.mjs'

const read = name => readFileSync(new URL(`../src/l12/site/${name}`, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const baselineSources = {
  publicDeckDetail: read('PublicDeckDetailPage.vue'),
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
  const contract = publicDeckConsumerTemplateContract(sources)
  assert.deepEqual(Object.entries(contract).filter(([, passed]) => !passed), [], name)
  cases++
}
const reject = (name, predicate, sources) => {
  assert.equal(publicDeckConsumerTemplateContract(sources)[predicate], false,
    `${name}: ${predicate} must fail closed`)
  cases++
}

const baseline = publicDeckConsumerTemplateContract(baselineSources)
assert.equal(publicDeckConsumerPredicateNames.length, 5)
assert.deepEqual(publicDeckConsumerGroups(baseline).map(([, predicates]) => predicates.length), [2, 2, 1])
accept('real public deck consumer ownership chain', baselineSources)

let reordered = changed('publicDeckDetail', 'class="deck-layout detail-anchor-section"',
  'class="selected detail-anchor-section deck-layout"')
reordered = changed('deckConstructionBrowser', 'class="construction-browser"',
  'class="state construction-browser shared"', reordered)
accept('static class order and extra state classes', reordered)

let spaced = changed('publicDeckDetail', ':entries="entries"', ':entries=" ( entries ) "')
spaced = changed('publicDeckDetail', 'v-if="entry.deck.cardIds.length"',
  'v-if=" ( entry.deck.cardIds.length ) "', spaced)
spaced = changed('deckConstructionBrowser', 'v-if="filterTarget && filterTargetReady"',
  'v-if=" ( filterTarget && filterTargetReady ) "', spaced)
accept('expression whitespace and parentheses', spaced)

let kebab = changed('publicDeckDetail', '<DeckConstructionBrowser ', '<deck-construction-browser ')
kebab = changed('publicDeckDetail', '<CardDetailContent ', '<card-detail-content ', kebab)
kebab = changed('publicDeckDetail', '<CatalogCardDetails ', '<catalog-card-details ', kebab)
kebab = changed('deckConstructionBrowser', '<CatalogCardDetails ', '<catalog-card-details ', kebab)
accept('explicit shared component kebab spellings', kebab)

const transparent = {
  ...baselineSources,
  publicDeckDetail: wrapSelfClosing(wrapSelfClosing(baselineSources.publicDeckDetail,
    '<DeckConstructionBrowser '), '<CatalogCardDetails '),
  deckConstructionBrowser: wrapSelfClosing(baselineSources.deckConstructionBrowser, '<CatalogCardDetails '),
}
accept('unconditional templates are transparent', transparent)

reject('hidden construction ancestor', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '<section id="public-deck-construction"', '<section v-show="false" id="public-deck-construction"'))
reject('native-hidden browser host', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '<div class="public-deck-main">', '<div hidden class="public-deck-main">'))
reject('style-hidden browser host', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '<div class="public-deck-main">', '<div style="display:none" class="public-deck-main">'))
reject('unknown browser host branch', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '<div class="public-deck-main">', '<div v-if="entry.official" class="public-deck-main">'))
reject('wrong construction host', 'detailSummaryHost', changed('publicDeckDetail',
  'id="public-deck-construction"', 'id="public-deck-overview"'))
reject('browser wrong import', 'sharedBrowserConsumer', changed('publicDeckDetail',
  "from './DeckConstructionBrowser.vue'", "from './FakeConstructionBrowser.vue'"))
reject('browser wrong entries', 'sharedBrowserConsumer', changed('publicDeckDetail',
  ':entries="entries"', ':entries="openingHand"'))
reject('browser object binding override', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '<DeckConstructionBrowser ', '<DeckConstructionBrowser v-bind="browserProps" '))
reject('browser duplicate static entries', 'sharedBrowserConsumer', changed('publicDeckDetail',
  ':entries="entries"', 'entries="wrong" :entries="entries"'))
reject('browser wrong selection event', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '@select="selectCard"', '@select="selectedCard = null"'))
reject('browser once selection event', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '@select="selectCard"', '@select.once="selectCard"'))
reject('browser loses external ownership flag', 'sharedBrowserConsumer', changed('publicDeckDetail',
  'hide-header external-details', 'hide-header'))
reject('second shared browser cannot own competing details', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '@select="selectCard"/>', '@select="selectCard"/><DeckConstructionBrowser :entries="entries" :catalog="catalog"/>'))

reject('filter anchor wrong target id', 'filterBridge', changed('publicDeckDetail',
  'id="public-deck-construction-filters"', 'id="public-deck-filter-tray"'))
reject('duplicate teleport destination outside summary', 'filterBridge', changed('publicDeckDetail',
  '<div class="public-deck-main">', '<div id="public-deck-construction-filters"></div><div class="public-deck-main">'))
reject('filter component wrong target', 'filterBridge', changed('publicDeckDetail',
  'filter-target="#public-deck-construction-filters"', 'filter-target="#wrong-target"'))
reject('filter anchor hidden', 'filterBridge', changed('publicDeckDetail',
  '<section id="public-deck-construction-filters"', '<section v-show="false" id="public-deck-construction-filters"'))
reject('filter anchor gains competing content', 'filterBridge', changed('publicDeckDetail',
  'aria-label="构筑筛选"></section>', 'aria-label="构筑筛选"><span>旧筛选</span></section>'))
reject('filter Teleport wrong condition', 'filterBridge', changed('deckConstructionBrowser',
  'v-if="filterTarget && filterTargetReady"', 'v-if="filterTarget"'))
reject('filter Teleport wrong destination', 'filterBridge', changed('deckConstructionBrowser',
  ':to="filterTarget"', ':to="\'#app\'"'))
reject('teleported filter hidden', 'filterBridge', changed('deckConstructionBrowser',
  'class="construction-filter-rail"', 'v-show="false" class="construction-filter-rail"'))
reject('local fallback loses else branch', 'filterBridge', changed('deckConstructionBrowser',
  '<nav v-else aria-label="构筑筛选">', '<nav v-if="!filterTarget" aria-label="构筑筛选">'))

reject('summary title moved to wrong wording', 'detailSummaryHost', changed('publicDeckDetail',
  '<b>构筑摘要</b>', '<b>牌库统计</b>'))
reject('summary wrong main condition', 'summaryRows', changed('publicDeckDetail',
  'v-if="entry.deck.cardIds.length"', 'v-if="entries.length"'))
reject('summary wrong special value', 'summaryRows', changed('publicDeckDetail',
  '<strong>{{ entry.deck.specialIds.length }}</strong>', '<strong>{{ entry.deck.cardIds.length }}</strong>'))
reject('summary automatic row relabeled as trial', 'summaryRows', changed('publicDeckDetail',
  '>自动额外<strong>', '>试炼/额外<strong>'))
reject('summary value overridden by v-text', 'summaryRows', changed('publicDeckDetail',
  '<strong>{{ entry.deck.moraleIds.length }}</strong>',
  '<strong v-text="entry.deck.cardIds.length">{{ entry.deck.moraleIds.length }}</strong>'))

reject('desktop detail wrong card', 'uniqueDetailOwnership', changed('publicDeckDetail',
  '<CardDetailContent v-if="selectedCard" :card="selectedCard"',
  '<CardDetailContent v-if="selectedCard" :card="master"'))
reject('desktop detail hidden owner', 'uniqueDetailOwnership', changed('publicDeckDetail',
  '<aside class="archive-detail public-card-detail"',
  '<aside v-show="false" class="archive-detail public-card-detail"'))
reject('mobile detail wrong branch', 'uniqueDetailOwnership', changed('publicDeckDetail',
  'v-if="mobileDetailsOpen && selectedCard"', 'v-if="selectedCard"'))
reject('mobile detail wrong close event', 'uniqueDetailOwnership', changed('publicDeckDetail',
  '@close="mobileDetailsOpen = false"', '@close="selectedCard = null"'))
reject('browser internal detail ignores external owner', 'uniqueDetailOwnership', changed('deckConstructionBrowser',
  'v-if="selected && !externalDetails"', 'v-if="selected"'))
reject('duplicate mobile detail owner', 'uniqueDetailOwnership', changed('publicDeckDetail',
  '<CatalogCardDetails v-if="mobileDetailsOpen && selectedCard"',
  '<CatalogCardDetails v-if="mobileDetailsOpen && selectedCard" :card="selectedCard" :show-catalog-only="false" @close="mobileDetailsOpen = false"/><CatalogCardDetails v-if="mobileDetailsOpen && selectedCard"'))

const commentedBrowser = changed('publicDeckDetail', '<DeckConstructionBrowser ', '<!-- <DeckConstructionBrowser ')
reject('comment cannot supply shared browser', 'sharedBrowserConsumer', changed('publicDeckDetail',
  '@select="selectCard"/>', '@select="selectCard"/> -->', commentedBrowser))

const fakeSources = {
  publicDeckDetail: `<script setup>const fake = '<DeckConstructionBrowser external-details/>'</script><template><!-- <section id="public-deck-construction-filters"/> --><script>const hidden = '构筑摘要'</script></template>`,
  deckConstructionBrowser: `<script setup>const fake = '<CatalogCardDetails/>'</script><template><style>.fake{content:'construction-filter-rail'}</style></template>`,
}
assert.deepEqual(Object.entries(publicDeckConsumerTemplateContract(fakeSources)).filter(([, passed]) => passed), [],
  'comments scripts and styles cannot lend public deck consumer nodes')
cases++

console.log(`A3 public deck consumer template contract passed: 5 legacy contract groups, 3 AST groups, ${cases} focused cases`)
