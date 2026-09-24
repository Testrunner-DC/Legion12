import assert from 'node:assert/strict'
import fs from 'node:fs'

const read = relative => fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
const editor = read('../src/l12/L12DeckEditor.vue')
const library = read('../src/l12/site/DeckLibraryPage.vue')
const detail = read('../src/l12/site/PublicDeckDetailPage.vue')
const share = read('../src/l12/site/deckShare.ts')
const entry = read('../src/l12/site/publicDeckEntry.ts')
const picker = read('../src/l12/SingleCardPicker.vue')

const removedDetailCopy = [
  '版本永久保存；相同构筑重复发布不会制造新版本。',
  '这里只展示能由权威记录证明属于具体公开版本的对局。',
  '不会保存结果或生成真实对局记录。',
  '仍以对局服务端结算为准',
]

const checks = [
  ['详情页移除面向存储与实现的说明', removedDetailCopy.every(value => !detail.includes(value))],
  ['详情页保留版本日期、匿名聚合统计和不可用原因', detail.includes('formatTime(version.createdAt)') && detail.includes('details.matchStatistics.groups') && detail.includes('stat.games') && detail.includes('formatRate(stat.winRate)') && detail.includes('details.matchBindingMessage')],
  ['详情页不再展示单局或回放入口', !detail.includes('match.matchId') && !detail.includes('match.playedAt') && !detail.includes('match.result') && !detail.includes('replayPath')],
  ['编辑器与我的牌库复用同一公开实体核验', entry.includes('matchesPublishedDeckReference') && editor.includes('matchesPublishedDeckReference(currentDeck()') && library.includes('matchesPublishedDeckReference(deck, publishedDeck')],
  ['公开实体核验同时匹配ID、版本与作者', ['published.id === publicationId', 'published.deck.publicationVersion === publicationVersion', 'published.ownerId === ownerId'].every(value => entry.includes(value))],
  ['我的牌库分享图仅在核验后传入稳定公开链接', library.includes('const publicUrl = await verifiedPublicDeckUrl(deck)') && library.includes('{ publicUrl, alternateArts: ownedAlternateArts.value }')],
  ['当前牌表逐副本显示保存的原画与异画', editor.includes('class="deck-copy-art-strip"') && editor.includes('deckCopyPresentations(entry.card,entry.count)') && editor.includes('{{ index + 1 }} {{ copy.label }}')],
  ['起手按逐副本身份抽取并显示实际卡图', editor.includes('mainDeckCopies.value.map(copy => copy.key)') && editor.includes(':card-id="copy.cardImageId"') && editor.includes('{{ copy.card.number }} · {{ copy.label }}')],
  ['分享图沿用alternateArtCopies并区分原画和异画', share.includes('deck.alternateArtCopies?.[cardId]') && share.includes("label: art ?") && share.includes("group.label.slice") && share.includes('group.count')],
  ['公开详情起手也不把异画默认为原画', detail.includes('deckImageGroups(entry.value.deck') && detail.includes(':card-id="copy.cardImageId"') && detail.includes('{{ copy.label }}')],
  ['单卡选择器位于移动筛选抽屉之上', picker.includes('z-index:5200')],
]

assert.deepEqual(checks.filter(([, passed]) => !passed).map(([label]) => label), [])
console.log(`牌库最终收口合同通过：${checks.length}/${checks.length} 项`)
