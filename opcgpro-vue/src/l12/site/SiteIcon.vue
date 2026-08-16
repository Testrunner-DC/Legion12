<script setup lang="ts">
defineProps<{ name: string }>()
</script>

<template>
  <svg class="site-icon" viewBox="0 0 24 24" aria-hidden="true">
    <g v-if="name === 'home'"><path d="m3 11 9-8 9 8M5 10v11h14V10M9 21v-7h6v7"/></g>
    <g v-else-if="name === 'news'"><path d="M5 3h14v18H5zM8 7h8M8 11h8M8 15h5"/></g>
    <g v-else-if="name === 'battle'"><path d="m14.5 5.5 4-4 1 3 3 1-4 4M13 7l4 4M3 21l7.5-7.5M4.5 1.5l15 15M1.5 4.5l3-3 4 4-3 3M15.5 15.5l3-3 4 4-3 3"/></g>
    <g v-else-if="name === 'tournament'"><path d="M7 3h10v4c0 4-2 7-5 7s-5-3-5-7zM7 5H3v2c0 3 2 5 5 5M17 5h4v2c0 3-2 5-5 5M12 14v4M8 21h8M9 18h6"/></g>
    <g v-else-if="name === 'decks'"><rect x="5" y="3" width="13" height="17" rx="1"/><path d="M9 7h5M9 11h5M2 6v15h13"/></g>
    <g v-else-if="name === 'friends'"><circle cx="9" cy="8" r="3"/><path d="M3 20c0-4 2.5-6 6-6s6 2 6 6M16 7h5M18.5 4.5v5"/></g>
    <g v-else-if="name === 'archive'"><path d="M4 4.5A3.5 3.5 0 0 1 7.5 1H20v18H7.5A3.5 3.5 0 0 0 4 22.5zM4 4.5v18M8 6h8M8 10h7"/></g>
    <g v-else-if="name === 'rules'"><path d="M4 3h16v18H4zM8 7h8M8 11h8M8 15h5"/><path d="m15 16 1.5 1.5L20 14"/></g>
    <g v-else-if="name === 'ranking'"><path d="M4 21V11h4v10M10 21V6h4v15M16 21V2h4v19M2 21h20"/></g>
    <g v-else-if="name === 'profile'"><circle cx="12" cy="8" r="4"/><path d="M4 22c.5-5 3-8 8-8s7.5 3 8 8"/></g>
    <g v-else-if="name === 'records'"><path d="M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 7v5l3 2"/></g>
    <g v-else-if="name === 'settings'"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3V2.8h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1z"/></g>
    <g v-else-if="name === 'updates'"><path d="M6 2h9l4 4v16H6zM14 2v5h5M9 12h7M9 16h7"/></g>
    <g v-else-if="name === 'online'"><circle cx="9" cy="8" r="3"/><path d="M3 20c0-4 2.5-6 6-6 2.3 0 4.2.9 5.2 2.6M17 10a3 3 0 1 0 0-6M16 14c3.2 0 5 2 5 5"/></g>
    <g v-else-if="name === 'connection'"><path d="M2 8.8a15 15 0 0 1 20 0M5.5 12.3a10 10 0 0 1 13 0M9 15.8a5 5 0 0 1 6 0"/><circle cx="12" cy="20" r="1"/></g>
  </svg>
</template>

<style scoped>.site-icon{width:21px;height:21px;fill:none;stroke:currentColor;stroke-width:1.7;stroke-linecap:round;stroke-linejoin:round}</style>
