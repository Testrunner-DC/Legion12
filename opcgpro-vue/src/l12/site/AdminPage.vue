<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { adminApi, apiBase, canAccessAdmin, isAdmin, type AdminAudit, type AtomicAbility, type AtomicCardEffect, type AtomicCoverage, type BugReport, type ContentEntry, type EffectAtomDescriptor, type PlatformAccount } from '@/l12/platform'
import { createHomeContent, homeContentFields } from './homeContent'

const tab = ref<'bugs' | 'accounts' | 'content' | 'effects' | 'audit' | 'operations'>('bugs')
const bugs = ref<BugReport[]>([])
const accounts = ref<PlatformAccount[]>([])
const statusFilter = ref('')
const priorityFilter = ref('')
const bugSearch = ref('')
const bugComments = reactive<Record<string, string>>({})
const reviewNotes = reactive<Record<string, string>>({})
const notice = ref('')
const content = reactive(createHomeContent())
const ruleNotice = ref('')
const contentEntries = reactive<Record<string, ContentEntry>>({})
const audits = ref<AdminAudit[]>([])
const auditCategory = ref('')
const effectCards = ref<AtomicCardEffect[]>([])
const effectAtoms = ref<EffectAtomDescriptor[]>([])
const effectCoverage = ref<AtomicCoverage | null>(null)
const selectedEffect = ref<AtomicCardEffect | null>(null)
const effectSearch = ref('')
const effectStatus = ref('')
const effectProduct = ref('')
const effectAtomKind = ref('')
const effectTotal = ref(0)
const effectPage = ref(1)
const effectLoading = ref(false)

