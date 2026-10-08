import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  ruleCenterGroups,
  ruleCenterLegacyPredicateMap,
  ruleCenterPredicateNames,
  ruleCenterTemplateContract,
} from './rule-center-template-contract.mjs'

const sourceRoot = resolve(process.env.L12_SOURCE_ROOT
  || resolve(dirname(fileURLToPath(import.meta.url)), '..'))
const read = path => readFileSync(resolve(sourceRoot, path), 'utf8').replace(/\r\n?/g, '\n')
const baselineSources = {
  player: read('src/l12/site/RuleCenterPage.vue'),
  admin: read('src/l12/site/AdminRuleRulingsPanel.vue'),
}

function replaceNth(source, before, after, occurrence = 1) {
  let from = 0, index = -1
  for (let count = 0; count < occurrence; count++) {
    index = source.indexOf(before, from)
    assert.notEqual(index, -1, `fixture marker ${occurrence} missing: ${before.slice(0, 100)}`)
    from = index + before.length
  }
  return source.slice(0, index) + after + source.slice(index + before.length)
}

const changed = (key, before, after, occurrence = 1, sources = baselineSources) => ({
  ...sources,
  [key]: replaceNth(sources[key], before, after, occurrence),
})

let cases = 0
const accept = (name, sources) => {
  const contract = ruleCenterTemplateContract(sources)
  assert.deepEqual(Object.entries(contract).filter(([, passed]) => !passed), [], name)
  cases++
}
const reject = (name, predicate, sources) => {
  assert.equal(ruleCenterTemplateContract(sources)[predicate], false, `${name}: ${predicate} must fail closed`)
  cases++
}

const baseline = ruleCenterTemplateContract(baselineSources)
assert.equal(ruleCenterPredicateNames.length, 6)
assert.deepEqual(ruleCenterGroups(baseline).map(([, predicates]) => predicates.length), [3, 1, 2])
assert.deepEqual(ruleCenterLegacyPredicateMap.map(group => group.removed), [2, 4, 1])
assert.equal(ruleCenterLegacyPredicateMap.reduce((sum, group) => sum + group.removed, 0), 7)
accept('real public and admin rule-center consumers', baselineSources)

let formatted = changed('player', 'class="rules-page ui-state-scope"', 'class="state ui-state-scope rules-page"')
formatted = changed('admin', 'class="admin-item-card center-editor"', 'class="center-editor active admin-item-card"', 1, formatted)
formatted = changed('player', 'v-if="coreTableOfContents.length" aria-label=',
  'v-if=" ( coreTableOfContents.length ) " aria-label=', 1, formatted)
accept('class order extra state classes and expression whitespace', formatted)

accept('non-rendering dynamic accessibility state stays compatible', changed('player',
  '<div class="rules-page ui-state-scope">',
  '<div :aria-busy="loading" class="rules-page ui-state-scope">'))

let ofLoops = changed('player', 'v-for="chapter in coreTableOfContents"', 'v-for="chapter of coreTableOfContents"')
ofLoops = changed('admin', 'v-for="item in group.items"', 'v-for="item of group.items"', 1, ofLoops)
accept('Vue in and of loop syntax are equivalent', ofLoops)

accept('explicit CardImage kebab spelling', changed('player', '<CardImage :card-id="linked.card.id"',
  '<card-image :card-id="linked.card.id"'))

let transparentRoot = changed('player', '<template>\n  <div class="rules-page ui-state-scope">',
  '<template>\n  <template>\n  <div class="rules-page ui-state-scope">')
transparentRoot = changed('player', '  </div>\n</template>\n\n<style scoped>',
  '  </div>\n  </template>\n</template>\n\n<style scoped>', 1, transparentRoot)
accept('unconditional template ancestor is transparent', transparentRoot)

reject('core directory hidden by page ancestor', 'publicCoreDirectory', changed('player',
  '<div class="rules-page ui-state-scope">', '<div v-show="false" class="rules-page ui-state-scope">'))
reject('core directory hidden by dynamic root class', 'publicCoreDirectory', changed('player',
  '<div class="rules-page ui-state-scope">',
  '<div :class="{ hidden: concealRules }" class="rules-page ui-state-scope">'))
reject('core directory hidden by dynamic hidden binding', 'publicCoreDirectory', changed('player',
  '<div class="rules-page ui-state-scope">',
  '<div :hidden="concealRules" class="rules-page ui-state-scope">'))
