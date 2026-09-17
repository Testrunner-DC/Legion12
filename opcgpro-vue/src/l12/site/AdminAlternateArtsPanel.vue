<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type AlternateArt, type AlternateArtAwardRule, type AlternateArtGrant, type SiteMedia } from '@/l12/platform'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import MediaUploadField from './MediaUploadField.vue'

const emit = defineEmits<{ notice: [value: string] }>()
const arts = ref<AlternateArt[]>([])
const grants = ref<AlternateArtGrant[]>([])
const awardRules = ref<AlternateArtAwardRule[]>([])
const catalog = ref<DeckCard[]>([])
const media = ref<SiteMedia[]>([])
const busy = ref(false)
const grantForm = reactive({ alternateArtId: '', username: '', sourceKind: 'manual' as AlternateArtGrant['sourceKind'], sourceReference: '' })
const artForm = reactive({ id: '', baseCardId: '', displayName: '', mediaAssetId: '', active: true })
const ruleForm = reactive({ id: '', alternateArtId: '', kind: 'rank-reached' as AlternateArtAwardRule['kind'], seasonId: '', eventId: '', minimumTierIndex: 0, active: true })
const eventUsernames = ref('')
const cardById = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const selectedArt = computed(() => arts.value.find(item => item.id === grantForm.alternateArtId))
function notice(value: string) { emit('notice', value) }
function resetArt() { Object.assign(artForm, { id: '', baseCardId: '', displayName: '', mediaAssetId: '', active: true }) }
function editArt(art: AlternateArt) { Object.assign(artForm, art) }
function uploaded(item: SiteMedia) { media.value = [item, ...media.value.filter(row => row.id !== item.id)]; artForm.mediaAssetId = item.id }
function resetRule() { Object.assign(ruleForm, { id: '', alternateArtId: '', kind: 'rank-reached', seasonId: '', eventId: '', minimumTierIndex: 0, active: true }) }
function editRule(rule: AlternateArtAwardRule) { Object.assign(ruleForm, rule) }
async function load() {
  busy.value = true
  try {
    const [nextArts, nextGrants, nextCatalog, nextMedia, nextRules] = await Promise.all([
      adminApi.alternateArts(), adminApi.alternateArtGrants(), loadDeckCatalog(), adminApi.siteMedia('card-art'), adminApi.alternateArtAwardRules(),
    ])
    arts.value = nextArts; grants.value = nextGrants; catalog.value = nextCatalog; media.value = nextMedia; awardRules.value = nextRules
  } catch (error) { notice(error instanceof Error ? error.message : '异画资料读取失败') }
  finally { busy.value = false }
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
onMounted(load)
</script>

<template>
  <section class="alternate-admin">
    <header><div><small>ALTERNATE ART</small><h3>异画与玩家权益</h3><p>异画不改变卡牌编号、效果或构筑合法性。先上传并绑定原卡，再授予玩家；派发来源会写入审计记录。</p></div><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button></header>
    <section class="art-editor"><h4>{{ artForm.id ? '编辑异画' : '新建异画' }}</h4><div class="form-grid"><label>原卡<select v-model="artForm.baseCardId"><option value="">选择原卡</option><option v-for="card in catalog" :key="card.id" :value="card.id">{{ card.id }} · {{ card.nameZh }}</option></select></label><label>异画名称<input v-model.trim="artForm.displayName" maxlength="100" placeholder="例如：第 1 赛季典藏异画"></label><label>异画素材<select v-model="artForm.mediaAssetId"><option value="">先上传或选择素材</option><option v-for="item in media" :key="item.id" :value="item.id">{{ item.altText || item.contentHash.slice(0, 12) }}</option></select></label><label class="check"><input v-model="artForm.active" type="checkbox">允许新授予与选用</label></div><MediaUploadField v-if="hasPermission('admin.content.draft')" kind="card-art" :initial-alt="artForm.displayName" @uploaded="uploaded" @notice="notice"/><div class="actions"><button @click="resetArt">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveArt">保存异画</button></div></section>
    <section class="art-list"><h4>已登记异画</h4><article v-for="art in arts" :key="art.id"><img :src="art.thumbnailUrl" :alt="art.displayName"><div><b>{{ art.displayName }}</b><span>{{ art.baseCardId }} · {{ cardById.get(art.baseCardId)?.nameZh || '原卡资料加载中' }}</span><small>{{ art.active ? '可授予' : '已停用' }}</small></div><button @click="editArt(art)">编辑</button></article><p v-if="!arts.length">尚未登记异画。</p></section>
    <section class="grant-panel"><h4>直接派发（兜底）</h4><p>赛季达段、赛季结算和活动派发将使用同一权益记录；此处可处理补发、修正与活动名单。</p><div class="form-grid"><label>异画<select v-model="grantForm.alternateArtId"><option value="">选择异画</option><option v-for="art in arts.filter(item => item.active)" :key="art.id" :value="art.id">{{ art.displayName }} · {{ art.baseCardId }}</option></select></label><label>玩家账号<input v-model.trim="grantForm.username" placeholder="精确用户名"></label><label>派发来源<select v-model="grantForm.sourceKind"><option value="manual">后台直接派发</option><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="event">活动派发</option></select></label><label>来源备注<input v-model.trim="grantForm.sourceReference" maxlength="240" :placeholder="selectedArt ? `例如：${selectedArt.displayName} 补发` : '赛季编号、活动编号或处理说明'"></label></div><button v-if="hasPermission('admin.content.draft')" class="primary" @click="grant">派发异画权益</button></section>
    <section class="award-rules"><h4>自动派发规则</h4><p>“达到段位”在排位结算后即时检查；“赛季结算”在后台切换赛季、归档旧赛季时自动执行；活动规则需由管理员在名单确认后执行。</p><div class="form-grid"><label>异画<select v-model="ruleForm.alternateArtId"><option value="">选择异画</option><option v-for="art in arts" :key="art.id" :value="art.id">{{ art.displayName }} · {{ art.baseCardId }}</option></select></label><label>规则类型<select v-model="ruleForm.kind"><option value="rank-reached">赛季达到段位</option><option value="season-final">赛季结算</option><option value="event">活动派发</option></select></label><label v-if="ruleForm.kind !== 'event'">赛季编号<input v-model.trim="ruleForm.seasonId" maxlength="100" placeholder="例如：S01"></label><label v-else>活动编号<input v-model.trim="ruleForm.eventId" maxlength="160" placeholder="例如：2026-国庆活动"></label><label v-if="ruleForm.kind !== 'event'">最低段位<select v-model.number="ruleForm.minimumTierIndex"><option v-for="index in 5" :key="index - 1" :value="index - 1">第 {{ index }} 档及以上</option></select></label><label class="check"><input v-model="ruleForm.active" type="checkbox">启用此规则</label></div><div class="actions"><button @click="resetRule">清空</button><button v-if="hasPermission('admin.content.draft')" class="primary" @click="saveRule">保存规则</button></div><article v-for="rule in awardRules" :key="rule.id" :class="{ inactive: !rule.active }"><div><b>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }}</b><span>{{ rule.kind === 'rank-reached' ? `赛季达段 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : rule.kind === 'season-final' ? `赛季结算 · ${rule.seasonId} · 第 ${rule.minimumTierIndex + 1} 档及以上` : `活动派发 · ${rule.eventId}` }}</span><small>{{ rule.active ? '已启用' : '已停用' }}</small></div><button @click="editRule(rule)">编辑</button></article><p v-if="!awardRules.length">尚未设置自动派发规则。</p></section>
    <section v-if="awardRules.some(rule => rule.active && rule.kind === 'event')" class="event-dispatch"><h4>执行活动名单</h4><p>确认活动获奖名单后粘贴精确玩家账号；每行一个，也可用逗号分隔。系统会逐名校验账号并写入派发记录。</p><textarea v-model="eventUsernames" rows="5" placeholder="玩家账号 1&#10;玩家账号 2"></textarea><div v-for="rule in awardRules.filter(rule => rule.active && rule.kind === 'event')" :key="rule.id" class="event-rule"><span>{{ arts.find(art => art.id === rule.alternateArtId)?.displayName || rule.alternateArtId }} · {{ rule.eventId }}</span><button v-if="hasPermission('admin.content.draft')" class="primary" @click="dispatchEvent(rule)">按此规则派发</button></div></section>
    <section class="grant-list"><h4>派发记录</h4><article v-for="grantRow in grants" :key="grantRow.id" :class="{ revoked: grantRow.revokedAt }"><div><b>{{ grantRow.username }}</b><span>{{ arts.find(art => art.id === grantRow.alternateArtId)?.displayName || grantRow.alternateArtId }}</span><small>{{ grantRow.sourceKind }}{{ grantRow.sourceReference ? ` · ${grantRow.sourceReference}` : '' }} · {{ new Date(grantRow.grantedAt).toLocaleString() }}</small></div><button v-if="!grantRow.revokedAt && hasPermission('admin.content.draft')" class="danger" @click="revoke(grantRow)">撤回</button><em v-else-if="grantRow.revokedAt">已撤回</em></article><p v-if="!grants.length">暂无派发记录。</p></section>
  </section>
</template>

<style scoped>
.alternate-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.alternate-admin>header{display:flex;justify-content:space-between;gap:18px;margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.alternate-admin h3,.alternate-admin h4{margin:5px 0 10px}.alternate-admin p,.alternate-admin span,.alternate-admin small{color:#93a0a6;font-size:14px;line-height:1.65}.alternate-admin small{color:#56c4cc;font-family:monospace;letter-spacing:.08em}.alternate-admin button,.alternate-admin input,.alternate-admin select{box-sizing:border-box;min-height:38px;padding:8px 10px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.art-editor,.art-list,.grant-panel,.grant-list{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.form-grid label{display:grid;gap:5px;color:#c4ccce;font-size:13px;font-weight:800}.form-grid .check{display:flex;align-items:center;gap:8px}.actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.primary{border-color:#d8ba68!important;background:#d8ba68!important;color:#080b0d!important;font-weight:900}.art-list article,.grant-list article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.art-list img{width:46px;height:64px;object-fit:cover}.art-list article div,.grant-list article div{display:grid;min-width:0;gap:3px;flex:1}.art-list button,.grant-list button{margin-left:auto}.grant-panel>p{margin-top:-4px}.grant-list article.revoked{opacity:.58}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}@media(max-width:760px){.alternate-admin>header{align-items:flex-start;flex-direction:column}.form-grid{grid-template-columns:1fr}}
.award-rules,.event-dispatch{margin-top:16px;padding:16px;border:1px solid #35424a;background:#091016}.award-rules article{display:flex;align-items:center;gap:12px;margin-top:8px;padding:9px;border:1px solid #35424a;background:#101820}.award-rules article div{display:grid;min-width:0;gap:3px;flex:1}.award-rules article.inactive{opacity:.58}.event-dispatch textarea{box-sizing:border-box;width:100%;margin:8px 0;padding:10px;border:1px solid #4a5860;background:#070d12;color:#fff;font:14px/1.5 ui-monospace,monospace}.event-rule{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:8px;padding:9px;border:1px solid #35424a}.event-rule span{color:#c4ccce}
</style>
