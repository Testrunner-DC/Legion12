import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import ts from 'typescript'
import { DECK_COVER_REQUEST_HEADERS, deckCoverHeadersForOrigin, selectDeckCoverCardIds, validateDeckCoverCardIds, validateOptionalDeckCoverCardIds } from '../src/l12/deckCover.ts'

assert.deepEqual(DECK_COVER_REQUEST_HEADERS, { 'X-L12-Deck-Cover': '1' })
for (const [base, origin, supported] of [
  ['https://site.invalid', 'https://site.invalid', true],
  ['https://site.invalid/testrun', 'https://site.invalid', true],
  ['/testrun', 'https://site.invalid', true],
  ['http://localhost:5173', 'http://localhost:5173', true],
  ['https://site.invalid:444', 'https://site.invalid', false],
  ['http://site.invalid', 'https://site.invalid', false],
  ['https://site.invalid.other.invalid', 'https://site.invalid', false],
  ['https://api.invalid', 'https://site.invalid', false],
  ['blob:https://site.invalid/fixture', 'https://site.invalid', false],
  ['file:///fixture', 'null', false],
  ['not a URL', undefined, false],
]) assert.deepEqual(deckCoverHeadersForOrigin(base, origin), supported ? DECK_COVER_REQUEST_HEADERS : {},
  'unknown cross-origin service must remain readable without a new CORS header')

const card = (id, number, cardType, cost, troops) => ({
  id, number, cardType, cost, troops, nameZh: id, product: 'test', faction: 'universal',
})
const cards = [
  card('L-COST', 'L-99', 'legion', 4, 9000),
  card('L-POWER', 'L-1', 'legion', 5, 2000),
  card('L-10', 'L-10', 'legion', 5, 3000),
  card('L-12', 'L-12', 'legion', 5, 3000),
  card('T-2', 'T-2', 'tactic', 3),
  card('T-10', 'T-10', 'tactic', 3),
  card('A-NULL', 'A-100', 'artifact', undefined),
  card('A-9', 'A-9', 'artifact', 4),
  card('ALT-2', 'ALT', 'legion', 4, 2000),
  card('ALT-10', 'ALT', 'legion', 4, 2000),
  card('MASTER', 'M-99', 'master', 99),
]
const byId = new Map(cards.map(item => [item.id, item]))

assert.deepEqual(
  selectDeckCoverCardIds(['A-NULL', 'L-COST', 'T-2', 'L-POWER', 'T-10', 'L-10', 'A-9', 'L-12'], byId),
  ['L-12', 'T-10', 'A-9'],
  'complete covers select one per type by printed values and latest natural number',
)
assert.deepEqual(
  selectDeckCoverCardIds(['T-2', 'L-10', 'L-12', 'L-10'], byId),
  ['L-12', 'L-10', 'T-2'],
  'a missing artifact refills from legion and regroups into right-to-left type order',
)
assert.deepEqual(
  selectDeckCoverCardIds(['A-NULL', 'T-2', 'A-9', 'T-10'], byId),
  ['T-10', 'T-2', 'A-9'],
  'a missing legion refills from tactic before artifact',
)
assert.deepEqual(
  selectDeckCoverCardIds(['A-9', 'L-10', 'A-NULL', 'L-12'], byId),
  ['L-12', 'L-10', 'A-9'],
  'a missing tactic refills from legion before artifact',
)
assert.deepEqual(
  selectDeckCoverCardIds(['L-12', 'l-12', 'L-12'], byId),
  ['L-12'],
  'case variants stay distinct only once and fewer than three eligible cards are never fabricated',
)
assert.deepEqual(
  selectDeckCoverCardIds(['ALT-2', 'ALT-10'], byId),
  ['ALT-10', 'ALT-2'],
  'equal printed numbers fall back to the latest natural card ID',
)
assert.deepEqual(
  selectDeckCoverCardIds(['UNKNOWN', 'MASTER', 'T-2'], byId),
  ['T-2'],
  'unknown and ineligible card types never enter the cover',
)

