<script setup lang="ts">
import type { TournamentSummary, PublicTournamentSummary } from '@/l12/platform'
import { tournamentFormatText, tournamentStatusText, tournamentViewerRoleText } from '@/l12/tournamentLabels'

defineProps<{ items: Array<TournamentSummary | PublicTournamentSummary>; loading: boolean }>()
defineEmits<{ open: [code: string] }>()

const displayTime = (value?: string) => value ? new Date(value).toLocaleString('zh-CN') : '时间待定'
</script>

<template>
  <div v-if="loading" class="empty" role="status" aria-live="polite"><strong>正在加载赛事</strong><span>正在同步最新报名与赛程状态…</span></div>
  <div v-else-if="!items.length" class="empty"><strong>这里暂时没有赛事</strong><span>可以调整筛选条件，或稍后再来查看。</span></div>
  <div v-else class="summary-grid">
    <button v-for="item in items" :key="item.code" class="summary-card" type="button" @click="$emit('open', item.code)">
      <span class="summary-top"><span><small>{{ item.code }}</small><b>{{ item.name }}</b></span><i v-if="'requiresAction' in item && item.requiresAction">需要处理</i></span>
      <span class="summary-tags"><em>{{ tournamentStatusText(item.status) }}</em><em>{{ tournamentFormatText(item.format) }}</em><em v-if="'viewerRole' in item" class="role">{{ tournamentViewerRoleText(item.viewerRole) }}</em></span>
      <span class="summary-facts"><span><small>计划时间</small><b>{{ displayTime(item.startAt) }}</b></span><span><small>主办者</small><b>{{ item.organizerName }}</b></span><span><small>席位</small><b>{{ item.counts.active }}/{{ item.maxPlayers }} 人<span v-if="item.counts.waitlisted"> · 候补 {{ item.counts.waitlisted }}</span></b></span></span>
      <span class="open-cue">查看赛事 <b aria-hidden="true">→</b></span>
    </button>
  </div>
</template>

<style scoped>
.summary-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(310px,1fr));gap:12px}.summary-card{display:flex;min-width:0;min-height:244px;flex-direction:column;gap:14px;padding:18px;text-align:left;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-md);background:linear-gradient(145deg,var(--l12-ui-panel-raised),var(--l12-ui-panel));color:var(--l12-ui-text);box-shadow:var(--l12-ui-shadow-panel);cursor:pointer;transition:border-color .16s ease,transform .16s ease,background .16s ease}.summary-card:hover{border-color:var(--l12-ui-accent-line);background:linear-gradient(145deg,#13212a,var(--l12-ui-panel));transform:translateY(-2px)}.summary-top{display:flex;min-width:0;align-items:flex-start;justify-content:space-between;gap:12px}.summary-top>span{display:grid;min-width:0;gap:5px}.summary-top small{color:var(--l12-ui-info);font:800 12px ui-monospace,monospace;letter-spacing:.09em}.summary-top b{overflow-wrap:anywhere;font-size:18px;line-height:1.35}.summary-top i{flex:0 0 auto;padding:4px 7px;border:1px solid var(--l12-ui-accent-line);border-radius:var(--l12-ui-radius-sm);background:#2f2814;color:#f0d47d;font-size:12px;font-style:normal;font-weight:900}.summary-tags{display:flex;flex-wrap:wrap;gap:6px}.summary-tags em{padding:4px 7px;border:1px solid var(--l12-ui-line-strong);border-radius:999px;background:var(--l12-ui-control);color:var(--l12-ui-text-soft);font-size:12px;font-style:normal}.summary-tags .role{border-color:#32767a;color:#87dce0}.summary-facts{display:grid;grid-template-columns:1fr 1fr;gap:10px 14px}.summary-facts>span{display:grid;min-width:0;gap:3px}.summary-facts>span:first-child{grid-column:1/-1}.summary-facts small{color:var(--l12-ui-text-muted);font-size:12px}.summary-facts b{overflow-wrap:anywhere;color:var(--l12-ui-text-soft);font-size:13px;font-weight:700}.open-cue{display:flex;align-items:center;justify-content:space-between;margin-top:auto;padding-top:12px;border-top:1px solid var(--l12-ui-line);color:var(--l12-ui-accent);font-size:13px;font-weight:900}.open-cue b{font-size:18px}.empty{display:grid;min-height:190px;place-items:center;align-content:center;gap:7px;padding:28px;border:1px dashed var(--l12-ui-line-strong);border-radius:var(--l12-ui-radius-md);background:rgba(11,18,24,.62);text-align:center}.empty strong{color:var(--l12-ui-text-soft)}.empty span{color:var(--l12-ui-text-muted);font-size:13px}
@media(max-width:700px){.summary-grid{grid-template-columns:1fr;gap:8px}.summary-card{min-height:0;padding:14px;box-shadow:none}.summary-card:hover{transform:none}.summary-facts{grid-template-columns:1fr 1fr}.open-cue{min-height:44px}.empty{min-height:150px}}
@media(max-width:390px){.summary-facts{grid-template-columns:1fr}.summary-facts>span:first-child{grid-column:auto}.summary-top{align-items:flex-start}.summary-top i{max-width:76px;text-align:center}}
</style>