reject('core directory blocked by dynamic inert binding', 'publicCoreDirectory', changed('player',
  '<div class="rules-page ui-state-scope">',
  '<div :inert="lockRules" class="rules-page ui-state-scope">'))
reject('core directory hidden by native ancestor', 'publicCoreDirectory', changed('player',
  '<div class="rule-layout"><aside>', '<div hidden class="rule-layout"><aside>'))
reject('core directory borrowed by wrong branch', 'publicCoreDirectory', changed('player',
  '<template v-else-if="tab === \'core\'">', '<template v-else-if="tab === \'quick-start\'">'))
reject('core directory on wrong host', 'publicCoreDirectory', changed('player',
  '<nav v-if="coreTableOfContents.length" aria-label="规则手册章节目录">',
  '<div v-if="coreTableOfContents.length" aria-label="规则手册章节目录">'))
reject('core chapter handler uses wrong argument', 'publicCoreDirectory', changed('player',
  '@click="scrollToCoreChapter(chapter.firstId)"', '@click="scrollToCoreChapter(chapter.chapter)"'))
reject('core directory object binding can override accessibility', 'publicCoreDirectory', changed('player',
  '<nav v-if="coreTableOfContents.length" aria-label=', '<nav v-bind="navProps" v-if="coreTableOfContents.length" aria-label='))

reject('FAQ answer borrows another item condition', 'publicExpandableRulings', changed('player',
  '<div v-if="openIds.has(item.id)" class="faq-answer">',
  '<div v-if="openIds.has(other.id)" class="faq-answer">', 1))
reject('construction disclosure cannot borrow FAQ loop object', 'publicExpandableRulings', changed('player',
  'v-for="item in constructionRulings"', 'v-for="item in faqResults"'))
reject('tournament disclosure loses expanded state', 'publicExpandableRulings', changed('player',
  '<div v-if="openIds.has(item.id)" class="faq-answer">', '<div v-show="openIds.has(item.id)" class="faq-answer">', 3))
reject('FAQ toggle uses wrong handler argument', 'publicExpandableRulings', changed('player',
  '@click="toggleEntry(item)"', '@click="toggleEntry(other)"', 1))
reject('FAQ toggle cannot be statically disabled', 'publicExpandableRulings', changed('player',
  '<button class="faq-question"', '<button disabled class="faq-question"', 1))
reject('FAQ list hidden ancestor', 'publicExpandableRulings', changed('player',
  '<div class="faq-list"><article v-for="item in faqResults"',
  '<div v-show="false" class="faq-list"><article v-for="item in faqResults"'))

reject('linked cards use wrong loop object', 'publicLinkedCardDisclosure', changed('player',
  'v-for="linked in linkedCards(item)"', 'v-for="linked in linkedCards(other)"'))
reject('linked cards use wrong availability condition', 'publicLinkedCardDisclosure', changed('player',
  'v-if="item.cardIds.length" class="ruling-cards"', 'v-if="other.cardIds.length" class="ruling-cards"'))
reject('linked detail handler selects wrong model', 'publicLinkedCardDisclosure', changed('player',
  '@click="detailCard = linked.card"', '@click="detailCard = item.card"'))
reject('linked CardImage binds wrong card', 'publicLinkedCardDisclosure', changed('player',
  ':card-id="linked.card.id"', ':card-id="item.id"'))
reject('catalog load loses expanded-and-linked guard', 'publicLinkedCardDisclosure', changed('player',
  'if (next.has(item.id) && item.cardIds.length) void ensureCardCatalog()',
  'if (item.cardIds.length) void ensureCardCatalog()'))

const centerForm = '<fieldset :disabled="!canDraft"><div class="form-grid">'
accept('safe center field bracket spelling is equivalent', changed('admin',
  'v-model.trim="item.row.chapter"', 'v-model.trim="item.row[\'chapter\']"'))
