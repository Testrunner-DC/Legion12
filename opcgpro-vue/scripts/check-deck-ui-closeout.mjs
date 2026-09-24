import fs from 'node:fs'

const read = path => fs.readFileSync(new URL(path, import.meta.url), 'utf8')
const editor = read('../src/l12/L12DeckEditor.vue')
const library = read('../src/l12/site/DeckLibraryPage.vue')
const detail = read('../src/l12/site/PublicDeckDetailPage.vue')
const content = read('../src/l12/site/PublicDeckContentEditor.vue')
const share = read('../src/l12/site/deckShare.ts')
const image = read('../src/l12/CardImage.vue')
const catalogDetail = read('../src/l12/CatalogCardDetails.vue')

const checks = [
  ['我的牌库逐项删除', library.includes('deleteMine(deck)') && library.includes('删除本地牌库不会删除公开版本')],
  ['公开内容进入编辑器子页', editor.includes('PublicDeckContentEditor') && editor.includes("workspace === 'content'")],
  ['指南与对局建议保存闭环', content.includes('publicDeckApi.updateContent') && content.includes('publicDeckApi.get')],
  ['详情同页锚点', detail.includes('scrollIntoView') && detail.includes('public-deck-construction') && !detail.includes('v-else-if="activeSection')],
  ['导航与操作同栏', detail.includes('class="detail-toolbar"')],
  ['全尺寸单一筛选入口', editor.includes('always-visible') && editor.includes('catalog-filter-summary')],
  ['共享卡图不再旋转横卡', image.includes('l12-card-image--landscape') && !image.includes('rotate(90deg)')],
  ['卡牌详情复用唯一正文组件', catalogDetail.includes('CardDetailContent') && !catalogDetail.includes('cardTypeLabel')],
  ['公开链接才生成二维码', share.includes('options.publicUrl') && share.includes('if (qrImage && publicUrl)')],
  ['二维码含四模块静区', share.includes("margin: 4")],
]

const failed = checks.filter(([, passed]) => !passed)
if (failed.length) throw new Error(`牌库界面收口合同失败：${failed.map(([name]) => name).join('、')}`)
console.log(`牌库界面收口合同通过：${checks.length}/${checks.length} 项`)
