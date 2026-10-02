<script setup lang="ts">
withDefaults(defineProps<{
  label?: string
  summary: string
  sample?: string
  items?: string[]
}>(), {
  label: '统计范围',
  sample: '',
  items: () => [],
})
</script>

<template>
  <aside class="statistics-scope" aria-label="统计口径">
    <div class="statistics-scope-summary">
      <small>{{ label }}</small>
      <b>{{ summary }}</b>
      <span v-if="sample">{{ sample }}</span>
    </div>
    <details v-if="items.length">
      <summary>查看统计口径</summary>
      <ul><li v-for="item in items" :key="item">{{ item }}</li></ul>
    </details>
  </aside>
</template>

<style scoped>
.statistics-scope{box-sizing:border-box;display:grid;min-width:0;max-width:100%;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:10px 14px;padding:10px 12px;border:1px solid #33434d;background:#0b151d;color:#edf1ed;font-family:'Microsoft YaHei','微软雅黑',system-ui,sans-serif}.statistics-scope-summary{display:flex;min-width:0;align-items:baseline;flex-wrap:wrap;gap:5px 10px}.statistics-scope-summary small{color:#79cbd0;font-size:12px;font-weight:900}.statistics-scope-summary b{font-size:13px;overflow-wrap:anywhere}.statistics-scope-summary span{color:#8d9ba0;font-size:12px;overflow-wrap:anywhere}.statistics-scope details{min-width:0}.statistics-scope details[open]{grid-column:1/-1}.statistics-scope summary{width:max-content;max-width:100%;min-height:32px;box-sizing:border-box;margin-left:auto;padding:7px 9px;border:1px solid #52616a;background:#071016;color:#d9c678;font-size:12px;font-weight:900;cursor:pointer;list-style:none}.statistics-scope summary::-webkit-details-marker{display:none}.statistics-scope details[open] summary{border-color:#9a8038}.statistics-scope ul{box-sizing:border-box;width:100%;margin:8px 0 0;padding:10px 12px 10px 30px;border-top:1px solid #35454e;color:#aab4b7;font-size:12px;line-height:1.65}.statistics-scope li+li{margin-top:4px}@media(max-width:700px){.statistics-scope{grid-template-columns:1fr}.statistics-scope details{width:100%}.statistics-scope summary{display:grid;width:100%;place-items:center}}
</style>