reject('center stable ID cannot return via bracket v-model', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<input v-model.trim="item.row['id']">`))
reject('center page cannot return under renamed label', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<label>定位<input v-model="item.row.page"></label>`))
reject('center topic cannot return through input assignment', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<input @input="item.row.topic = $event.target.value">`))
reject('center collection cannot return through value binding', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<select :value="item.collection"><option>raw</option></select>`))
reject('center raw object binding fails closed', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<input v-bind="rawCenterField">`))
reject('center dynamic row field fails closed', 'adminCenterImmutableFields', changed('admin', centerForm,
  `${centerForm}<input v-model="item.row[field]">`))

reject('center publish wrong permission', 'adminCenterPublishOwnership', changed('admin',
  'v-if="hasPermission(\'admin.content.publish\')" class="publish"',
  'v-if="canDraft" class="publish"', 1))
reject('center publish loses busy disabled state', 'adminCenterPublishOwnership', changed('admin',
  'class="publish" :disabled="publishingCenterIds.has(item.id)" @click="publishCenterItem',
  'class="publish" @click="publishCenterItem'))
reject('center publish cannot be statically disabled', 'adminCenterPublishOwnership', changed('admin',
  'class="publish" :disabled="publishingCenterIds.has(item.id)" @click="publishCenterItem',
  'class="publish" disabled :disabled="publishingCenterIds.has(item.id)" @click="publishCenterItem'))
reject('center publish cannot borrow ruling document busy state', 'adminCenterPublishOwnership', changed('admin',
  'publishingCenterIds.has(item.id)', 'publishingRulingIds.has(item.id)', 1))
reject('center publish cannot borrow another item busy state', 'adminCenterPublishOwnership', changed('admin',
  'publishingCenterIds.has(item.id)', 'publishingCenterIds.has(other.id)', 1))
reject('center publish label must use the same owner and document', 'adminCenterPublishOwnership', changed('admin',
  "publishingCenterIds.has(item.id) ? '发布中…'", "publishingRulingIds.has(item.id) ? '发布中…'"))
reject('center publish swaps handler arguments', 'adminCenterPublishOwnership', changed('admin',
  'publishCenterItem(item.collection, item.id)', 'publishCenterItem(item.id, item.collection)'))
reject('center publish duplicated outside local actions', 'adminCenterPublishOwnership', changed('admin',
  '<header><div><small>RULE CENTER REVIEW</small>',
  '<header><button @click="publishCenterItem(item.collection, item.id)">detached</button><div><small>RULE CENTER REVIEW</small>'))
reject('admin root hidden from all object actions', 'adminCenterPublishOwnership', changed('admin',
  '<section class="ruling-admin">', '<section hidden class="ruling-admin">'))

reject('ruling publish wrong permission', 'adminRulingPublishOwnership', changed('admin',
  'v-if="hasPermission(\'admin.content.publish\')" class="publish"',
  'v-if="canDraft" class="publish"', 2))
reject('ruling publish binds center item busy state', 'adminRulingPublishOwnership', changed('admin',
  ':disabled="publishingRulingIds.has(row.item.id)" @click="publishRuling(row.item)"',
  ':disabled="publishingCenterIds.has(row.item.id)" @click="publishRuling(row.item)"'))
reject('ruling publish cannot borrow another row busy state', 'adminRulingPublishOwnership', changed('admin',
  'publishingRulingIds.has(row.item.id)', 'publishingRulingIds.has(other.item.id)', 1))
reject('ruling publish label must use the same owner and document', 'adminRulingPublishOwnership', changed('admin',
  "publishingRulingIds.has(row.item.id) ? '发布中…'", "publishingCenterIds.has(row.item.id) ? '发布中…'"))
reject('ruling publish uses wrong handler argument', 'adminRulingPublishOwnership', changed('admin',
  'publishRuling(row.item)', 'publishRuling(item)'))
reject('ruling publish hidden by conditional ancestor', 'adminRulingPublishOwnership', changed('admin',
  '<div class="item-actions"><button v-if="canDraft" @click="saveRulingItem(row.item)">',
  '<div v-show="false" class="item-actions"><button v-if="canDraft" @click="saveRulingItem(row.item)">'))

const fakeSources = {
  player: `<script setup>const fake = '<nav aria-label="规则手册章节目录">'</script><template><!-- v-if="openIds.has(item.id)" --></template>`,
  admin: `<script setup>const fake = 'publishCenterItem(item.collection,item.id)'</script><template><style>.publish-queue{}</style></template>`,
}
assert.deepEqual(Object.entries(ruleCenterTemplateContract(fakeSources)).filter(([, passed]) => passed), [],
  'comments scripts and styles cannot lend rule-center evidence')
cases++

console.log(`A3 rule-center template contract passed: 6 predicates, 3 groups, 7 legacy subpredicates, ${cases} focused cases`)
