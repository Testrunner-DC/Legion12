<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type AlternateArt, type AlternateArtAwardRule, type AlternateArtGrant, type AlternateArtProduct, type AlternateArtRankedParticipantDispatchPreview, type AlternateArtSearchPage, type SiteMedia } from '@/l12/platform'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import MediaUploadField from './MediaUploadField.vue'
import FilteredSingleCardPicker, { type FilteredSingleCardItem } from './FilteredSingleCardPicker.vue'
import CardImage from '@/l12/CardImage.vue'

const emit = defineEmits<{ notice: [value: string] }>()
const arts = ref<AlternateArt[]>([])
const grants = ref<AlternateArtGrant[]>([])
const awardRules = ref<AlternateArtAwardRule[]>([])
const catalog = ref<DeckCard[]>([])
const media = ref<SiteMedia[]>([])
const products = ref<AlternateArtProduct[]>([])
const busy = ref(false)
const artsLoaded = ref(false)
const registryOpen = ref(false)
const registryBusy = ref(false)
const registryFilters = reactive({ name: '', artCode: '', baseCard: '' })
const registry = ref<AlternateArtSearchPage>({ items: [], total: 0, page: 1, pageSize: 20 })
const registryPages = computed(() => Math.max(1, Math.ceil(registry.value.total / registry.value.pageSize)))
const grantForm = reactive({ alternateArtId: '', username: '', sourceKind: 'manual' as AlternateArtGrant['sourceKind'], sourceReference: '' })
const artForm = reactive({ id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', productId: '', active: true })
const productForm = reactive({ id: '', name: '', active: true })
const ruleForm = reactive({ id: '', alternateArtId: '', kind: 'rank-reached' as AlternateArtAwardRule['kind'], seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true })
const eventUsernames = ref('')
const betaGrantForm = reactive({ alternateArtId: '', seasonId: '' })
const betaGrantPreview = ref<AlternateArtRankedParticipantDispatchPreview | null>(null)
const rankedParticipantScope = ref<'current' | 'specified'>('current')
const artPickerTarget = ref<'base' | 'grant' | 'participants' | 'rule' | null>(null)
const cardById = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const selectedArt = computed(() => arts.value.find(item => item.id === grantForm.alternateArtId))
const selectedParticipantArt = computed(() => arts.value.find(item => item.id === betaGrantForm.alternateArtId))
const selectedRuleArt = computed(() => arts.value.find(item => item.id === ruleForm.alternateArtId))
const basePickerItems = computed<FilteredSingleCardItem[]>(() => catalog.value.map(card => ({ id: card.id, cardId: card.id,
  number: card.number, name: card.nameZh, cardImageId: card.id, cardType: card.cardType, faction: card.faction,
  product: card.product, cost: card.cost })))
const artPickerItems = computed<FilteredSingleCardItem[]>(() => arts.value.filter(art => art.active).map(art => {
  const card = cardById.value.get(art.baseCardId)
  return { id: art.id, cardId: art.baseCardId, number: art.artCode, name: art.displayName,
    imageUrl: art.builtIn ? undefined : art.thumbnailUrl, cardImageId: art.builtIn ? art.cardImageId : undefined,
    cardType: card?.cardType, faction: card?.faction, product: art.productName || card?.product,
    subtitle: `${art.artCode} · ${card?.nameZh || art.baseCardId}` }
}))
const selectedBaseCard = computed(() => cardById.value.get(artForm.baseCardId))
function notice(value: string) { emit('notice', value) }
async function ensureArtsLoaded() {
  if (artsLoaded.value) return
  arts.value = await adminApi.alternateArts()
  artsLoaded.value = true
}
async function openArtPicker(target: 'base' | 'grant' | 'participants' | 'rule') {
  if (target !== 'base') {
    try { await ensureArtsLoaded() }
    catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败'); return }
  }
  artPickerTarget.value = target
}
async function searchRegistry(page = 1) {
  registryBusy.value = true
  try {
    registry.value = await adminApi.searchAlternateArts({ ...registryFilters, page, pageSize: registry.value.pageSize, includeInactive: true })
  } catch (error) { notice(error instanceof Error ? error.message : '已登记异画查询失败') }
  finally { registryBusy.value = false }
}
async function openRegistry() { registryOpen.value = true; await searchRegistry(1) }
function resetRegistryFilters() { Object.assign(registryFilters, { name: '', artCode: '', baseCard: '' }); void searchRegistry(1) }
function choosePickerItem(item: FilteredSingleCardItem) {
  if (artPickerTarget.value === 'base') {
    artForm.baseCardId = item.cardId; artForm.displayName = item.name; artPickerTarget.value = null; return
  }
  const art = arts.value.find(row => row.id === item.id)
  if (!art) return
  if (artPickerTarget.value === 'grant') grantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'participants') betaGrantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'rule') ruleForm.alternateArtId = art.id
  betaGrantPreview.value = null
  artPickerTarget.value = null
}
function resetArt() { Object.assign(artForm, { id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', productId: '', active: true }) }
function editArt(art: AlternateArt) { if (!art.builtIn) Object.assign(artForm, art) }
function editRegistryArt(art: AlternateArt) { editArt(art); registryOpen.value = false }
function resetProduct() { Object.assign(productForm, { id: '', name: '', active: true }) }
function editProduct(product: AlternateArtProduct) { Object.assign(productForm, product) }
function uploaded(item: SiteMedia) { media.value = [item, ...media.value.filter(row => row.id !== item.id)]; artForm.mediaAssetId = item.id }
function resetRule() { Object.assign(ruleForm, { id: '', alternateArtId: '', kind: 'rank-reached', seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true }) }
function editRule(rule: AlternateArtAwardRule) { Object.assign(ruleForm, rule) }
async function load() {
  busy.value = true
  try {
    const [nextGrants, nextCatalog, nextMedia, nextRules, nextProducts] = await Promise.all([
      adminApi.alternateArtGrants(), loadDeckCatalog(), adminApi.siteMedia('card-art'), adminApi.alternateArtAwardRules(), adminApi.alternateArtProducts(),
    ])
    grants.value = nextGrants; catalog.value = nextCatalog; media.value = nextMedia; awardRules.value = nextRules; products.value = nextProducts
  } catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败') }
  finally { busy.value = false }
}
async function saveProduct() {
  try {
    const saved = await adminApi.saveAlternateArtProduct({ ...productForm, id: productForm.id || undefined })
    products.value = [saved, ...products.value.filter(item => item.id !== saved.id)]
    artForm.productId = saved.id
    resetProduct()
    notice('异画归属产品已保存，可在异画登记时选用')
  } catch (error) { notice(error instanceof Error ? error.message : '异画归属产品保存失败') }
}
async function saveArt() {
  try {
    const base = cardById.value.get(artForm.baseCardId)
    if (!base) { notice('请先通过单卡筛选选择原卡'); return }
    artForm.displayName = base.nameZh
    const saved = await adminApi.saveAlternateArt({ ...artForm, id: artForm.id || undefined })
    arts.value = [saved, ...arts.value.filter(item => item.id !== saved.id)]
    grantForm.alternateArtId ||= saved.id
    notice(saved.active ? '异画已保存并立即进入画廊；获得权益的玩家可在构筑中选用' : '异画已保存为停用状态，不会在画廊展示')
    resetArt()
    if (registryOpen.value) await searchRegistry(registry.value.page)
  } catch (error) { notice(error instanceof Error ? error.message : '异画保存失败') }
}
async function grant() {
  if (!grantForm.alternateArtId || !grantForm.username.trim()) { notice('请选择异画并填写玩家账号'); return }
  try { const saved = await adminApi.grantAlternateArt(grantForm); grants.value = [saved, ...grants.value.filter(item => item.id !== saved.id)]; notice('异画权益已派发，玩家下次刷新构筑即可选用') }
  catch (error) { notice(error instanceof Error ? error.message : '异画权益派发失败') }
}
async function revoke(grant: AlternateArtGrant) {
  if (!window.confirm(`撤回 ${grant.username} 的异画权益？现有构筑将自动回退为原卡图。`)) return
  try { await adminApi.revokeAlternateArtGrant(grant.id); grants.value = grants.value.map(item => item.id === grant.id ? { ...item, revokedAt: new Date().toISOString() } : item); notice('异画权益已撤回') }
  catch (error) { notice(error instanceof Error ? error.message : '撤回失败') }
}
async function saveRule() {
  if (!ruleForm.alternateArtId) { notice('请先选择要派发的异画'); return }
  try {
    const saved = await adminApi.saveAlternateArtAwardRule({ ...ruleForm, id: ruleForm.id || undefined })
    awardRules.value = [saved, ...awardRules.value.filter(rule => rule.id !== saved.id)]
    notice(saved.kind === 'event' ? '活动异画规则已保存，可在下方粘贴玩家名单执行派发' : '赛季异画规则已保存，将由结算自动派发')
    resetRule()
  } catch (error) { notice(error instanceof Error ? error.message : '异画规则保存失败') }
}
async function dispatchEvent(rule: AlternateArtAwardRule) {
  const usernames = eventUsernames.value.split(/[\s,，;；]+/).map(value => value.trim()).filter(Boolean)
  if (!usernames.length) { notice('请先填写活动获奖玩家账号，每行一个或用逗号分隔'); return }
  if (!window.confirm(`向 ${usernames.length} 名玩家派发活动异画？`)) return
  try {
    const created = await adminApi.dispatchAlternateArtEvent(rule.id, usernames)
    grants.value = [...created, ...grants.value.filter(item => !created.some(saved => saved.id === item.id))]
    eventUsernames.value = ''
    notice(`已完成活动派发：${created.length} 名玩家`)
  } catch (error) { notice(error instanceof Error ? error.message : '活动异画派发失败') }
}
async function previewBetaGrant() {
  if (!betaGrantForm.alternateArtId) { notice('请先选择要派发的异画'); return }
  if (rankedParticipantScope.value === 'specified' && !betaGrantForm.seasonId.trim()) { notice('请填写指定赛季编号'); return }
  try { betaGrantPreview.value = await adminApi.previewAlternateArtRankedParticipants({
    alternateArtId: betaGrantForm.alternateArtId,
    seasonId: rankedParticipantScope.value === 'specified' ? betaGrantForm.seasonId.trim() : undefined,
  }) }
  catch (error) { notice(error instanceof Error ? error.message : '排位参与者预览失败') }
}
async function dispatchBetaGrant() {
  if (!betaGrantPreview.value || !betaGrantForm.alternateArtId) { notice('请先执行预览并核对人数'); return }
  if (!window.confirm(`将向 ${betaGrantPreview.value.toGrant} 名排位参与者派发异画；已拥有同来源权益的 ${betaGrantPreview.value.alreadyGranted} 人不会重复派发。确认继续？`)) return
  try {
    const saved = await adminApi.dispatchAlternateArtRankedParticipants({
      alternateArtId: betaGrantForm.alternateArtId,
      seasonId: rankedParticipantScope.value === 'specified' ? betaGrantForm.seasonId.trim() : undefined,
    })
    grants.value = [...saved, ...grants.value.filter(item => !saved.some(row => row.id === item.id))]
    notice(`已完成 ${betaGrantPreview.value.seasonId} 赛季排位参与者派发：${betaGrantPreview.value.toGrant} 名新增权益`)
    betaGrantPreview.value = null
  } catch (error) { notice(error instanceof Error ? error.message : '排位参与者派发失败') }
}
onMounted(load)
</script>

<template>
  <section class="alternate-admin">
    <header><div><small>ALTERNATE ART</small><h3>异画与玩家权益</h3><p>异画不改变卡牌编号、效果或构筑合法性。先上传并绑定原卡，再授予玩家；派发来源会写入审计记录。</p></div><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button></header>
    <section class="product-editor"><h4>异画归属产品</h4><p>产品独立于站点商品，可先在此新建，再绑定一张或多张异画。</p><div class="form-grid"><label>产品名称<input v-model.trim="productForm.name" maxlength="100" placeholder="例如：S01 赛季典藏"></label><label class="check"><input v-model="productForm.active" type="checkbox">可继续绑定</label></div><div class="actions"><button @click="resetProduct">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveProduct">保存归属产品</button></div><article v-for="product in products" :key="product.id"><span>{{ product.name }} · {{ product.active ? '可用' : '已停用' }}</span><button @click="editProduct(product)">编辑</button></article></section>
    <section class="art-editor"><h4>{{ artForm.id ? '编辑异画' : '新建异画' }}</h4><p>异画卡名始终沿用原卡；启用后保存即进入玩家画廊，不需要再走资讯或站点内容发布。</p><div class="form-grid"><label>异画编号<input v-model.trim="artForm.artCode" maxlength="80" placeholder="例如：ALT-S01-001"></label><label>绑定原卡<button class="art-choice" type="button" @click="openArtPicker('base')">{{ selectedBaseCard ? `${selectedBaseCard.number} · ${selectedBaseCard.nameZh}` : '打开单卡筛选选择原卡' }}</button></label><label>卡名（随原卡，不可单独修改）<input :value="selectedBaseCard?.nameZh || '选择原卡后自动带入'" disabled></label><label>归属产品（复用已有）<select v-model="artForm.productId"><option value="">暂不归属产品</option><option v-for="item in products.filter(product => product.active || product.id === artForm.productId)" :key="item.id" :value="item.id">{{ item.name }}</option></select></label><label>异画素材<select v-model="artForm.mediaAssetId"><option value="">先上传或选择素材</option><option v-for="item in media" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }}</option></select></label><label class="check"><input v-model="artForm.active" type="checkbox">启用并在画廊展示</label></div><MediaUploadField v-if="hasPermission('admin.content.draft')" kind="card-art" :initial-alt="selectedBaseCard?.nameZh || '异画卡图'" @uploaded="uploaded" @notice="notice"/><div class="actions"><button @click="resetArt">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveArt">保存并同步画廊</button></div></section>
    <section class="art-list art-registry-entry"><div><h4>已登记异画</h4><p>按需打开查询，不在进入页面时载入全部异画。</p></div><button type="button" @click="openRegistry">查看已登记异画</button></section>
    <section class="grant-panel"><h4>直接派发（兜底）</h4><p>赛季达段、赛季结算和活动派发将使用同一权益记录；此处可处理补发、修正与活动名单。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('grant')">{{ selectedArt ? `${selectedArt.artCode} · ${selectedArt.displayName}` : '打开异画卡查选择' }}</button></label><label>玩家账号<input v-model.trim="grantForm.username" placeholder="精确用户名"></label><label>派发来源<select v-model="grantForm.sourceKind"><option value="manual">后台直接派发</option><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="event">活动派发</option></select></label><label>来源备注<input v-model.trim="grantForm.sourceReference" maxlength="240" :placeholder="selectedArt ? `例如：${selectedArt.displayName} 补发` : '赛季编号、活动编号或处理说明'"></label></div><button v-if="hasPermission('admin.content.draft')" class="primary" @click="grant">派发异画权益</button></section>
    <section class="award-rules"><h4>自动派发规则</h4><p>“达到段位”在排位结算后即时检查；“赛季结算”和“最强主宰”在后台归档旧赛季时自动执行；活动规则需由管理员在名单确认后执行。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('rule')">{{ selectedRuleArt ? `${selectedRuleArt.artCode} · ${selectedRuleArt.displayName}` : '打开异画卡查选择' }}</button></label><label>规则类型<select v-model="ruleForm.kind"><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="master-champion-season-final">赛季最强主宰</option><option value="event">活动派发</option></select></label><label v-if="ruleForm.kind !== 'event'">赛季编号<input v-model.trim="ruleForm.seasonId" maxlength="100" placeholder="例如：S01"></label><label v-else>活动编号<input v-model.trim="ruleForm.eventId" maxlength="160" placeholder="例如：2026-国庆活动"></label><label v-if="ruleForm.kind === 'master-champion-season-final'">指定主宰<select v-model="ruleForm.masterId"><option value="">所有最强主宰得主</option><option v-for="card in catalog.filter(item => item.cardType === 'master')" :key="card.id" :value="card.id">{{ card.nameZh }}</option></select></label><label v-if="ruleForm.kind === 'rank-reached' || ruleForm.kind === 'season-final'">最低段位<select v-model.number="ruleForm.minimumTierIndex"><option v-for="index in 5" :key="index - 1" :value="index - 1">第 {{ index }} 档及以上</option></select></label><label class="check"><input v-model="ruleForm.active" type="checkbox">启用此规则</label></div><div class="actions"><button @click="resetRule">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveRule">保存规则</button></div><article v-for="rule in awardRules" :key="rule.id" :class="{ inactive: !rule.active }"><div><b>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }}</b><span>{{ rule.kind === 'rank-reached' ? `赛季达段 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'season-final' ? `赛季结算 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'master-champion-season-final' ? `赛季最强主宰 · ${rule.seasonId} · ${rule.masterId || '全部主宰'}` : `活动派发 · ${rule.eventId}` }}</span><small>{{ rule.active ? '已启用' : '已停用' }}</small></div><button @click="editRule(rule)">编辑</button></article><p v-if="!awardRules.length">尚未设置自动派发规则。</p></section>
    <section class="event-dispatch"><h4>赛季排位参与者派发</h4><p>选择异画后先预览。默认只覆盖本赛季参与过排位的玩家；也可明确填写一个历史赛季。确认后同一异画、同一赛季只会派发一次。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('participants')">{{ selectedParticipantArt ? `${selectedParticipantArt.artCode} · ${selectedParticipantArt.displayName}` : '打开异画卡查选择' }}</button></label><label>派发范围<select v-model="rankedParticipantScope" @change="betaGrantPreview = null"><option value="current">本赛季参与过排位的玩家</option><option value="specified">指定赛季参与过排位的玩家</option></select></label><label v-if="rankedParticipantScope === 'specified'">指定赛季<input v-model.trim="betaGrantForm.seasonId" maxlength="100" placeholder="例如：S01" @input="betaGrantPreview = null"></label></div><p v-if="betaGrantPreview">{{ betaGrantPreview.seasonId }} 赛季可参与 {{ betaGrantPreview.eligibleAccounts }} 人；已拥有同来源权益 {{ betaGrantPreview.alreadyGranted }} 人；本次新增 {{ betaGrantPreview.toGrant }} 人。</p><div class="actions"><button @click="previewBetaGrant">预览派发范围</button><button v-if="betaGrantPreview && hasPermission('admin.content.draft')" class="primary" @click="dispatchBetaGrant">确认派发</button></div></section>
    <section v-if="awardRules.some(rule => rule.active && rule.kind === 'event')" class="event-dispatch"><h4>执行活动名单</h4><p>确认活动获奖名单后粘贴精确玩家账号；每行一个，也可用逗号分隔。系统会逐名校验账号并写入派发记录。</p><textarea v-model="eventUsernames" rows="5" placeholder="玩家账号 1&#10;玩家账号 2"></textarea><div v-for="rule in awardRules.filter(rule => rule.active && rule.kind === 'event')" :key="rule.id" class="event-rule"><span>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }} · {{ rule.eventId }}</span><button v-if="hasPermission('admin.content.draft')" class="primary" @click="dispatchEvent(rule)">按此规则派发</button></div></section>
    <section class="grant-list"><h4>派发记录</h4><article v-for="grantRow in grants" :key="grantRow.id" :class="{ revoked: grantRow.revokedAt }"><div><b>{{ grantRow.username }}</b><span>{{ arts.find(art => art.id === grantRow.alternateArtId)?.displayName || grantRow.alternateArtId }}</span><small>{{ grantRow.sourceKind }}{{ grantRow.sourceReference ? ` · ${grantRow.sourceReference}` : '' }} · {{ new Date(grantRow.grantedAt).toLocaleString() }}</small></div><button v-if="!grantRow.revokedAt && hasPermission('admin.content.draft')" class="danger" @click="revoke(grantRow)">撤回</button><em v-else-if="grantRow.revokedAt">已撤回</em></article><p v-if="!grants.length">暂无派发记录。</p></section>
    <Teleport to="body">
      <div v-if="registryOpen" class="registry-mask" @click.self="registryOpen = false">
        <section class="registry-modal" role="dialog" aria-modal="true" aria-labelledby="alternate-art-registry-title">
          <header><div><small>ALTERNATE ART REGISTRY</small><h2 id="alternate-art-registry-title">已登记异画</h2></div><button type="button" aria-label="关闭" @click="registryOpen = false">×</button></header>
          <form class="registry-filters" @submit.prevent="searchRegistry(1)"><label>名称<input v-model.trim="registryFilters.name" placeholder="异画名称"></label><label>编号<input v-model.trim="registryFilters.artCode" placeholder="异画编号"></label><label>绑定卡<input v-model.trim="registryFilters.baseCard" placeholder="原卡名或编号"></label><button type="button" @click="resetRegistryFilters">重置</button><button class="primary" type="submit">查询</button></form>
          <div class="registry-results" :aria-busy="registryBusy">
            <article v-for="art in registry.items" :key="art.id"><CardImage v-if="art.builtIn" :card-id="art.cardImageId" :alt="art.displayName" intent="thumb"/><img v-else :src="art.thumbnailUrl" :alt="art.displayName"><div><b>{{ art.displayName }}</b><span>{{ art.artCode || '待补异画编号' }} · {{ art.baseCardId }} · {{ art.baseCardName || cardById.get(art.baseCardId)?.nameZh || '原卡资料加载中' }}</span><small>{{ art.productName || '未归属产品' }} · {{ art.builtIn ? '内置异画，已在画廊展示，可授予' : art.active ? '已在画廊展示，可授予' : '已停用，不展示不再授予' }}</small></div><button v-if="!art.builtIn" type="button" @click="editRegistryArt(art)">编辑</button></article>
            <p v-if="!registryBusy && !registry.items.length">没有符合条件的已登记异画。</p>
          </div>
          <footer><span>共 {{ registry.total }} 项 · 第 {{ registry.page }} / {{ registryPages }} 页</span><div><button type="button" :disabled="registryBusy || registry.page <= 1" @click="searchRegistry(registry.page - 1)">上一页</button><button type="button" :disabled="registryBusy || registry.page >= registryPages" @click="searchRegistry(registry.page + 1)">下一页</button></div></footer>
        </section>
      </div>
      <FilteredSingleCardPicker v-if="artPickerTarget" :title="artPickerTarget === 'base' ? '选择异画绑定的原卡' : '选择异画'" :items="artPickerTarget === 'base' ? basePickerItems : artPickerItems" @select="choosePickerItem" @close="artPickerTarget = null"/>
    </Teleport>
  </section>
