import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import {
  profileUsesAggregatedStatisticsWithoutReplayHistory,
  replayEntryGroups,
  replayEntryPredicateNames,
  replayEntryTemplateContract,
} from './replay-entry-template-contract.mjs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const baselineSources = {
  matchRecords: read('../src/l12/MatchRecords.vue'),
  replayPage: read('../src/l12/ReplayPage.vue'),
  gameBoard: read('../src/l12/game/GameBoard.vue'),
  profilePage: read('../src/l12/site/ProfilePage.vue'),
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
  const contract = replayEntryTemplateContract(sources)
  assert.deepEqual(Object.entries(contract).filter(([, passed]) => !passed), [], name)
  cases++
}
const reject = (name, predicate, sources) => {
  assert.equal(replayEntryTemplateContract(sources)[predicate], false,
    `${name}: ${predicate} must fail closed`)
  cases++
}

const baseline = replayEntryTemplateContract(baselineSources)
assert.equal(replayEntryPredicateNames.length, 9)
assert.deepEqual(replayEntryGroups(baseline).map(([, predicates]) => predicates.length), [3, 3, 3])
accept('real replay entry retention and mobile boundary', baselineSources)

let reordered = changed('matchRecords', 'class="match-records grand-panel"',
  'class="state grand-panel match-records"')
reordered = changed('replayPage', 'class="game-page replay-page"',
  'class="replay-page active game-page"', reordered)
accept('static class order and extra state classes', reordered)

let spaced = changed('matchRecords', ':disabled="!canUseSelectedReplay"',
  ':disabled=" ( !canUseSelectedReplay ) "')
spaced = changed('replayPage', ':replay-playback-speed="playbackSpeed"',
  ':replay-playback-speed=" ( playbackSpeed ) "', spaced)
spaced = changed('gameBoard', 'v-if="mobileLandscapeViewport && mobileInspectorOpen"',
  'v-if=" ( mobileLandscapeViewport && mobileInspectorOpen ) "', spaced)
accept('expression whitespace and parentheses', spaced)

let kebab = changed('replayPage', '<GameBoard ', '<game-board ')
kebab = changed('gameBoard', '<CardDetailContent :card="focusDetailCard"',
  '<card-detail-content :card="focusDetailCard"', kebab)
accept('explicit component kebab spellings', kebab)

accept('mobile viewport named import order is irrelevant', changed('replayPage',
  'import { isMobileDeviceExperience, landscapeTeleportTarget } from \'./mobileViewport\'',
  'import { landscapeTeleportTarget, isMobileDeviceExperience } from \'./mobileViewport\''))

const transparent = {
  ...baselineSources,
  replayPage: wrapSelfClosing(baselineSources.replayPage, '    <GameBoard '),
  gameBoard: wrapSelfClosing(baselineSources.gameBoard, '              <CardDetailContent :card="focusDetailCard"'),
}
accept('unconditional templates are transparent', transparent)

let transparentReplayRoot = changed('replayPage', '<template>\n  <div class="game-page replay-page">',
  '<template>\n  <template>\n  <div class="game-page replay-page">')
transparentReplayRoot = changed('replayPage', '  </div>\n</template>\n\n<style scoped>',
  '  </div>\n  </template>\n</template>\n\n<style scoped>', transparentReplayRoot)
accept('unconditional template ancestor keeps replay root reachable', transparentReplayRoot)

assert.equal(profileUsesAggregatedStatisticsWithoutReplayHistory(baselineSources.profilePage), true,
  'real profile must use aggregated statistics without a replay list request')
cases++
assert.equal(profileUsesAggregatedStatisticsWithoutReplayHistory(replaceOnce(baselineSources.profilePage,
  "const publicHistory =", "const duplicateReplayList = platformRequest('/api/matches?limit=10')\nconst publicHistory =")), false,
  'profile must not recreate the retained replay list through a script string')
cases++

reject('records header hidden', 'recordsHeaderAndImport', changed('matchRecords',
  '<header class="records-header">', '<header v-show="false" class="records-header">'))
reject('records title wrong node text', 'recordsHeaderAndImport', changed('matchRecords',
  '<h1>对局回放</h1>', '<h1>录像中心</h1><span>对局回放</span>'))
reject('retention note wrong wording', 'recordsHeaderAndImport', changed('matchRecords',
  '仅保存7天内最近10场回放，历史回放文件可能随版本更新失效。',
  '保存最近10场回放。'))
reject('import button wrong handler', 'recordsHeaderAndImport', changed('matchRecords',
  '@click="openReplayImport">打开 JSON 回放', '@click="loadMatches">打开 JSON 回放'))
reject('file input wrong change handler', 'recordsHeaderAndImport', changed('matchRecords',
  '@change="importReplay"', '@change="loadMatches"'))
reject('duplicate JSON import entry', 'recordsHeaderAndImport', changed('matchRecords',
  '<button @click="openReplayImport">打开 JSON 回放</button>',
  '<button @click="openReplayImport">打开 JSON 回放</button><button @click="openReplayImport">打开 JSON 回放</button>'))
