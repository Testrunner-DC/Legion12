<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { rankedApi, type RankedBroadcast } from '@/l12/platform'
const items = ref<RankedBroadcast[]>([])
let timer: number | undefined
async function load() { try { items.value = await rankedApi.broadcasts(20) } catch { items.value = [] } }
onMounted(() => { void load(); timer = window.setInterval(load, 30_000) })
onBeforeUnmount(() => { if (timer) window.clearInterval(timer) })
</script>
<template><div v-if="items.length" class="ranked-ticker" aria-label="排位快讯"><b>排位快讯</b><div><span v-for="item in items" :key="item.id">{{ item.message }}</span></div></div></template>
<style scoped>
.ranked-ticker{position:relative;z-index:30;display:grid;grid-template-columns:auto 1fr;align-items:center;min-height:30px;overflow:hidden;border:1px solid #66572f;background:#12120ee8;color:#f0dda0;font-size:10px}.ranked-ticker>b{position:relative;z-index:2;height:100%;padding:0 12px;background:#b9953f;color:#111;line-height:30px}.ranked-ticker>div{display:flex;width:max-content;animation:ticker 45s linear infinite}.ranked-ticker span{padding:0 42px;white-space:nowrap}@keyframes ticker{from{transform:translateX(70vw)}to{transform:translateX(-100%)}}
</style>