async function loadBugs() { try { bugs.value = await adminApi.bugs({ status: statusFilter.value, priority: priorityFilter.value, search: bugSearch.value }) } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function loadAccounts() { try { accounts.value = await adminApi.accounts() } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function updateBug(item: BugReport) { try { const updated = await adminApi.updateBug(item.id, { status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes, comment: bugComments[item.id] }); bugs.value = bugs.value.map(bug => bug.id === updated.id ? updated : bug); bugComments[item.id] = ''; notice.value = `${item.id} 已更新并写入审计记录` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
function bugActionLabel(action: string) { return ({ created: '建立反馈', status: '状态变更', priority: '优先级变更', assignee: '负责人变更', notes: '处理摘要变更', comment: '追加处理记录' } as Record<string, string>)[action] || action }
function matchJsonUrl(matchId: string) { return `${apiBase()}/api/matches/${encodeURIComponent(matchId)}` }
async function setRole(account: PlatformAccount) { try { await adminApi.setRole(account.id, account.role); notice.value = `${account.username} 权限已更新` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
async function loadContent() {
  for (const field of homeContentFields) {
    try { const entry = await adminApi.getContent(field.key); contentEntries[field.key] = entry; content[field.id] = entry.draftValue || field.defaultValue } catch {}
  }
  try { const entry = await adminApi.getContent('rules.notice'); contentEntries['rules.notice'] = entry; ruleNotice.value = entry.draftValue } catch {}
}
async function saveContentDrafts() { try { const entries = await Promise.all([...homeContentFields.map(field => adminApi.saveContentDraft(field.key, content[field.id])), adminApi.saveContentDraft('rules.notice', ruleNotice.value)]); entries.forEach(entry => { contentEntries[entry.key] = entry }); notice.value = '草稿已保存，尚未影响官网' } catch (error) { notice.value = error instanceof Error ? error.message : '保存失败' } }
async function publishContent() { try { await saveContentDrafts(); const entries = await Promise.all([...homeContentFields.map(field => adminApi.publishContent(field.key)), adminApi.publishContent('rules.notice')]); entries.forEach(entry => { contentEntries[entry.key] = entry }); notice.value = '官网内容已发布并写入审计记录' } catch (error) { notice.value = error instanceof Error ? error.message : '发布失败' } }
async function reviewAbility(ability: AtomicAbility, status: string, note = '') { if (!selectedEffect.value) return; try { await adminApi.reviewEffect(selectedEffect.value.cardId, { abilityId: ability.abilityId, status, note }); selectedEffect.value = await adminApi.effect(selectedEffect.value.cardId); effectCards.value = effectCards.value.map(card => card.cardId === selectedEffect.value?.cardId ? selectedEffect.value : card); notice.value = `${selectedEffect.value.cardId} ABILITY ${ability.sequence} 审查状态已记录` } catch (error) { notice.value = error instanceof Error ? error.message : '审查记录失败' } }
async function loadAudit() { try { audits.value = await adminApi.audit(auditCategory.value) } catch (error) { notice.value = error instanceof Error ? error.message : '审计日志加载失败' } }
async function loadEffects(resetPage = false) {
  if (resetPage) effectPage.value = 1
  effectLoading.value = true
  try {
    const [page, atoms] = await Promise.all([
      adminApi.effects({ search: effectSearch.value, status: effectStatus.value, product: effectProduct.value, atomKind: effectAtomKind.value, page: effectPage.value, pageSize: 50 }),
      effectAtoms.value.length ? Promise.resolve(effectAtoms.value) : adminApi.effectAtoms(),
    ])
    effectCards.value = page.items; effectTotal.value = page.total; effectCoverage.value = page.coverage; effectAtoms.value = atoms
    if (selectedEffect.value) selectedEffect.value = page.items.find(card => card.cardId === selectedEffect.value?.cardId) ?? selectedEffect.value
  } catch (error) { notice.value = error instanceof Error ? error.message : '卡效清单加载失败' }
  finally { effectLoading.value = false }
}
async function selectEffect(card: AtomicCardEffect) {
  try { selectedEffect.value = await adminApi.effect(card.cardId) } catch (error) { notice.value = error instanceof Error ? error.message : '卡效详情加载失败' }
}
function statusLabel(status: string) { return ({ 'no-effect': '无卡效', 'legacy-backed': '旧实现兜底', 'partially-atomized': '部分原子化', 'declarative-ready': '声明就绪', 'runtime-migrated': '运行时迁移', verified: '已验证' } as Record<string, string>)[status] || status }
function reviewLabel(status: string) { return ({ confirmed: '人工确认', 'human-assisted': '人工辅助', rejected: '退回修正', unreviewed: '待人工审查' } as Record<string, string>)[status] || status }
function atomDescriptor(kind: string) { return effectAtoms.value.find(atom => atom.kind === kind) }
function previousEffectsPage() { if (effectPage.value > 1) { effectPage.value--; loadEffects() } }
function nextEffectsPage() { if (effectPage.value * 50 < effectTotal.value) { effectPage.value++; loadEffects() } }
onMounted(() => { if (canAccessAdmin.value) { loadContent(); loadEffects(); if (isAdmin.value) { loadBugs(); loadAccounts(); loadAudit() } else tab.value = 'content' } })
</script>

<template>
  <div class="admin-page">
    <header><div><small>ADMINISTRATION</small><h1>管理后台</h1><p>账号权限、Bug 闭环、官网内容与运营配置。</p></div><router-link to="/me">← 返回我的</router-link></header>
    <section v-if="!canAccessAdmin" class="denied"><b>需要后台权限</b><span>请先在“我的”页面登录管理员或内容编辑账号。</span></section>
    <template v-else>
      <nav>
        <button v-if="isAdmin" :class="{ active: tab === 'bugs' }" @click="tab = 'bugs'">Bug 管理</button>
        <button v-if="isAdmin" :class="{ active: tab === 'accounts' }" @click="tab = 'accounts'">账号权限</button>
        <button :class="{ active: tab === 'content' }" @click="tab = 'content'">官网内容</button>
        <button :class="{ active: tab === 'effects' }" @click="tab = 'effects'; loadEffects()">卡效原子化</button>
        <button v-if="isAdmin" :class="{ active: tab === 'audit' }" @click="tab = 'audit'; loadAudit()">审计日志</button>
        <button :class="{ active: tab === 'operations' }" @click="tab = 'operations'">运营配置</button>
      </nav>
      <section v-if="tab === 'bugs'" class="panel">
        <header><h2>Bug 反馈</h2><div class="bug-filters"><input v-model="bugSearch" placeholder="编号 / 标题 / 玩家 / 房间 / 对局" @keyup.enter="loadBugs"><select v-model="statusFilter" @change="loadBugs"><option value="">全部状态</option><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="priorityFilter" @change="loadBugs"><option value="">全部优先级</option><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><button @click="loadBugs">查询</button></div></header>
        <article v-for="item in bugs" :key="item.id" class="bug-row"><div class="bug-summary"><code>{{ item.id }}</code><b>{{ item.title }}</b><span>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }} · {{ item.version }}</span><p>{{ item.description }}</p><small>{{ item.page }}<template v-if="item.roomCode"> · 房间 {{ item.roomCode }}</template><template v-if="item.matchId"> · <a :href="matchJsonUrl(item.matchId)" target="_blank" rel="noopener">查看对局 JSON {{ item.matchId }}</a></template></small><details class="bug-history"><summary>处理记录（{{ item.history.length }}）</summary><ol><li v-for="audit in item.history" :key="audit.id"><b>{{ bugActionLabel(audit.action) }}</b><span>{{ audit.actorName }} · {{ new Date(audit.createdAt).toLocaleString() }}</span><p v-if="audit.comment">{{ audit.comment }}</p><code v-else-if="audit.fromValue !== audit.toValue">{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}</code></li></ol></details></div><div class="bug-admin"><select v-model="item.status"><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><input v-model="item.assignee" placeholder="负责人"/><textarea v-model="item.adminNotes" rows="3" placeholder="当前处理摘要"/><textarea v-model="bugComments[item.id]" rows="3" placeholder="追加处理记录（保存后进入时间线）"/><button @click="updateBug(item)">保存并记录</button></div></article>
        <div v-if="!bugs.length" class="empty">暂无符合筛选条件的反馈</div>
      </section>
      <section v-else-if="tab === 'accounts'" class="panel"><header><h2>账号与权限</h2><button @click="loadAccounts">刷新</button></header><div class="account-row head"><b>用户名</b><span>建立时间</span><span>权限</span><span>操作</span></div><div v-for="account in accounts" :key="account.id" class="account-row"><b>{{ account.username }}</b><span>{{ new Date(account.createdAt).toLocaleString() }}</span><select v-model="account.role" :disabled="account.username === 'Admin'"><option value="player">玩家</option><option value="referee">裁判</option><option value="organizer">主办者</option><option value="editor">内容编辑</option><option value="admin">管理员</option></select><button :disabled="account.username === 'Admin'" @click="setRole(account)">保存</button></div></section>
      <section v-else-if="tab === 'content'" class="panel content-editor"><header><div><h2>官网内容</h2><p>先保存草稿，确认后再发布；只有“发布”会改变玩家看到的官网。</p></div><span class="content-actions"><button @click="saveContentDrafts">保存草稿</button><button class="publish" @click="publishContent">发布全部</button></span></header><label v-for="field in homeContentFields" :key="field.key">{{ field.label }}<em :data-status="contentEntries[field.key]?.status">{{ contentEntries[field.key]?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em><textarea v-if="field.multiline" v-model="content[field.id]" :rows="field.rows ?? 4"/><input v-else v-model="content[field.id]"/></label><label>规则页公告<em :data-status="contentEntries['rules.notice']?.status">{{ contentEntries['rules.notice']?.status === 'draft' ? '有未发布草稿' : '已发布' }}</em><textarea v-model="ruleNotice" rows="5"/></label></section>
      <section v-else-if="tab === 'effects'" class="effects-workbench">
        <div v-if="effectCoverage" class="coverage-strip">
          <article><small>卡牌</small><b>{{ effectCoverage.totalCards }}</b><span>{{ effectCoverage.cardsWithText }} 张含效果</span></article>
          <article><small>能力</small><b>{{ effectCoverage.totalAbilities }}</b><span>按原文时点拆分</span></article>
          <article><small>原子</small><b>{{ effectCoverage.totalAtoms }}</b><span>{{ effectAtoms.length }} 种注册类型</span></article>
          <article class="coverage-warning"><small>旧实现兜底</small><b>{{ effectCoverage.legacyBackedAbilities }}</b><span>迁移完成前保留</span></article>
          <article class="coverage-ready"><small>声明就绪</small><b>{{ effectCoverage.declarativeReadyAbilities }}</b><span>仍需逐卡等价验证</span></article>
          <article class="coverage-verified"><small>实战已验证</small><b>{{ effectCoverage.verifiedAbilities }}</b><span>已由原子程序接管</span></article>
        </div>
        <div class="effects-layout">
          <section class="panel effects-list">
            <header><div><h2>全卡效能力清单</h2><p>后台与规则内核读取同一份原子注册表；状态不会因画出节点而自动视为已迁移。</p></div><button @click="loadEffects()">刷新</button></header>
            <div class="effect-filters">
              <input v-model="effectSearch" placeholder="卡号 / 卡名 / 原文" @keyup.enter="loadEffects(true)"/>
              <select v-model="effectProduct" @change="loadEffects(true)"><option value="">全部卡池</option><option value="S01">S01</option><option value="S02">S02</option></select>
              <select v-model="effectStatus" @change="loadEffects(true)"><option value="">全部状态</option><option value="verified">实战已验证</option><option value="legacy-backed">旧实现兜底</option><option value="partially-atomized">部分原子化</option><option value="declarative-ready">声明就绪</option><option value="no-effect">无卡效</option></select>
              <select v-model="effectAtomKind" @change="loadEffects(true)"><option value="">全部原子</option><option v-for="atom in effectAtoms" :key="atom.kind" :value="atom.kind">{{ atom.label }}</option></select>
              <button @click="loadEffects(true)">查询</button>
            </div>
            <div class="effect-scroll" tabindex="0" aria-label="全卡效能力清单，可上下滚动">
              <div class="effect-table-head"><span>卡牌</span><span>组合</span><span>迁移状态</span></div>
              <button v-for="card in effectCards" :key="card.cardId" class="effect-row" :class="{ selected: selectedEffect?.cardId === card.cardId }" @click="selectEffect(card)">
                <span class="effect-identity"><img v-if="card.imageUrl" :src="card.imageUrl"/><span><code>{{ card.cardId }}</code><b>{{ card.name }}</b><small>{{ card.faction }} · {{ card.cardType }}</small></span></span>
                <span class="effect-count"><b>{{ card.abilities.length }}</b> 能力 / <b>{{ card.atomCount }}</b> 原子<small v-if="card.legacyAtomCount">{{ card.legacyAtomCount }} 个兜底节点</small><em class="review-pill" :data-review="card.reviewStatus">{{ reviewLabel(card.reviewStatus) }}</em></span>
                <span class="status-pill" :data-status="card.migrationStatus">{{ statusLabel(card.migrationStatus) }}</span>
              </button>
              <div v-if="effectLoading" class="empty">正在读取原子清单…</div>
            </div>
            <div class="effect-pagination"><button :disabled="effectPage <= 1" @click="previousEffectsPage">上一页</button><span>第 {{ effectPage }} 页 · 共 {{ effectTotal }} 张</span><button :disabled="effectPage * 50 >= effectTotal" @click="nextEffectsPage">下一页</button></div>
          </section>
          <section class="panel effect-detail">
            <template v-if="selectedEffect">
              <header><div><small>{{ selectedEffect.cardId }} · {{ selectedEffect.product }}</small><h2>{{ selectedEffect.name }}</h2><p>{{ selectedEffect.faction }} · {{ selectedEffect.cardType }}</p></div><span class="effect-header-status"><em class="review-pill" :data-review="selectedEffect.reviewStatus">{{ reviewLabel(selectedEffect.reviewStatus) }}</em><span class="status-pill" :data-status="selectedEffect.migrationStatus">{{ statusLabel(selectedEffect.migrationStatus) }}</span></span></header>
              <div class="original-text"><b>卡面原文</b><p>{{ selectedEffect.effectText || '无效果文本' }}</p></div>
              <article v-for="ability in selectedEffect.abilities" :key="ability.abilityId" class="ability-card">
                <header><span><small>ABILITY {{ ability.sequence }}</small><b>{{ ability.trigger }}</b><em class="execution-model">{{ ability.executionModel }}</em></span><span class="effect-header-status"><em class="review-pill" :data-review="ability.reviewStatus">{{ reviewLabel(ability.reviewStatus) }}</em><span class="status-pill" :data-status="ability.migrationStatus">{{ statusLabel(ability.migrationStatus) }}</span></span></header>
                <p>{{ ability.text }}</p>
                <div class="atom-flow">
                  <template v-for="(atom, index) in ability.atoms" :key="atom.atomId">
                    <article class="atom-node" :data-category="atomDescriptor(atom.kind)?.category" :class="{ legacy: atom.kind === 'legacy.resolve' }" :title="atomDescriptor(atom.kind)?.description">
                      <small>{{ atom.stage }} · {{ atomDescriptor(atom.kind)?.category || '原子' }}</small><b>{{ atom.label }}</b><code>{{ atom.kind }}</code>
                      <dl v-if="Object.keys(atom.parameters).length"><template v-for="(value, key) in atom.parameters" :key="key"><dt>{{ key }}</dt><dd>{{ value }}</dd></template></dl>
                    </article><span v-if="index < ability.atoms.length - 1" class="flow-arrow">→</span>
                  </template>
                </div>
                <div class="ability-review"><textarea v-model="reviewNotes[ability.abilityId]" rows="2" placeholder="人工核对备注（规则书、FAQ、测试证据）"/><button @click="reviewAbility(ability, 'human-assisted', reviewNotes[ability.abilityId])">标记人工辅助</button><button class="confirm" @click="reviewAbility(ability, 'confirmed', reviewNotes[ability.abilityId])">确认拆分</button><button class="reject" @click="reviewAbility(ability, 'rejected', reviewNotes[ability.abilityId])">退回修正</button></div>
                <details><summary>线性执行与迁移守卫</summary><ol><li v-for="atom in ability.atoms" :key="`trace-${atom.atomId}`"><b>{{ atom.order }}. {{ atom.label }}</b><span>{{ atomDescriptor(atom.kind)?.kernelContract }}</span></li></ol><p v-if="ability.hasLegacyFallback" class="legacy-note">执行到 <code>legacy.resolve</code> 时只调用旧权威分支；新旧实现不会同时结算。</p></details>
              </article>
              <details class="raw-definition"><summary>查看原子定义 JSON</summary><pre>{{ JSON.stringify(selectedEffect, null, 2) }}</pre></details>
            </template>
            <div v-else class="empty detail-empty">从左侧选择一张卡牌，查看原文、能力拆分、流程图、参数与迁移守卫。</div>
          </section>
        </div>
      </section>
      <section v-else-if="tab === 'audit'" class="panel audit-panel"><header><h2>管理操作审计</h2><div><select v-model="auditCategory" @change="loadAudit"><option value="">全部类型</option><option value="account">账号权限</option><option value="content">内容发布</option><option value="effect">卡效确认</option></select><button @click="loadAudit">刷新</button></div></header><div class="audit-head"><span>时间 / 操作者</span><span>动作</span><span>对象</span><span>变更</span></div><article v-for="audit in audits" :key="audit.id" class="audit-row"><span>{{ new Date(audit.createdAt).toLocaleString() }}<small>{{ audit.actorName }}</small></span><b>{{ audit.category }} · {{ audit.action }}</b><code>{{ audit.target }}</code><span>{{ audit.fromValue || '无' }} → {{ audit.toValue || '无' }}<small v-if="audit.comment">{{ audit.comment }}</small></span></article><div v-if="!audits.length" class="empty">暂无管理操作记录</div></section>
      <section v-else class="panel operation-grid"><article><b>赛季与天灾</b><p>配置当前赛季天灾池、堙灭锁定、禁限卡表及生效时间。</p></article><article><b>赛事监管</b><p>赛事审批、主办者/裁判权限、暂停与判罚审计。</p></article><article><b>对局与回放</b><p>按房间、玩家、赛事检索对局及 JSON 回放。</p></article><article><b>内容发布</b><p>资讯草稿、定时发布、规则书/FAQ 版本和更新日志。</p></article><article><b>安全与审计</b><p>恶意用户名词库、账号状态、权限变更和管理操作日志。</p></article><article><b>运行状态</b><p>在线人数、连接健康、图片缓存命中率与服务版本。</p></article></section>
      <p v-if="notice" class="notice">{{ notice }}</p>
    </template>
  </div>
</template>

<style scoped>
.admin-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.admin-page>header{display:flex;align-items:flex-start;justify-content:space-between}.admin-page small{color:#d5b85e;font:900 9px monospace;letter-spacing:.16em}.admin-page h1{margin:5px 0;font-size:30px}.admin-page p{color:#7d898e;font-size:11px;line-height:1.7}.admin-page>header a{color:#e1c36e;text-decoration:none;font-size:11px;font-weight:900}.admin-page>nav{display:flex;gap:8px;margin:22px 0}.admin-page>nav button{padding:11px 18px;border:1px solid #3f4b52;background:#0d141a;color:#899398;font-weight:900}.admin-page>nav button.active{border-color:#d6b85f;background:#2a2313;color:#f2d985}.panel,.denied{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel button,.panel select,.panel input,.panel textarea{border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.bug-row{display:grid;grid-template-columns:1fr 280px;gap:18px;padding:18px 0;border-bottom:1px solid #303c43}.bug-summary code{color:#dfc36f}.bug-summary b,.bug-summary span,.bug-summary small{display:block}.bug-summary b{margin:7px 0;font-size:16px}.bug-summary span,.bug-summary small{color:#718087;font-size:9px}.bug-summary p{color:#c7ccca;white-space:pre-wrap;overflow-wrap:anywhere}.bug-admin{display:grid;grid-template-columns:1fr 1fr;gap:7px}.bug-admin input,.bug-admin textarea,.bug-admin button{grid-column:1/-1}.account-row{display:grid;grid-template-columns:1fr 1.5fr 180px 90px;align-items:center;gap:10px;padding:12px;border-bottom:1px solid #303c43}.account-row.head{color:#7e8a90;font-size:9px}.content-editor label{display:block;margin-top:16px;color:#b8c0c1;font-size:10px;font-weight:900}.content-editor input,.content-editor textarea{box-sizing:border-box;width:100%;margin-top:7px}.operation-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.operation-grid article{padding:16px;border:1px solid #35424a;background:#0a1117}.denied{display:flex;flex-direction:column;gap:7px;margin-top:22px}.denied span,.empty{color:#7e8a90}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
.bug-filters{display:flex;flex-wrap:wrap;gap:7px}.bug-filters input{min-width:240px}.bug-summary a{color:#83d5e4}.bug-history{margin-top:14px;border-top:1px solid #2e3b42;padding-top:10px}.bug-history summary{cursor:pointer;color:#d6bd70;font-size:10px;font-weight:900}.bug-history ol{max-height:220px;overflow:auto;padding-left:20px}.bug-history li{margin:8px 0}.bug-history li b,.bug-history li span{display:inline;margin-right:7px}.bug-history li p{margin:3px 0}.bug-history li code{display:block;color:#a8b3b5}
.coverage-strip{display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px}.coverage-strip article{display:flex;flex-direction:column;gap:4px;padding:15px;border:1px solid #39474e;background:#0c141a}.coverage-strip b{font-size:24px}.coverage-strip span{color:#75838a;font-size:9px}.coverage-strip .coverage-warning{border-color:#7b4936;background:#21140f}.coverage-strip .coverage-ready{border-color:#2c6754;background:#0c1c17}.effects-workbench{min-width:0}.effects-layout{display:grid;grid-template-columns:minmax(480px,.9fr) minmax(540px,1.1fr);gap:12px;min-width:0}.effects-list,.effect-detail{min-width:0}.effects-list{display:flex;max-height:calc(100vh - 215px);min-height:560px;flex-direction:column;overflow:hidden}.effects-list>header{flex:none;gap:12px;min-width:0}.effects-list>header>div{min-width:0}.effects-list>header button{flex:none}.effects-list>header p{margin:3px 0;overflow-wrap:anywhere}.effect-filters{display:flex;flex:none;flex-wrap:wrap;gap:7px;margin:14px 0}.effect-filters input{flex:1 1 220px}.effect-filters select{flex:1 1 112px}.effect-filters button{flex:0 0 auto}.effect-filters input,.effect-filters select,.effect-filters button{box-sizing:border-box;min-width:0}.effect-scroll{min-height:0;flex:1;overflow-x:hidden;overflow-y:auto;overscroll-behavior:contain;padding-right:5px;scrollbar-gutter:stable}.effect-scroll:focus-visible{outline:1px solid #d8b95f;outline-offset:2px}.effect-table-head,.effect-row{display:grid;grid-template-columns:minmax(220px,1fr) minmax(125px,145px) minmax(92px,110px);align-items:center;gap:10px}.effect-table-head{padding:7px 10px;color:#6f7d84;font-size:9px;font-weight:900}.effect-row{box-sizing:border-box;width:100%;margin-top:4px;padding:8px 10px!important;text-align:left}.effect-row.selected{border-color:#d8b95f;background:#241e11}.effect-identity{display:flex;align-items:center;min-width:0;gap:9px}.effect-identity img{width:38px;height:52px;object-fit:cover;object-position:center 30%;border:1px solid #56636a}.effect-identity>span{display:flex;min-width:0;flex-direction:column}.effect-identity code{color:#d8bd6a;font-size:9px}.effect-identity b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.effect-identity small{color:#708087!important;letter-spacing:0!important}.effect-count{display:flex;min-width:0;flex-wrap:wrap;gap:3px;color:#9aa4a7;font-size:10px}.effect-count small{width:100%;color:#c17d60!important;letter-spacing:0!important}.status-pill,.review-pill{justify-self:start;padding:5px 7px;border:1px solid #516068;background:#151e24;color:#b9c2c4;font-size:9px;font-style:normal;font-weight:900;white-space:nowrap}.status-pill[data-status="partially-atomized"]{border-color:#9a742a;background:#2b210d;color:#f0cf73}.status-pill[data-status="legacy-backed"]{border-color:#84424b;background:#291116;color:#ef8994}.status-pill[data-status="declarative-ready"],.status-pill[data-status="verified"]{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.review-pill[data-review="human-assisted"]{border-color:#72539a;background:#20152c;color:#d9baff}.review-pill[data-review="confirmed"]{border-color:#2f7b89;background:#0a2027;color:#7bd8e7}.review-pill[data-review="unreviewed"]{color:#7d8b91}.effect-header-status{display:flex;flex:none;align-items:center;justify-content:flex-end;gap:6px}.effect-pagination{display:flex;flex:none;align-items:center;justify-content:center;gap:12px;margin-top:14px}.effect-pagination span{color:#78858a;font-size:9px}.effect-detail>header{gap:12px}.effect-detail>header>div{min-width:0}.effect-detail>header>div small{display:block}.original-text{margin:14px 0;padding:14px;border-left:3px solid #d7b85d;background:#0a1116}.original-text p{margin:7px 0 0;color:#cdd2d0}.ability-card{margin-top:12px;padding:13px;border:1px solid #35434a;background:#0a1117}.ability-card>header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.ability-card>header>span:first-child{display:flex;min-width:0;flex-direction:column;gap:3px}.ability-card>header small{letter-spacing:.12em}.ability-card>p{color:#c5ccca;overflow-wrap:anywhere}.atom-flow{display:flex;align-items:stretch;gap:5px;overflow-x:auto;padding:8px 1px 13px}.atom-node{flex:0 0 150px;padding:10px;border:1px solid #3e5965;background:#0d1b22}.atom-node[data-category="费用"]{border-color:#85652d;background:#241d0f}.atom-node[data-category="选择"]{border-color:#5f4385;background:#1c1328}.atom-node[data-category="结算"],.atom-node[data-category="数值"]{border-color:#296b69;background:#0b2423}.atom-node.legacy{border-color:#934452;background:#2b1017}.atom-node>small,.atom-node>b,.atom-node>code{display:block}.atom-node>b{margin:5px 0}.atom-node>code{color:#83949b;font-size:8px}.atom-node dl{display:grid;grid-template-columns:auto 1fr;gap:3px;margin:8px 0 0;font-size:8px}.atom-node dt{color:#6e7d82}.atom-node dd{overflow:hidden;margin:0;color:#c6ccca;text-overflow:ellipsis;white-space:nowrap}.flow-arrow{align-self:center;color:#c8a94e;font-size:18px}.ability-card details,.raw-definition{margin-top:10px;border-top:1px solid #2e3a40;padding-top:9px}.ability-card summary,.raw-definition summary{cursor:pointer;color:#d6bd70;font-size:10px;font-weight:900}.ability-card ol{padding-left:20px}.ability-card li{margin:7px 0;color:#c8cecc;font-size:10px}.ability-card li span{display:block;color:#718087}.legacy-note{padding:8px;border-left:2px solid #a04755;background:#251016;color:#e19aa4!important}.raw-definition pre{max-height:360px;overflow:auto;padding:12px;background:#05090c;color:#aeb9b9;font-size:9px;white-space:pre-wrap}.detail-empty{display:grid;min-height:400px;place-items:center;text-align:center}
.coverage-strip{grid-template-columns:repeat(6,1fr)}.coverage-strip .coverage-verified{border-color:#2f7b89;background:#0a2027}
.content-actions{display:flex;gap:7px}.content-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.content-editor label>em{float:right;padding:3px 6px;border:1px solid #3e5c4f;color:#7fd3ae;font-size:8px;font-style:normal}.content-editor label>em[data-status="draft"]{border-color:#876328;color:#efca70}.ability-review{display:grid;grid-template-columns:1fr auto auto auto;gap:6px;margin-top:10px}.ability-review textarea{min-width:0;resize:vertical}.ability-review .confirm{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.ability-review .reject{border-color:#84424b;background:#291116;color:#ef8994}.review-pill[data-review="rejected"]{border-color:#84424b;background:#291116;color:#ef8994}.audit-head,.audit-row{display:grid;grid-template-columns:1.25fr .8fr 1fr 1.5fr;gap:12px;padding:10px}.audit-head{color:#77858b;font-size:9px;font-weight:900}.audit-row{align-items:start;border-top:1px solid #303c43;color:#c8cecc;font-size:10px}.audit-row span,.audit-row small{display:block}.audit-row small{margin-top:4px;color:#77858b}.audit-row code{color:#dfc36f;overflow-wrap:anywhere}
@media(max-width:1300px){.effects-layout{grid-template-columns:1fr}.coverage-strip{grid-template-columns:repeat(3,1fr)}}@media(max-width:850px){.bug-row{grid-template-columns:1fr}.account-row{grid-template-columns:1fr 1fr}.operation-grid{grid-template-columns:1fr}.admin-page>nav{overflow-x:auto}.admin-page>nav button{flex:none}.coverage-strip{grid-template-columns:1fr 1fr}.effect-filters{grid-template-columns:1fr 1fr}.effect-table-head,.effect-row{grid-template-columns:minmax(180px,1fr) 110px}.effect-table-head span:last-child,.effect-row>.status-pill{display:none}}
</style>
