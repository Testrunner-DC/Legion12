<script setup lang="ts">
withDefaults(defineProps<{
  label: string
  variant?: 'tier' | 'title'
  compact?: boolean
}>(), {
  variant: 'title',
  compact: false,
})
</script>

<template>
  <span class="ranked-identity-badge" :class="[`is-${variant}`, { compact }]" :title="label">
    <i v-if="variant === 'title'" aria-hidden="true">✦</i>
    <span>{{ label }}</span>
  </span>
</template>

<style scoped>
.ranked-identity-badge {
  position: relative;
  display: inline-flex !important;
  box-sizing: border-box;
  width: max-content;
  max-width: none;
  min-width: 0;
  flex-shrink: 0;
  align-items: center;
  margin: 0 !important;
  font-style: normal;
  font-weight: 900;
  line-height: 1.2;
  white-space: nowrap;
}

.ranked-identity-badge.is-tier {
  padding: 3px 6px;
  border: 1px solid #82663a;
  border-radius: 3px;
  background: #261b0c;
  color: #f2d27a;
  font-size: var(--ranked-tier-badge-font-size, 14px) !important;
}

.ranked-identity-badge.is-title {
  gap: 5px;
  padding: 5px 10px;
  border: 1px solid #f1bd4a !important;
  border-radius: 5px;
  background: linear-gradient(135deg, #b47716 0%, #6f3d08 48%, #3a1d02 100%) !important;
  box-shadow: 0 0 0 1px #5b3208, 0 0 16px #e8a12f78, inset 0 1px #fff1a477;
  color: #fff4b5 !important;
  font-size: var(--ranked-title-badge-font-size, 14px) !important;
  letter-spacing: .04em;
  text-shadow: 0 1px 2px #000;
}

.ranked-identity-badge.compact {
  --ranked-tier-badge-font-size: max(15px, calc(var(--l12-board-copy, 13px) - 3px));
  --ranked-title-badge-font-size: max(15px, calc(var(--l12-board-copy, 13px) - 3px));
}

.ranked-identity-badge.is-tier.compact {
  padding: 2px 5px;
}

.ranked-identity-badge.is-title.compact {
  gap: 3px;
  padding: 2px 6px;
}

.ranked-identity-badge > i {
  flex: 0 0 auto;
  color: #fff0a0;
  font-size: inherit !important;
  font-style: normal;
  filter: drop-shadow(0 0 4px #ffd047);
}

.ranked-identity-badge > span {
  min-width: max-content;
  overflow: visible;
  color: inherit !important;
  font-size: inherit !important;
  text-overflow: clip;
  white-space: nowrap;
}
</style>
