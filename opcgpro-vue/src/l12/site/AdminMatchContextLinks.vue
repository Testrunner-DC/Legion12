<script setup lang="ts">
defineProps<{ matchId: string; accountId?: string }>()
</script>
<template>
  <nav class="match-context" aria-label="对局关联任务">
    <span><small>对局对象</small><code>{{ matchId }}</code></span>
    <router-link :to="`/admin/matches/archive/${encodeURIComponent(matchId)}`">档案</router-link>
    <router-link :to="{ path: '/admin/matches/governance', query: { search: matchId } }">治理记录</router-link>
    <router-link :to="{ path: '/admin/matches/integrity', query: { matchId } }">完整性审计</router-link>
    <router-link to="/admin/matches/tournaments">赛事上下文</router-link>
    <router-link v-if="accountId" :to="`/admin/users/accounts/${encodeURIComponent(accountId)}`">账号详情</router-link>
  </nav>
</template>
<style scoped>
.match-context{display:flex;align-items:center;gap:7px;flex-wrap:wrap;margin:12px 0;padding:9px;border:1px solid #39474e;background:#080e13}.match-context span{display:grid;min-width:180px;margin-right:auto}.match-context small{color:#75838a}.match-context code{color:#d8ba68;overflow-wrap:anywhere}.match-context a{padding:7px 9px;border:1px solid #4c5961;color:#d7c074;text-decoration:none;font-size:13px;font-weight:900}.match-context a:hover{border-color:#c1a44e;background:#211b0e}@media(max-width:620px){.match-context{display:grid}.match-context span{min-width:0}.match-context a{text-align:center}}
</style>
