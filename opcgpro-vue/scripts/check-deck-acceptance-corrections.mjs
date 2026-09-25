import fs from 'node:fs'
import assert from 'node:assert/strict'
const read = name => fs.readFileSync(new URL(`../src/l12/${name}`, import.meta.url), 'utf8')
const editor = read('L12DeckEditor.vue'), library = read('site/DeckLibraryPage.vue'), detail = read('site/PublicDeckDetailPage.vue')
const checks = [
  ['分享图与内容编辑共用短码且不使用已退役 UUID 路由', editor.includes('publicDeckApi.list()') && editor.includes('publicDeckRouteReference') && editor.includes(':publication-id="publicationCode"') && !editor.includes('publicDeckApi.get(id)') && editor.includes('verifiedPublicDeckUrl') && library.includes('publicDeckApi.get(publicDeckRouteReference(candidate))')],
  ['主宰术语', ['L12DeckEditor.vue','site/DeckLibraryPage.vue','site/PublicDeckDetailPage.vue','site/PublicDeckContentEditor.vue'].every(name => !read(name).includes('主城'))],
  ['详情下方常驻唯一已保存牌库区', editor.includes('class="saved-decks-panel grand-panel"') && editor.includes('class="saved-list"') && !editor.includes('<label>已保存牌库<select') && !editor.includes('class="saved-decks-dialog"') && !editor.includes('savedDecksOpen')],
  ['我的牌库三动作', library.includes('@click="duplicateMine(deck)"') && library.includes('@click="copyCode(deck)"') && library.includes('@click="deleteMine(deck)"') && library.includes('window.confirm(message)')],
  ['对局建议复用 Profile', detail.includes('<DeckProfile compact :master-id="row.opponentMasterId"') && !detail.includes('<CardImage :card-id="row.opponentMasterId"')],
  ['构筑及起手点击共享详情', read('site/DeckConstructionBrowser.vue').includes('@click="selectCard(entry.cardId)"') && (detail.includes('@click="selectCard(card)"') || detail.includes('@click="selectCard(copy.card!)"')) && detail.includes('external-details @select="selectCard"') && detail.includes('class="archive-detail public-card-detail"') && detail.includes('--l12-card-detail-sidebar-width') && !detail.includes('dblclick')],
]
checks.push(['编辑牌库短文案及跳转参数', detail.includes('@click="editDeck">编辑牌库</button>') && detail.includes('published: entry.value.id, returnTo: route.fullPath')])
const failures = checks.filter(([,pass]) => !pass).map(([name]) => name)
assert.deepEqual(failures, [], `验收纠错失败：${failures.join('、')}`)
console.log(`牌库验收纠错合同通过 ${checks.length}/${checks.length}`)
