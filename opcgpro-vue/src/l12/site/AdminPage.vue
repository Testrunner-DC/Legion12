<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { adminApi, isAdmin, type BugReport, type PlatformAccount } from '@/l12/platform'
import { createHomeContent, homeContentFields } from './homeContent'

const tab = ref<'bugs' | 'accounts' | 'content' | 'operations'>('bugs')
const bugs = ref<BugReport[]>([])
const accounts = ref<PlatformAccount[]>([])
const statusFilter = ref('')
const notice = ref('')
const content = reactive(createHomeContent())
const ruleNotice = ref('')

async function loadBugs() { try { bugs.value = await adminApi.bugs(statusFilter.value) } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function loadAccounts() { try { accounts.value = await adminApi.accounts() } catch (error) { notice.value = error instanceof Error ? error.message : '加载失败' } }
async function updateBug(item: BugReport) { try { await adminApi.updateBug(item.id, { status: item.status, priority: item.priority, assignee: item.assignee, adminNotes: item.adminNotes }); notice.value = `${item.id} 已更新` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
async function setRole(account: PlatformAccount) { try { await adminApi.setRole(account.id, account.role); notice.value = `${account.username} 权限已更新` } catch (error) { notice.value = error instanceof Error ? error.message : '更新失败' } }
async function loadContent() {
  for (const field of homeContentFields) {
    try { content[field.id] = (await adminApi.getContent(field.key)).value || field.defaultValue } catch {}
  }
  try { ruleNotice.value = (await adminApi.getContent('rules.notice')).value } catch {}
}
async function saveContent() { try { await Promise.all([...homeContentFields.map(field => adminApi.setContent(field.key, content[field.id])), adminApi.setContent('rules.notice', ruleNotice.value)]); notice.value = '官网内容已保存' } catch (error) { notice.value = error instanceof Error ? error.message : '保存失败' } }
onMounted(() => { if (isAdmin.value) { loadBugs(); loadAccounts(); loadContent() } })
</script>

<template>
  <div class="admin-page">
    <header><div><small>ADMINISTRATION</small><h1>管理后台</h1><p>账号权限、Bug 闭环、官网内容与运营配置。</p></div><router-link to="/me">← 返回我的</router-link></header>
    <section v-if="!isAdmin" class="denied"><b>需要最高权限账号</b><span>请先在“我的”页面登录管理员账号。</span></section>
    <template v-else>
      <nav><button :class="{ active: tab === 'bugs' }" @click="tab = 'bugs'">Bug 管理</button><button :class="{ active: tab === 'accounts' }" @click="tab = 'accounts'">账号权限</button><button :class="{ active: tab === 'content' }" @click="tab = 'content'">官网内容</button><button :class="{ active: tab === 'operations' }" @click="tab = 'operations'">运营配置</button></nav>
      <section v-if="tab === 'bugs'" class="panel">
        <header><h2>Bug 反馈</h2><div><select v-model="statusFilter" @change="loadBugs"><option value="">全部状态</option><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><button @click="loadBugs">刷新</button></div></header>
        <article v-for="item in bugs" :key="item.id" class="bug-row"><div class="bug-summary"><code>{{ item.id }}</code><b>{{ item.title }}</b><span>{{ item.reporterName }} · {{ new Date(item.createdAt).toLocaleString() }}</span><p>{{ item.description }}</p><small>{{ item.page }}<template v-if="item.roomCode"> · 房间 {{ item.roomCode }}</template><template v-if="item.matchId"> · 对局 {{ item.matchId }}</template></small></div><div class="bug-admin"><select v-model="item.status"><option value="new">新反馈</option><option value="confirmed">已确认</option><option value="in-progress">处理中</option><option value="resolved">已解决</option><option value="closed">已关闭</option></select><select v-model="item.priority"><option value="low">低</option><option value="normal">普通</option><option value="high">高</option><option value="critical">紧急</option></select><input v-model="item.assignee" placeholder="负责人"/><textarea v-model="item.adminNotes" rows="3" placeholder="内部备注"/><button @click="updateBug(item)">保存处理</button></div></article>
        <div v-if="!bugs.length" class="empty">暂无符合筛选条件的反馈</div>
      </section>
      <section v-else-if="tab === 'accounts'" class="panel"><header><h2>账号与权限</h2><button @click="loadAccounts">刷新</button></header><div class="account-row head"><b>用户名</b><span>建立时间</span><span>权限</span><span>操作</span></div><div v-for="account in accounts" :key="account.id" class="account-row"><b>{{ account.username }}</b><span>{{ new Date(account.createdAt).toLocaleString() }}</span><select v-model="account.role" :disabled="account.username === 'Admin'"><option value="player">玩家</option><option value="referee">裁判</option><option value="organizer">主办者</option><option value="editor">内容编辑</option><option value="admin">管理员</option></select><button :disabled="account.username === 'Admin'" @click="setRole(account)">保存</button></div></section>
      <section v-else-if="tab === 'content'" class="panel content-editor"><header><h2>官网内容</h2><button @click="saveContent">保存全部</button></header><label v-for="field in homeContentFields" :key="field.key">{{ field.label }}<textarea v-if="field.multiline" v-model="content[field.id]" :rows="field.rows ?? 4"/><input v-else v-model="content[field.id]"/></label><label>规则页公告<textarea v-model="ruleNotice" rows="5"/></label></section>
      <section v-else class="panel operation-grid"><article><b>赛季与天灾</b><p>配置当前赛季天灾池、堙灭锁定、禁限卡表及生效时间。</p></article><article><b>赛事监管</b><p>赛事审批、主办者/裁判权限、暂停与判罚审计。</p></article><article><b>对局与回放</b><p>按房间、玩家、赛事检索对局及 JSON 回放。</p></article><article><b>内容发布</b><p>资讯草稿、定时发布、规则书/FAQ 版本和更新日志。</p></article><article><b>安全与审计</b><p>恶意用户名词库、账号状态、权限变更和管理操作日志。</p></article><article><b>运行状态</b><p>在线人数、连接健康、图片缓存命中率与服务版本。</p></article></section>
      <p v-if="notice" class="notice">{{ notice }}</p>
    </template>
  </div>
</template>

<style scoped>
.admin-page{min-height:100%;padding:30px clamp(18px,3vw,46px) 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.admin-page>header{display:flex;align-items:flex-start;justify-content:space-between}.admin-page small{color:#d5b85e;font:900 9px monospace;letter-spacing:.16em}.admin-page h1{margin:5px 0;font-size:30px}.admin-page p{color:#7d898e;font-size:11px;line-height:1.7}.admin-page>header a{color:#e1c36e;text-decoration:none;font-size:11px;font-weight:900}.admin-page>nav{display:flex;gap:8px;margin:22px 0}.admin-page>nav button{padding:11px 18px;border:1px solid #3f4b52;background:#0d141a;color:#899398;font-weight:900}.admin-page>nav button.active{border-color:#d6b85f;background:#2a2313;color:#f2d985}.panel,.denied{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel button,.panel select,.panel input,.panel textarea{border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.bug-row{display:grid;grid-template-columns:1fr 280px;gap:18px;padding:18px 0;border-bottom:1px solid #303c43}.bug-summary code{color:#dfc36f}.bug-summary b,.bug-summary span,.bug-summary small{display:block}.bug-summary b{margin:7px 0;font-size:16px}.bug-summary span,.bug-summary small{color:#718087;font-size:9px}.bug-summary p{color:#c7ccca;white-space:pre-wrap;overflow-wrap:anywhere}.bug-admin{display:grid;grid-template-columns:1fr 1fr;gap:7px}.bug-admin input,.bug-admin textarea,.bug-admin button{grid-column:1/-1}.account-row{display:grid;grid-template-columns:1fr 1.5fr 180px 90px;align-items:center;gap:10px;padding:12px;border-bottom:1px solid #303c43}.account-row.head{color:#7e8a90;font-size:9px}.content-editor label{display:block;margin-top:16px;color:#b8c0c1;font-size:10px;font-weight:900}.content-editor input,.content-editor textarea{box-sizing:border-box;width:100%;margin-top:7px}.operation-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.operation-grid article{padding:16px;border:1px solid #35424a;background:#0a1117}.denied{display:flex;flex-direction:column;gap:7px;margin-top:22px}.denied span,.empty{color:#7e8a90}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}@media(max-width:850px){.bug-row{grid-template-columns:1fr}.account-row{grid-template-columns:1fr 1fr}.operation-grid{grid-template-columns:1fr}.admin-page>nav{overflow-x:auto}.admin-page>nav button{flex:none}}
</style>
