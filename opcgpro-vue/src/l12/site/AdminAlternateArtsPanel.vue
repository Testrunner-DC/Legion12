<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type AlternateArt, type AlternateArtAwardRule, type AlternateArtGrant, type AlternateArtProduct, type AlternateArtRankedParticipantDispatchPreview, type SiteMedia } from '@/l12/platform'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import MediaUploadField from './MediaUploadField.vue'

const emit = defineEmits<{ notice: [value: string] }>()
const arts = ref<AlternateArt[]>([])
const grants = ref<AlternateArtGrant[]>([])
const awardRules = ref<AlternateArtAwardRule[]>([])
const catalog = ref<DeckCard[]>([])
const media = ref<SiteMedia[]>([])
const products = ref<AlternateArtProduct[]>([])
const busy = ref(false)
const grantForm = reactive({ alternateArtId: '', username: '', sourceKind: 'manual' as AlternateArtGrant['sourceKind'], sourceReference: '' })
const artForm = reactive({ id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', productId: '', active: true })
const productForm = reactive({ id: '', name: '', active: true })
const ruleForm = reactive({ id: '', alternateArtId: '', kind: 'rank-reached' as AlternateArtAwardRule['kind'], seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true })
const eventUsernames = ref('')
const betaGrantForm = reactive({ alternateArtId: '', seasonId: '' })
const betaGrantPreview = ref<AlternateArtRankedParticipantDispatchPreview | null>(null)
const rankedParticipantScope = ref<'current' | 'specified'>('current')
const artPickerTarget = ref<'grant' | 'participants' | 'rule' | null>(null)
const artPickerQuery = ref('')
const cardById = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const selectedArt = computed(() => arts.value.find(item => item.id === grantForm.alternateArtId))
const selectedParticipantArt = computed(() => arts.value.find(item => item.id === betaGrantForm.alternateArtId))
const selectedRuleArt = computed(() => arts.value.find(item => item.id === ruleForm.alternateArtId))
const pickerArts = computed(() => {
  const keyword = artPickerQuery.value.trim().toLocaleLowerCase('zh-CN')
  return arts.value.filter(art => art.active && (!keyword || [art.artCode, art.displayName, art.baseCardId,
    art.productName, cardById.value.get(art.baseCardId)?.nameZh].some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))))
    .sort((left, right) => left.artCode.localeCompare(right.artCode, 'zh-CN'))
})
function notice(value: string) { emit('notice', value) }
function openArtPicker(target: 'grant' | 'participants' | 'rule') { artPickerQuery.value = ''; artPickerTarget.value = target }
function chooseArt(art: AlternateArt) {
  if (artPickerTarget.value === 'grant') grantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'participants') betaGrantForm.alternateArtId = art.id
  else if (artPickerTarget.value === 'rule') ruleForm.alternateArtId = art.id
  betaGrantPreview.value = null
  artPickerTarget.value = null
}
function resetArt() { Object.assign(artForm, { id: '', artCode: '', baseCardId: '', displayName: '', mediaAssetId: '', productId: '', active: true }) }
function editArt(art: AlternateArt) { Object.assign(artForm, art) }
function resetProduct() { Object.assign(productForm, { id: '', name: '', active: true }) }
function editProduct(product: AlternateArtProduct) { Object.assign(productForm, product) }
function uploaded(item: SiteMedia) { media.value = [item, ...media.value.filter(row => row.id !== item.id)]; artForm.mediaAssetId = item.id }
function resetRule() { Object.assign(ruleForm, { id: '', alternateArtId: '', kind: 'rank-reached', seasonId: '', eventId: '', masterId: '', minimumTierIndex: 0, active: true }) }
function editRule(rule: AlternateArtAwardRule) { Object.assign(ruleForm, rule) }
async function load() {
  busy.value = true
  try {
    const [nextArts, nextGrants, nextCatalog, nextMedia, nextRules, nextProducts] = await Promise.all([
      adminApi.alternateArts(), adminApi.alternateArtGrants(), loadDeckCatalog(), adminApi.siteMedia('card-art'), adminApi.alternateArtAwardRules(), adminApi.alternateArtProducts(),
    ])
    arts.value = nextArts; grants.value = nextGrants; catalog.value = nextCatalog; media.value = nextMedia; awardRules.value = nextRules; products.value = nextProducts
  } catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败') }
  finally { busy.value = false }
}
async function saveProduct() {
  try {
    const saved = await adminApi.saveAlternateArtProduct({ ...productForm, id: productForm.id || undefined })
    products.value = [saved, ...products.value.filter(item => item.id !== saved.id)]
    artForm.productId ||= saved.id
    resetProduct()
    notice('异画归属产品已保存，可在异画登记时选用')
  } catch (error) { notice(error instanceof Error ? error.message : '异画归属产品保存失败') }
}
async function saveArt() {
  try {
    const saved = await adminApi.saveAlternateArt({ ...artForm, id: artForm.id || undefined })
    arts.value = [saved, ...arts.value.filter(item => item.id !== saved.id)]
    grantForm.alternateArtId ||= saved.id
    notice('异画已保存；只有被授予权益的玩家才可以在构筑中选用')
    resetArt()
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
    <section class="art-editor"><h4>{{ artForm.id ? '编辑异画' : '新建异画' }}</h4><div class="form-grid"><label>异画编号<input v-model.trim="artForm.artCode" maxlength="80" placeholder="例如：ALT-S01-001"></label><label>原卡<select v-model="artForm.baseCardId"><option value="">选择原卡</option><option v-for="card in catalog" :key="card.id" :value="card.id">{{ card.id }} · {{ card.nameZh }}</option></select></label><label>异画名称<input v-model.trim="artForm.displayName" maxlength="100" placeholder="例如：第 1 赛季典藏异画"></label><label>归属产品<select v-model="artForm.productId"><option value="">暂不归属产品</option><option v-for="item in products.filter(product => product.active || product.id === artForm.productId)" :key="item.id" :value="item.id">{{ item.name }}</option></select></label><label>异画素材<select v-model="artForm.mediaAssetId"><option value="">先上传或选择素材</option><option v-for="item in media" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }}</option></select></label><label class="check"><input v-model="artForm.active" type="checkbox">允许新授予与选用</label></div><MediaUploadField v-if="hasPermission('admin.content.draft')" kind="card-art" :initial-alt="artForm.displayName" @uploaded="uploaded" @notice="notice"/><div class="actions"><button @click="resetArt">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveArt">保存并公开展示异画</button></div></section>
    <section class="art-list"><h4>已登记异画</h4><article v-for="art in arts" :key="art.id"><img :src="art.thumbnailUrl" :alt="art.displayName"><div><b>{{ art.displayName }}</b><span>{{ art.artCode || '待补异画编号' }} · {{ art.baseCardId }} · {{ cardById.get(art.baseCardId)?.nameZh || '原卡资料加载中' }}</span><small>{{ art.productName || '未归属产品' }} · {{ art.active ? '已在画廊展示，可授予' : '已停用，不展示不再授予' }}</small></div><button @click="editArt(art)">编辑</button></article><p v-if="!arts.length">尚未登记异画。</p></section>
    <section class="grant-panel"><h4>直接派发（兜底）</h4><p>赛季达段、赛季结算和活动派发将使用同一权益记录；此处可处理补发、修正与活动名单。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('grant')">{{ selectedArt ? `${selectedArt.artCode} · ${selectedArt.displayName}` : '打开异画卡查选择' }}</button></label><label>玩家账号<input v-model.trim="grantForm.username" placeholder="精确用户名"></label><label>派发来源<select v-model="grantForm.sourceKind"><option value="manual">后台直接派发</option><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="event">活动派发</option></select></label><label>来源备注<input v-model.trim="grantForm.sourceReference" maxlength="240" :placeholder="selectedArt ? `例如：${selectedArt.displayName} 补发` : '赛季编号、活动编号或处理说明'"></label></div><button v-if="hasPermission('admin.content.draft')" class="primary" @click="grant">派发异画权益</button></section>
    <section class="award-rules"><h4>自动派发规则</h4><p>“达到段位”在排位结算后即时检查；“赛季结算”和“最强主宰”在后台归档旧赛季时自动执行；活动规则需由管理员在名单确认后执行。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('rule')">{{ selectedRuleArt ? `${selectedRuleArt.artCode} · ${selectedRuleArt.displayName}` : '打开异画卡查选择' }}</button></label><label>规则类型<select v-model="ruleForm.kind"><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="master-champion-season-final">赛季最强主宰</option><option value="event">活动派发</option></select></label><label v-if="ruleForm.kind !== 'event'">赛季编号<input v-model.trim="ruleForm.seasonId" maxlength="100" placeholder="例如：S01"></label><label v-else>活动编号<input v-model.trim="ruleForm.eventId" maxlength="160" placeholder="例如：2026-国庆活动"></label><label v-if="ruleForm.kind === 'master-champion-season-final'">指定主宰<select v-model="ruleForm.masterId"><option value="">所有最强主宰得主</option><option v-for="card in catalog.filter(item => item.cardType === 'master')" :key="card.id" :value="card.id">{{ card.nameZh }}</option></select></label><label v-if="ruleForm.kind === 'rank-reached' || ruleForm.kind === 'season-final'">最低段位<select v-model.number="ruleForm.minimumTierIndex"><option v-for="index in 5" :key="index - 1" :value="index - 1">第 {{ index }} 档及以上</option></select></label><label class="check"><input v-model="ruleForm.active" type="checkbox">启用此规则</label></div><div class="actions"><button @click="resetRule">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveRule">保存规则</button></div><article v-for="rule in awardRules" :key="rule.id" :class="{ inactive: !rule.active }"><div><b>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }}</b><span>{{ rule.kind === 'rank-reached' ? `赛季达段 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'season-final' ? `赛季结算 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'master-champion-season-final' ? `赛季最强主宰 · ${rule.seasonId} · ${rule.masterId || '全部主宰'}` : `活动派发 · ${rule.eventId}` }}</span><small>{{ rule.active ? '已启用' : '已停用' }}</small></div><button @click="editRule(rule)">编辑</button></article><p v-if="!awardRules.length">尚未设置自动派发规则。</p></section>
    <section class="event-dispatch"><h4>赛季排位参与者派发</h4><p>选择异画后先预览。默认只覆盖本赛季参与过排位的玩家；也可明确填写一个历史赛季。确认后同一异画、同一赛季只会派发一次。</p><div class="form-grid"><label>异画<button class="art-choice" type="button" @click="openArtPicker('participants')">{{ selectedParticipantArt ? `${selectedParticipantArt.artCode} · ${selectedParticipantArt.displayName}` : '打开异画卡查选择' }}</button></label><label>派发范围<select v-model="rankedParticipantScope" @change="betaGrantPreview = null"><option value="current">本赛季参与过排位的玩家</option><option value="specified">指定赛季参与过排位的玩家</option></select></label><label v-if="rankedParticipantScope === 'specified'">指定赛季<input v-model.trim="betaGrantForm.seasonId" maxlength="100" placeholder="例如：S01" @input="betaGrantPreview = null"></label></div><p v-if="betaGrantPreview">{{ betaGrantPreview.seasonId }} 赛季可参与 {{ betaGrantPreview.eligibleAccounts }} 人；已拥有同来源权益 {{ betaGrantPreview.alreadyGranted }} 人；本次新增 {{ betaGrantPreview.toGrant }} 人。</p><div class="actions"><button @click="previewBetaGrant">预览派发范围</button><button v-if="betaGrantPreview && hasPermission('admin.content.draft')" class="primary" @click="dispatchBetaGrant">确认派发</button></div></section>
    <section v-if="awardRules.some(rule => rule.active && rule.kind === 'event')" class="event-dispatch"><h4>执行活动名单</h4><p>确认活动获奖名单后粘贴精确玩家账号；每行一个，也可用逗号分隔。系统会逐名校验账号并写入派发记录。</p><textarea v-model="eventUsernames" rows="5" placeholder="玩家账号 1&#10;玩家账号 2"></textarea><div v-for="rule in awardRules.filter(rule => rule.active && rule.kind === 'event')" :key="rule.id" class="event-rule"><span>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }} · {{ rule.eventId }}</span><button v-if="hasPermission('admin.content.draft')" class="primary" @click="dispatchEvent(rule)">按此规则派发</button></div></section>
    <section class="grant-list"><h4>派发记录</h4><article v-for="grantRow in grants" :key="grantRow.id" :class="{ revoked: grantRow.revokedAt }"><div><b>{{ grantRow.username }}</b><span>{{ arts.find(art => art.id === grantRow.alternateArtId)?.displayName || grantRow.alternateArtId }}</span><small>{{ grantRow.sourceKind }}{{ grantRow.sourceReference ? ` · ${grantRow.sourceReference}` : '' }} · {{ new Date(grantRow.grantedAt).toLocaleString() }}</small></div><button v-if="!grantRow.revokedAt && hasPermission('admin.content.draft')" class="danger" @click="revoke(grantRow)">撤回</button><em v-else-if="grantRow.revokedAt">已撤回</em></article><p v-if="!grants.length">暂无派发记录。</p></section>
    <Teleport to="body"><div v-if="artPickerTarget" class="art-picker-mask" @click.self="artPickerTarget = null"><section class="art-picker" role="dialog" aria-modal="true" aria-label="选择异画"><header><div><small>ALTERNATE ART ARCHIVE</small><h2>选择要派发的异画</h2></div><button type="button" @click="artPickerTarget = null">×</button></header><input v-model="artPickerQuery" type="search" autofocus placeholder="搜索异画编号、名称、原卡或产品"><div class="art-picker-grid"><button v-for="art in pickerArts" :key="art.id" type="button" class="art-picker-card" @click="chooseArt(art)"><img :src="art.thumbnailUrl" :alt="art.displayName"><b>{{ art.displayName }}</b><small>{{ art.artCode }} · {{ cardById.get(art.baseCardId)?.nameZh || art.baseCardId }}</small><span>{{ art.productName || '未归属产品' }}</span></button><p v-if="!pickerArts.length">没有符合条件的可派发异画。</p></div></section></div></Teleport>
  </section>
