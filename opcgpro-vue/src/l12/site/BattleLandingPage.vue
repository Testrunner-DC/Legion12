<script setup lang="ts">
const entries = [
  { to: '/battle/lobby', label: '大厅', description: '创建友谊房、加入房间、观战或进入单人测试沙盒。' },
  { to: '/battle/tournaments', label: '赛事中心', description: '报名、组织与查看当前及已结束赛事。' },
  { to: '/decks?from=%2Fbattle', label: '牌库', description: '选择用于对战的牌库，或进入牌库编辑器。' },
  { to: '/battle/rankings', label: '排行榜', description: '查看玩家排名、主宰数据与对阵矩阵。' },
  { to: '/battle/friends', label: '好友', description: '管理好友、申请并直接邀请好友进入对战房间。' },
  { to: '/battle/records', label: '对局记录', description: '仅查看、导出并回放属于当前账号的对局。' },
]
</script>

<template>
  <div class="battle-landing">
    <header><small>BATTLE</small><h1>对战</h1><p>房间、赛事、好友、排行与复盘统一从这里进入。</p></header>
    <section class="battle-grid"><router-link v-for="entry in entries" :key="entry.to" :to="entry.to"><b>{{ entry.label }}</b><span>{{ entry.description }}</span><i>进入 →</i></router-link></section>
  </div>
</template>

<style scoped>
.battle-landing{width:min(1120px,calc(100% - 40px));min-height:100%;margin:auto;padding:42px 0 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.battle-landing header small{color:#52c3ca;font:900 9px monospace;letter-spacing:.2em}.battle-landing h1{margin:6px 0;font-size:34px}.battle-landing header p{margin:0;color:#7d8990;font-size:12px}.battle-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:26px}.battle-grid a{position:relative;min-height:150px;padding:24px;border:1px solid rgba(235,230,216,.18);background:linear-gradient(135deg,#111a23,#0b1218);color:#f2eee5;text-decoration:none}.battle-grid a:hover{border-color:#52c3ca;background:linear-gradient(135deg,#153039,#11161b)}.battle-grid b,.battle-grid span,.battle-grid i{display:block}.battle-grid b{font-size:21px}.battle-grid span{max-width:420px;margin-top:10px;color:#7e8b91;font-size:11px;line-height:1.7}.battle-grid i{position:absolute;right:22px;bottom:20px;color:#dfc16c;font-size:10px;font-style:normal;font-weight:900}@media(max-width:720px){.battle-landing{width:auto;padding:24px 12px 48px}.battle-grid{grid-template-columns:1fr}.battle-grid a{min-height:130px}}
</style>