reject('export loses unavailable disabled state', 'recordsReplayAvailability', changed('matchRecords',
  '<button :disabled="!canUseSelectedReplay" @click="exportReplay">', '<button @click="exportReplay">'))
reject('playback loses unavailable disabled state', 'recordsReplayAvailability', changed('matchRecords',
  '<button class="primary" :disabled="!canUseSelectedReplay"', '<button class="primary"'))
reject('disabled state borrowed by refresh button', 'recordsReplayAvailability', changed('matchRecords',
  '<button :disabled="!canUseSelectedReplay" @click="exportReplay">保存 JSON</button>\n        <button @click="loadMatches">',
  '<button @click="exportReplay">保存 JSON</button>\n        <button :disabled="!canUseSelectedReplay" @click="loadMatches">'))
reject('cleared payload wrong condition', 'recordsReplayAvailability', changed('matchRecords',
  'v-if="selected.commandCount === 0"', 'v-if="selected.commandCount < 0"'))
reject('mobile notice becomes blocking dialog', 'recordsMobileInlineBlock', changed('matchRecords',
  '<p v-else-if="mobileReplayBlocked" class="mobile-replay-inline"',
  '<p v-else-if="mobileReplayBlocked" role="alertdialog" class="mobile-replay-inline"'))
reject('mobile notice wrong branch', 'recordsMobileInlineBlock', changed('matchRecords',
  'v-else-if="mobileReplayBlocked" class="mobile-replay-inline"',
  'v-else-if="!mobileReplayBlocked" class="mobile-replay-inline"'))

reject('route blocker wrong condition', 'replayMobileBlocker', changed('replayPage',
  'v-if="mobileReplayBlocked" class="replay-mobile-blocked"',
  'v-if="loading" class="replay-mobile-blocked"'))
reject('route blocker hidden ancestor', 'replayMobileBlocker', changed('replayPage',
  '<div class="game-page replay-page">', '<div hidden class="game-page replay-page">'))
reject('route blocker wrong handler', 'replayMobileBlocker', changed('replayPage',
  '@click="returnFromReplay">{{ returnLabel }}', '@click="loadReplay">{{ returnLabel }}'))
reject('route blocker text borrowed by sibling', 'replayMobileBlocker', changed('replayPage',
  '<p>请到电脑端查看回放</p>', '<p>移动端不可播放</p><span>请到电脑端查看回放</span>'))
let hiddenReplayRoot = changed('replayPage', '<template>\n  <div class="game-page replay-page">',
  '<template>\n  <template v-if="false">\n  <div class="game-page replay-page">')
hiddenReplayRoot = changed('replayPage', '  </div>\n</template>\n\n<style scoped>',
  '  </div>\n  </template>\n</template>\n\n<style scoped>', hiddenReplayRoot)
reject('replay root hidden by conditional template ancestor', 'replayMobileBlocker', hiddenReplayRoot)
reject('route blocker text hidden in conditional descendant', 'replayMobileBlocker', changed('replayPage',
  '<p>请到电脑端查看回放</p>', '<p><span v-if="false">请到电脑端查看回放</span></p>'))
reject('return label hidden in conditional descendant', 'replayMobileBlocker', changed('replayPage',
  '<button @click="returnFromReplay">{{ returnLabel }}</button>',
  '<button @click="returnFromReplay"><span v-if="false">{{ returnLabel }}</span></button>'))

reject('replay board wrong game model', 'replayBoardBinding', changed('replayPage',
  ':game="currentGame"', ':game="detail"'))
reject('replay board loses read-only flag', 'replayBoardBinding', changed('replayPage',
  ':reveal-both-hands="isAdminReplay" read-only', ':reveal-both-hands="isAdminReplay"'))
reject('replay board wrong presentation handler', 'replayBoardBinding', changed('replayPage',
  '@replay-presentation-change="replayPresentationBusy = $event"',
  '@replay-presentation-change="playing = $event"'))
reject('replay board object binding override', 'replayBoardBinding', changed('replayPage',
  '<GameBoard v-else-if="currentGame"', '<GameBoard v-bind="boardProps" v-else-if="currentGame"'))
reject('duplicate replay board entry', 'replayBoardBinding', changed('replayPage',
  '<GameBoard v-else-if="currentGame"', '<GameBoard v-else-if="currentGame" :game="currentGame" read-only/><GameBoard v-else-if="currentGame"'))

reject('control Teleport wrong target', 'replayControlTeleport', changed('replayPage',
  ':to="landscapeTeleportTarget()"', ':to="\'#app\'"'))
reject('control Teleport wrong condition', 'replayControlTeleport', changed('replayPage',
  'v-if="!mobileReplayBlocked" :to=', 'v-if="currentGame" :to='))
reject('control Teleport cannot borrow same-named local functions', 'replayControlTeleport', changed('replayPage',
  "import { isMobileDeviceExperience, landscapeTeleportTarget } from './mobileViewport'",
  "function isMobileDeviceExperience() { return false }\nfunction landscapeTeleportTarget() { return '#app' }"))
