import fs from 'node:fs'

const read = path => fs.readFileSync(new URL(path, import.meta.url), 'utf8')
const editor = read('../src/l12/L12DeckEditor.vue')
const library = read('../src/l12/site/DeckLibraryPage.vue')
const detail = read('../src/l12/site/PublicDeckDetailPage.vue')
const content = read('../src/l12/site/PublicDeckContentEditor.vue')
const share = read('../src/l12/site/deckShare.ts')
const image = read('../src/l12/CardImage.vue')
const catalogDetail = read('../src/l12/CatalogCardDetails.vue')
const construction = read('../src/l12/site/DeckConstructionBrowser.vue')

const checks = [
  ['我的牌库逐项删除', library.includes('deleteMine(deck)') && library.includes('删除本地牌库不会删除公开版本')],
  ['公开内容进入编辑器子页', editor.includes('PublicDeckContentEditor') && editor.includes("workspace === 'content'")],
  ['指南与对局建议保存闭环', content.includes('publicDeckApi.updateContent') && content.includes('publicDeckApi.get')],
  ['空指南与空对局建议不生成内容和锚点', detail.includes('const hasGuide = computed') && detail.includes('const hasMatchups = computed') && detail.includes('v-if="hasGuide" id="public-deck-guide"') && detail.includes('v-if="hasMatchups" id="public-deck-matchups"') && detail.includes('v-for="tab in sectionTabs"')],
  ['对局建议显示主城头像且统一主城用语', detail.includes('class="matchup-city"') && detail.includes('<CardImage :card-id="row.opponentMasterId"') && detail.includes('对阵 {{ homeCityName(row.opponentMasterId) }}') && content.includes('添加对方主城')],
  ['导航与操作同栏', detail.includes('class="detail-toolbar"')],
  ['编辑器只收纳产品卡池筛选，其余筛选常驻', editor.includes('class="pool-selector-trigger"') && editor.includes('class="catalog-filter-bar"') && !editor.includes('卡池（可多选）') && !editor.includes('mobileFiltersOpen')],
  ['编辑器只有一个牌库切换入口且详情可折叠', editor.includes('@click="savedDecksOpen = true">切换牌库') && editor.includes('class="saved-decks-dialog"') && editor.includes('detailCollapsed') && !editor.includes('class="saved-decks-panel grand-panel"') && !editor.includes('class="saved-deck-switcher"')],
  ['编辑器主城候选排除 divinity', editor.includes("card.cardType === 'master'") && !editor.match(/card\.cardType === 'master'\s*\|\|\s*card\.cardType === 'divinity'/)],
  ['共享卡图不再旋转横卡', image.includes('l12-card-image--landscape') && !image.includes('rotate(90deg)')],
  ['构筑详情复用图鉴同一组件并隐藏图鉴专属信息', construction.includes('CatalogCardDetails') && construction.includes(':show-catalog-only="false"') && catalogDetail.includes('CardDetailContent') && catalogDetail.includes(':show-catalog-only="showCatalogOnly"')],
  ['构筑卡表随页面自然增长', construction.includes('.construction-grid{') && construction.includes('overflow:visible') && !construction.includes('max-height:58vh')],
  ['公开链接才生成二维码', share.includes('options.publicUrl') && share.includes('if (qrImage && publicUrl)')],
  ['二维码只保留白边并位于右下角，不挤压卡表', share.includes("margin: 3") && share.includes("fillStyle = '#ffffff'") && share.includes('const qrX = 1860 - qrSize') && !share.includes('扫码查看') && !share.includes('reservedQrWidth')],
  ['我的牌库支持名称、主城、合法性、排序和独立空状态', ['mineQuery', 'mineHomeCityFilter', 'mineLegalFilter', 'mineSort', 'filteredMine'].every(value => library.includes(value)) && library.includes('没有符合筛选条件的牌库') && library.includes('还没有自定义牌库')],
  ['公开牌库单卡筛选复用共享选择器', library.includes('SingleCardPicker') && library.includes('plazaCardPickerItems') && library.includes('choosePlazaCard')],
  ['首次公开可同步保存指南和对局建议', library.includes('publicDeckApi.publish(deck)') && library.includes('publicDeckApi.updateContent(entry.id, publishGuide.value, publishMatchups.value)') && library.includes('可在首次发布时同步填写公开内容')],
  ['移动端次要操作进入更多操作菜单', library.includes('<details><summary>更多操作</summary>') && library.includes('deck-actions-menu') && editor.includes('class="more-actions-trigger"') && editor.includes('class="secondary-actions"')],
]

const failed = checks.filter(([, passed]) => !passed)
if (failed.length) throw new Error(`牌库界面收口合同失败：${failed.map(([name]) => name).join('、')}`)
console.log(`牌库界面收口合同通过：${checks.length}/${checks.length} 项`)
