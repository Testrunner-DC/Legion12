<script setup lang="ts">
import type { TournamentSummary } from '@/l12/platform'
import { tournamentFormatText, tournamentStatusText, tournamentViewerRoleText } from '@/l12/tournamentLabels'

defineProps<{ items: TournamentSummary[]; loading: boolean }>()
defineEmits<{ open: [code: string] }>()

const displayTime = (value?: string) => value ? new Date(value).toLocaleString('zh-CN') : '时间待定'
</script>

<template>
  <div v-if="loading" class="empty">正在加载赛事…</div>
  <div v-else-if="!items.length" class="empty">这里暂时没有赛事</div>
  <div v-else class="summary-grid">
    <button v-for="item in items" :key="item.id" class="summary-card" type="button" @click="$emit('open', item.code)">
      <span class="summary-top"><b>{{ item.name }}</b><i v-if="item.requiresAction">需要处理</i></span>
      <span>{{ tournamentStatusText(item.status) }} · {{ tournamentFormatText(item.format) }} · {{ item.code }}</span>
      <span>{{ displayTime(item.startAt) }} · 主办 {{ item.organizerName }}</span>
      <span>{{ item.counts.active }}/{{ item.maxPlayers }} 人<span v-if="item.counts.waitlisted"> · 候补 {{ item.counts.waitlisted }}</span></span>
      <small>{{ tournamentViewerRoleText(item.viewerRole) }}</small>
    </button>
  </div>
</template>

<style scoped>
.summary-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:12px}.summary-card{display:flex;flex-direction:column;gap:7px;padding:16px;text-align:left;border:1px solid var(--border);border-radius:12px;background:var(--panel);color:inherit;cursor:pointer}.summary-card:hover{border-color:var(--accent)}.summary-top{display:flex;justify-content:space-between;gap:8px}.summary-top i{font-size:12px;color:var(--accent);font-style:normal}.summary-card span:not(.summary-top),.summary-card small{color:var(--muted);font-size:13px}.empty{padding:28px;text-align:center;color:var(--muted)}
</style>