for (const valid of [undefined, [], ['L-12'], ['L-12', 'T-10', 'A-9']]) {
  if (valid === undefined) continue
  assert.equal(validateDeckCoverCardIds(valid), true)
}
for (const invalid of [null, 'L-12', ['', 'T-10'], ['L-12', 'l-12'], ['1', '2', '3', '4'], [1]])
  assert.equal(validateDeckCoverCardIds(invalid), false)
assert.equal(validateDeckCoverCardIds(['UNKNOWN'], byId), false, 'catalog-aware validation rejects unknown cards')
assert.equal(validateDeckCoverCardIds(['MASTER'], byId), false, 'catalog-aware validation rejects ineligible types')
assert.equal(validateOptionalDeckCoverCardIds({}), true, 'older summaries without the additive field remain readable')
assert.equal(validateOptionalDeckCoverCardIds({ coverCardIds: ['L-12', 'L-12'] }), false)

const source = relative => readFileSync(new URL(relative, import.meta.url), 'utf8')
const coverComponent = source('../src/l12/DeckCover.vue')
assert.doesNotMatch(coverComponent, /\.deck-cover--compact\{height:(?:104|96)px/,
  'compact hosts must preserve the 12:5 banner instead of fixed-height cropping')
assert.match(coverComponent, /bottom:0;right:-2%/,
  'representative cards must sit flush right with only their own staggered bottom clipping')
assert.match(coverComponent, /right:-2%;width:55%/,
  'representative cards must use the enlarged shared fan without losing the right-edge anchor')
for (const whiteTextContract of [
  '.deck-cover__copy small{overflow:hidden;color:#f4f1e8',
  '.deck-cover__copy span,.deck-cover__copy em{overflow:hidden;color:#f4f1e8',
  '.deck-cover__footer em{min-width:0;overflow:hidden;flex:1;color:#f4f1e8',
  'background:rgba(8,14,18,.84);color:#f4f1e8',
  'background:rgba(7,11,15,.82);color:#f4f1e8',
]) assert.ok(coverComponent.includes(whiteTextContract), 'deck cover text must remain white across factions')
assert.doesNotMatch(coverComponent, /deck-cover__copy b\)\{color:color-mix\(in srgb,var\(--deck-cover-accent\)/,
  'hover must not recolor deck cover text with the faction accent')
const deckBannerMasterIds = [
  'S01-01M1', 'S01-01M2', 'S01-02M1', 'S01-02M3', 'S01-03M1', 'S01-03M2', 'S01-04M1', 'S01-04M2',
  'S02-01M1', 'S02-02M1', 'S02-03M1', 'S02-04M1', 'S02-05M1', 'S02-05M2', 'S02-06M1', 'S02-06M2',
  'ST01-M1', 'ST02-M1', 'ST03-M1', 'ST04-M1', 'ST05-M1', 'ST06-M1',
]
let deckBannerBytes = 0
for (const masterId of deckBannerMasterIds) {
  const banner = readFileSync(new URL(`../public/assets/l12/deck-banners/${masterId}.png`, import.meta.url))
  deckBannerBytes += banner.length
  assert.deepEqual([...banner.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10], `${masterId} must remain a PNG`)
  assert.equal(banner.readUInt32BE(16), 1200, `${masterId} banner width must be 1200`)
  assert.equal(banner.readUInt32BE(20), 500, `${masterId} banner height must be 500`)
  assert.match(coverComponent, new RegExp(`['\"]${masterId}['\"]`), `${masterId} must be in the formal banner allowlist`)
}
assert.ok(deckBannerBytes > 0, 'formal banner assets must not be empty')
assert.doesNotMatch(coverComponent, /['\"]S01-02M2['\"]/, 'the non-selectable revived Osiris must keep the fallback path')
for (const contract of [
  "variant?: 'regular' | 'featured' | 'compact'",
  'metaPrefix?: string',
  'selectDeckCoverCardIds',
  'validateDeckCoverCardIds',
  'getFactionPresentation',
  'v-for="card in cards"',
  '.deck-cover__card:nth-child(1){z-index:3}',
  '.deck-cover__card:nth-child(2){z-index:2;right:26%',
  '.deck-cover__card:nth-child(3){z-index:1;right:48%',
  '@media(hover:hover) and (pointer:fine)',
  ':global(button:focus-visible > .deck-cover)',
  ':global(button:focus-visible > .deck-cover__footer)',
  'transform:translateY(-4px)',
  'transform:scale(1.08)',
  'border-radius:0',
  'aspect-ratio:12/5',
  'height:auto',
  'bottom:0;right:-2%',
  'transform-origin:100% 100%',
  '.deck-cover--compact{height:auto;aspect-ratio:12/5',
  "import { deploymentPath } from './deploymentBase'",
  'deck-cover__master-banner',
  'loading="lazy"',
  'fetchpriority="low"',
  '@error="rejectMasterBanner"',
  'height:30px',
  'flex-wrap:nowrap',
  '@media(min-width:701px)',
  'bottom:0;right:-2%',
  'class="deck-cover__footer"',
  'data-cover-footer',
  'color-mix(in srgb,var(--deck-cover-accent) 76%,transparent)',
  '@media(prefers-reduced-motion:reduce)',
  'background:transparent!important',
]) assert.match(coverComponent, new RegExp(contract.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))

assert.doesNotMatch(coverComponent, /\.deck-cover__cards\{width:65%\}/,
  'mobile covers must retain the same container-proportional fan instead of enlarging it again')

const consumers = [
  '../src/l12/site/DeckLibraryPage.vue',
  '../src/l12/SavedDeckSelector.vue',
  '../src/l12/LobbyPage.vue',
  '../src/l12/site/BattleHubPage.vue',
  '../src/l12/site/SandboxPage.vue',
  '../src/l12/L12DeckEditor.vue',
]
for (const path of consumers) {
  const contents = source(path)
  assert.match(contents, /import DeckCover from/)
  assert.match(contents, /<DeckCover\b/)
}
for (const path of ['../src/l12/SavedDeckSelector.vue', '../src/l12/LobbyPage.vue', '../src/l12/L12DeckEditor.vue']) {
  const contents = source(path)
  assert.match(contents, /@media\(hover:hover\) and \(pointer:fine\)/)
  assert.match(contents, /transform:translateY\(-4px\)/)
  assert.match(contents, /:deep\(\.deck-cover\).*transform:none/,
    `${path} must lift its outer host and suppress the nested duplicate translation`)
  assert.doesNotMatch(contents, /border-radius:13px/,
    `${path} must keep deck cover hosts square`)
}
const library = source('../src/l12/site/DeckLibraryPage.vue')
const battleHub = source('../src/l12/site/BattleHubPage.vue')
assert.equal((battleHub.match(/class="deck-selection-trigger"/g) || []).length, 3,
  'matchmaking, friendly setup and active room must share the clickable cover entry')
assert.match(battleHub, /const deckSelectionAriaLabel = computed\(\(\) => `更换牌库：/)
assert.match(battleHub, /class="deck-selection-trigger"[^>]*:disabled="me\?\.ready"/,
  'ready rooms must keep the clickable cover visibly locked')
assert.doesNotMatch(battleHub, /<button[^>]*>更换牌库<\/button>/,
  'the lobby must not restore a separate change-deck button')
assert.match(battleHub, /class="match-action-stack"[\s\S]*class="deck-selection-block deck-selection-block--match"/,
  'matchmaking must place the deck selector after its primary controls')
assert.match(battleHub, /class="friendly-entry-grid"[\s\S]*class="room-settings-block"[\s\S]*class="deck-selection-block deck-selection-block--friendly"/,
  'friendly setup must flow from room entry through rules to the deck selector')
assert.match(battleHub, /class="deck-selection-block deck-selection-block--match"/)
assert.match(battleHub, /class="deck-selection-block deck-selection-block--friendly"/)
assert.match(battleHub, /class="deck-selection-block deck-selection-block--room"/)
assert.match(battleHub, /\.deck-selection-block\{[^}]*grid-template-columns:minmax\(140px,\.55fr\) minmax\(320px,1fr\)[^}]*width:100%[^}]*border:0;border-top:1px solid #394851;background:transparent/,
  'deck selection must be a full-width bottom function row rather than a nested centered panel')
assert.match(battleHub, /\.deck-selection-trigger\{[^}]*max-width:520px;justify-self:end/,
  'desktop deck covers must sit at the right of their owning function row')
assert.match(battleHub, /@media\(max-width:700px\)\{[\s\S]*?\.deck-selection-block\{grid-template-columns:1fr[^}]*}[\s\S]*?\.deck-selection-trigger\{max-width:none;justify-self:stretch/,
  'mobile deck selection must stack its status above the full-width cover')
for (const preservedLobbyContract of [
  'data-ui-contract="faction-totals-above-public-match"',
  'ranked.factionTotals[faction.name]',
  "operationsPolicy.season.name",
  ">排位规则</button>",
  ">排位匹配</button>",
  ">休闲匹配</button>",
  '@click="onCreate">创建新房间</button>',
  '@click="onJoin">加入对战</button>',
  '@click="onSpectate">直接观战</button>',
  'v-model="roomOptions.useCardRestrictions"',
  'v-model="roomOptions.spectating"',
  'v-model="roomOptions.handVisibility"',
  'v-model="roomOptions.disasterMode"',
  "@click=\"toggleReady\">{{ me?.ready ? '取消准备'",
  'data-ui-contract="long-term-announcements-above-deck"',
  "tab === 'tutorial'",
  'class="tutorial-return"',
  '.battle-hub.tutorial-mode',
]) assert.ok(battleHub.includes(preservedLobbyContract), `lobby redesign must preserve: ${preservedLobbyContract}`)
assert.match(battleHub, /data-lobby-art-slot="match"/)
assert.match(battleHub, /data-lobby-art-slot="friendly"/)
assert.match(battleHub, /data-lobby-art-slot="tutorial"/)
for (const asset of ['Match_1_w.png','Match_1_m.png','Match_2_w.png','Match_2_m.png','Match_3_w.png','Match_3_m.png']) {
  assert.ok(existsSync(new URL(`../src/assets/l12/battle/${asset}`, import.meta.url)), `${asset} must be bundled from the source asset tree`)
  assert.ok(battleHub.includes(`from '@/assets/l12/battle/${asset}'`), `${asset} must be imported through Vite`)
}
for (const contract of [
  '<small>MATCH</small><b>匹配</b>',
  '<small>ROOM</small><b>好友房</b>',
  '<small>TUTORIAL</small><b>教学</b>',
  'source media="(max-width: 700px)" :srcset="matchModeMobile"',
  'source media="(max-width: 700px)" :srcset="roomModeMobile"',
  'source media="(max-width: 700px)" :srcset="tutorialModeMobile"',
]) assert.ok(battleHub.includes(contract), `lobby artwork contract missing: ${contract}`)
assert.match(battleHub, /\.mode-tab-art\{[^}]*filter:grayscale\(1\) brightness\(\.42\) contrast\(\.9\)/,
  'unselected mode artwork must be gray without dimming its text layer')
assert.match(battleHub, /\.mode-tabs button\.active \.mode-tab-art\{filter:none}/,
  'only the selected mode artwork may retain its original color')
assert.match(battleHub, /button:not\(\.active\):hover \.mode-tab-art,\.mode-tabs button:not\(\.active\):focus-visible \.mode-tab-art\{filter:grayscale\(1\)/,
  'hover and keyboard focus must keep an unselected mode gray')
assert.equal((battleHub.match(/<button data-lobby-art-slot=/g) || []).length, 3,
  'each functional lobby mode tab must own exactly one replaceable illustration slot')
assert.match(battleHub, /\.mode-tabs button\{[^}]*min-height:112px/,
  'lobby mode navigation must render as functional illustrated blocks')
assert.doesNotMatch(battleHub, /mode-hero-art/,
  'lobby artwork must stay attached to functional navigation instead of a standalone decoration block')
assert.match(source('../src/l12/L12DeckEditor.vue'), /\.mobile-saved-decks-list\{grid-auto-rows:max-content\}/,
  'cover buttons must keep their content-sized grid rows and form a scrollable mobile list')
assert.match(library, /variant="featured"/)
assert.doesNotMatch(library, /badge="热门牌库"/)
assert.match(library, /replace\(\/\^环境\\s\*\//,
  'cover environment labels must remove the redundant 环境 prefix')
assert.match(library, /:meta-prefix="compactDeckEnvironmentLabel\(entry\.environment\.value \?\? 'pending'\)" :meta="`浏览量 /,
  'featured covers must place environment immediately before view count')
assert.match(coverComponent, /<\/div>\s*<div v-if="metaPrefix" class="deck-cover__footer"/,
  'featured statistics must be outside the 12:5 visual body')
assert.match(library, /class="plaza-card-stats"><span class="deck-environment-badge">[\s\S]{0,180}<span>浏览量 /,
  'regular public covers must place environment immediately before view count')
assert.match(library, /class="deck-environment-badge">\{\{ compactDeckEnvironmentLabel\(entry\.environment\.value \?\? 'pending'\) \}\}/,
  'regular public environment badges must use the same compact label')
assert.doesNotMatch(library, /<DeckCover[^>]*\/><span class="deck-environment-badge">/,
  'regular cover environment must not remain at the top right of the cover')
assert.match(library, /\.mine-grid>article,\.plaza-grid>article,\.hot-deck-loop>button\{overflow:visible;border-radius:0/)
assert.doesNotMatch(library, /rgba\(215,184,91,\.5\)/,
  'deck hover glow must come from the faction-aware shared cover')
assert.match(library, /class="plaza-summary"[\s\S]{0,700}<DeckCover/)
assert.match(source('../src/l12/LobbyPage.vue'), /repeat\(auto-fit,minmax\(min\(180px,100%\),1fr\)\)/,
  'legacy room cover grid must collapse responsively instead of squeezing four covers')
assert.doesNotMatch(source('../src/l12/site/PublicDeckDetailPage.vue'), /DeckCover/,
  'public detail and matchup layouts remain unchanged')
assert.match(source('../src/l12/decks.ts'), /platformRequest<unknown>\(privateDeckSummaryRequestPath\(query\),\s*\{\s*cache: 'no-store', headers: getDeckCoverRequestHeaders\(\)/)
const platform = source('../src/l12/platform.ts')
assert.match(platform, /platformRequest<PublicDeckSummaryPage>\([^;]*headers: getDeckCoverRequestHeaders\(\)/)
const platformTree = ts.createSourceFile('platform.ts', platform, ts.ScriptTarget.Latest, true)
const getter = platformTree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'getDeckCoverRequestHeaders')
assert.ok(getter)
const getterSource = ts.transpileModule(getter.getText(platformTree), { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const runGetter = new Function('exports', 'apiBase', 'deckCoverHeadersForOrigin', 'globalThis', getterSource + ';return exports.getDeckCoverRequestHeaders;')
for (const api of ['https://site.invalid/testrun', 'https://old-api.invalid']) {
  const getHeaders = runGetter({}, () => api, deckCoverHeadersForOrigin, { location: { origin: 'https://site.invalid' } })
  assert.deepEqual(getHeaders(), api.startsWith('https://site.invalid/') ? DECK_COVER_REQUEST_HEADERS : {})
}

console.log('Deck cover selection passed: type coverage, refill priority, natural ties, deduplication and bounded contract.')