reject('control Teleport rejects mobile helpers from wrong module', 'replayControlTeleport', changed('replayPage',
  "from './mobileViewport'", "from './mobileViewport-copy'"))
reject('controls hidden node', 'replayControlTeleport', changed('replayPage',
  'v-if="currentGame" class="replay-controls"',
  'v-if="currentGame" v-show="false" class="replay-controls"'))
reject('controls class borrowed by sibling', 'replayControlTeleport', changed('replayPage',
  'class="replay-controls" aria-label="回放控制"',
  'class="replay-controls-copy" aria-label="回放控制"><span class="replay-controls"></span'))

reject('inspector handle wrong handler', 'boardInspectorHandle', changed('gameBoard',
  '@click="mobileInspectorOpen = !mobileInspectorOpen"', '@click="mobileInspectorOpen = false"'))
reject('inspector handle wrong model', 'boardInspectorHandle', changed('gameBoard',
  ':aria-expanded="mobileInspectorOpen"', ':aria-expanded="mobileRecordOpen"'))
reject('inspector handle hidden host', 'boardInspectorHandle', changed('gameBoard',
  '<Teleport :to="landscapeTeleportTarget()">\n      <button v-if="mobileLandscapeViewport"',
  '<Teleport v-show="false" :to="landscapeTeleportTarget()">\n      <button v-if="mobileLandscapeViewport"'))
reject('duplicate inspector handle entry', 'boardInspectorHandle', changed('gameBoard',
  '<button v-if="mobileLandscapeViewport" type="button" class="mobile-card-inspector-handle mobile-card-inspector-handle-global"',
  '<button v-if="mobileLandscapeViewport" class="mobile-card-inspector-handle mobile-card-inspector-handle-global">借位</button><button v-if="mobileLandscapeViewport" type="button" class="mobile-card-inspector-handle mobile-card-inspector-handle-global"'))

reject('mobile detail wrong card model', 'boardMobileInspectorDetails', changed('gameBoard',
  '<div v-if="focusCard && focusDetailCard" class="archive-detail mobile-card-detail-body">\n              <CardDetailContent :card="focusDetailCard"',
  '<div v-if="focusCard && focusDetailCard" class="archive-detail mobile-card-detail-body">\n              <CardDetailContent :card="focusCard"'))
reject('mobile detail owner wrong condition', 'boardMobileInspectorDetails', changed('gameBoard',
  'v-if="focusCard && focusDetailCard" class="archive-detail mobile-card-detail-body"',
  'v-if="focusCard" class="archive-detail mobile-card-detail-body"'))
reject('mobile inspector hidden transition ancestor', 'boardMobileInspectorDetails', changed('gameBoard',
  '<Transition name="mobile-card-inspector">', '<Transition v-show="false" name="mobile-card-inspector">'))
reject('mobile detail content overridden', 'boardMobileInspectorDetails', changed('gameBoard',
  '<div v-if="focusCard && focusDetailCard" class="archive-detail mobile-card-detail-body">',
  '<div v-if="focusCard && focusDetailCard" v-html="detailHtml" class="archive-detail mobile-card-detail-body">'))

reject('opponent hand loses mobile layout model', 'boardMobileLayoutConsumers', changed('gameBoard',
  ':confirm-all-playable="mobileLandscapeViewport" :mobile-layout="mobileLandscapeViewport"',
  ':confirm-all-playable="mobileLandscapeViewport"'))
reject('prompt receives wrong mobile model', 'boardMobileLayoutConsumers', changed('gameBoard',
  ':inspector-visible="modalInspectorVisible" :mobile-layout="mobileLandscapeViewport"',
  ':inspector-visible="modalInspectorVisible" :mobile-layout="compactViewport"'))
reject('mobile layout binding borrowed by plain node', 'boardMobileLayoutConsumers', changed('gameBoard',
  ':inspector-visible="modalInspectorVisible" :mobile-layout="mobileLandscapeViewport"',
  ':inspector-visible="modalInspectorVisible"><span :mobile-layout="mobileLandscapeViewport"></span'))

const fakeSources = {
  matchRecords: `<script setup>const fake = '<button :disabled="!canUseSelectedReplay">播放回放</button>'</script><template><!-- <h1>对局回放</h1> --></template>`,
  replayPage: `<script setup>const fake = '<GameBoard read-only/>'</script><template><script>const hidden = 'replay-controls'</script></template>`,
  gameBoard: `<script setup>const fake = 'mobile-card-inspector-handle-global'</script><template><style>.fake{content:'CardDetailContent'}</style></template>`,
}
assert.deepEqual(Object.entries(replayEntryTemplateContract(fakeSources)).filter(([, passed]) => passed), [],
  'comments scripts and styles cannot lend replay entry nodes')
cases++

console.log(`A3 replay entry template contract passed: 9 migrated predicates, 3 groups, ${cases} focused cases`)