</template>

<style scoped>
.alternate-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.alternate-admin>header{display:flex;justify-content:space-between;gap:18px;margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.alternate-admin h3,.alternate-admin h4{margin:5px 0 10px}.alternate-admin p,.alternate-admin span,.alternate-admin small{color:#93a0a6;font-size:14px;line-height:1.65}.alternate-admin small{color:#56c4cc;font-family:monospace;letter-spacing:.08em}.alternate-admin button,.alternate-admin input,.alternate-admin select{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.art-editor,.art-list,.grant-panel,.grant-list,.product-editor{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{display:grid;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.form-grid .check{display:flex;align-items:center;gap:8px}.actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.primary{border-color:#d8ba68!important;background:#d8ba68!important;color:#080b0d!important;font-weight:900}.art-list article,.grant-list article,.product-editor article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.art-list img,.art-list :deep(.l12-card-image){width:46px;height:64px;flex:0 0 46px;object-fit:cover}.art-list article div,.grant-list article div{display:grid;min-width:0;gap:3px;flex:1}.art-list button,.grant-list button,.product-editor button{margin-left:auto}.grant-panel>p{margin-top:-4px}.grant-list article.revoked{opacity:.58}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}@media(max-width:760px){.alternate-admin>header{align-items:flex-start;flex-direction:column}.form-grid{grid-template-columns:1fr}}
.award-rules,.event-dispatch{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.award-rules article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.award-rules article div{display:grid;min-width:0;gap:3px;flex:1}.award-rules article.inactive{opacity:.58}.event-dispatch textarea{box-sizing:border-box;width:100%;margin:8px 0;padding:10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:14px/1.5 ui-monospace,monospace}.event-rule{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:8px;padding:9px;border:1px solid #35424a}.event-rule span{color:#c4ccce}.art-choice{text-align:left;font-weight:800}.art-picker-mask{position:fixed;z-index:3400;inset:0;display:grid;padding:36px;background:#020507dc;place-items:center}.art-picker{display:grid;width:min(1080px,96vw);height:min(780px,90vh);grid-template-rows:auto auto minmax(0,1fr);border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.art-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.art-picker h2{margin:4px 0 0;font-size:22px}.art-picker small{color:#cfad43;font:900 12px monospace;letter-spacing:.08em}.art-picker>header button{border:0;background:transparent;color:#fff;font-size:28px}.art-picker>input{margin:12px;padding:10px;border:1px solid #445159;background:#070c10;color:#fff;font-weight:800}.art-picker-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.art-picker-card{display:grid;gap:6px;padding:8px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.art-picker-card:hover{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.art-picker-card img{width:100%;aspect-ratio:5/7;object-fit:cover;background:#050708}.art-picker-card b,.art-picker-card small,.art-picker-card span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.art-picker-card span{color:#93a0a6;font-size:12px}@media(max-width:760px){.art-picker-mask{padding:8px}.art-picker{width:100%;height:96vh}.art-picker-grid{grid-template-columns:repeat(auto-fill,minmax(110px,1fr))}}
.art-registry-entry{display:flex;align-items:center;justify-content:space-between;gap:18px}.art-registry-entry h4,.art-registry-entry p{margin:0}.registry-mask{position:fixed;z-index:3500;inset:0;display:grid;place-items:center;padding:32px;background:#020507df}.registry-modal{display:grid;width:min(1040px,94vw);height:min(760px,90vh);grid-template-rows:auto auto minmax(0,1fr) auto;border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.registry-modal>header,.registry-modal>footer{display:flex;align-items:center;justify-content:space-between;gap:14px;padding:14px 18px;border-bottom:1px solid #374147}.registry-modal>header h2{margin:4px 0 0}.registry-modal>header small{color:#cfad43;font:900 12px monospace;letter-spacing:.08em}.registry-modal>header button{border:0;background:transparent;color:#fff;font-size:28px}.registry-filters{display:grid;grid-template-columns:repeat(3,minmax(0,1fr)) auto auto;align-items:end;gap:8px;padding:12px 18px;border-bottom:1px solid #283238}.registry-filters label{display:grid;gap:5px;color:#aeb8bb;font-size:13px;font-weight:900}.registry-filters input,.registry-filters button,.registry-modal>footer button{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff}.registry-results{overflow:auto;padding:12px 18px}.registry-results article{display:grid;grid-template-columns:46px minmax(0,1fr) auto;align-items:center;gap:12px;margin-bottom:8px;padding:9px;border:1px solid #35424a;background:#101820}.registry-results img,.registry-results :deep(.l12-card-image){width:46px;height:64px;object-fit:cover}.registry-results article>div{display:grid;gap:3px}.registry-results span,.registry-results small{color:#93a0a6;font-size:13px}.registry-results article>button{min-height:34px;padding:7px 10px;border:1px solid #4a5860;background:#070d12;color:#fff}.registry-modal>footer{border-top:1px solid #374147;border-bottom:0}.registry-modal>footer span{color:#93a0a6}.registry-modal>footer div{display:flex;gap:8px}.registry-modal button:disabled{cursor:not-allowed;opacity:.42}@media(max-width:760px){.art-registry-entry{align-items:stretch;flex-direction:column}.registry-mask{padding:8px}.registry-modal{width:100%;height:96dvh}.registry-filters{grid-template-columns:1fr 1fr}.registry-filters label:first-child{grid-column:1/-1}.registry-results{padding:9px}.registry-results article{grid-template-columns:38px minmax(0,1fr)}.registry-results img,.registry-results :deep(.l12-card-image){width:38px;height:54px}.registry-results article>button{grid-column:1/-1}.registry-modal>footer{align-items:flex-start;flex-direction:column}.registry-modal>footer div,.registry-modal>footer button{width:100%}}
</style>