</template>

<style scoped>
.alternate-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.alternate-admin>header{display:flex;justify-content:space-between;gap:18px;margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.alternate-admin h3,.alternate-admin h4{margin:5px 0 10px}.alternate-admin p,.alternate-admin span,.alternate-admin small{color:#93a0a6;font-size:14px;line-height:1.65}.alternate-admin small{color:#56c4cc;font-family:monospace;letter-spacing:.08em}.alternate-admin button,.alternate-admin input,.alternate-admin select{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.art-editor,.art-list,.grant-panel,.grant-list,.product-editor{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{display:grid;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.form-grid .check{display:flex;align-items:center;gap:8px}.actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.primary{border-color:#d8ba68!important;background:#d8ba68!important;color:#080b0d!important;font-weight:900}.art-list article,.grant-list article,.product-editor article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.art-list img{width:46px;height:64px;object-fit:cover}.art-list article div,.grant-list article div{display:grid;min-width:0;gap:3px;flex:1}.art-list button,.grant-list button,.product-editor button{margin-left:auto}.grant-panel>p{margin-top:-4px}.grant-list article.revoked{opacity:.58}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}@media(max-width:760px){.alternate-admin>header{align-items:flex-start;flex-direction:column}.form-grid{grid-template-columns:1fr}}
.award-rules,.event-dispatch{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.award-rules article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.award-rules article div{display:grid;min-width:0;gap:3px;flex:1}.award-rules article.inactive{opacity:.58}.event-dispatch textarea{box-sizing:border-box;width:100%;margin:8px 0;padding:10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:14px/1.5 ui-monospace,monospace}.event-rule{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:8px;padding:9px;border:1px solid #35424a}.event-rule span{color:#c4ccce}.art-choice{text-align:left;font-weight:800}.art-picker-mask{position:fixed;z-index:3400;inset:0;display:grid;padding:36px;background:#020507dc;place-items:center}.art-picker{display:grid;width:min(1080px,96vw);height:min(780px,90vh);grid-template-rows:auto auto minmax(0,1fr);border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.art-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.art-picker h2{margin:4px 0 0;font-size:22px}.art-picker small{color:#cfad43;font:900 12px monospace;letter-spacing:.08em}.art-picker>header button{border:0;background:transparent;color:#fff;font-size:28px}.art-picker>input{margin:12px;padding:10px;border:1px solid #445159;background:#070c10;color:#fff;font-weight:800}.art-picker-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.art-picker-card{display:grid;gap:6px;padding:8px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.art-picker-card:hover{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.art-picker-card img{width:100%;aspect-ratio:5/7;object-fit:cover;background:#050708}.art-picker-card b,.art-picker-card small,.art-picker-card span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.art-picker-card span{color:#93a0a6;font-size:12px}@media(max-width:760px){.art-picker-mask{padding:8px}.art-picker{width:100%;height:96vh}.art-picker-grid{grid-template-columns:repeat(auto-fill,minmax(110px,1fr))}}
</style>
