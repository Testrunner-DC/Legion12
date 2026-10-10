<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  label: string
  variant?: 'tier' | 'faction-title' | 'master-title'
  faction?: string | null
  compact?: boolean
}>(), {
  variant: 'faction-title',
  faction: '',
  compact: false,
})

const factionKey = computed(() => {
  const value = (props.faction ?? '').trim().toLocaleLowerCase()
  if (value === 'order' || value.includes('秩序')) return 'order'
  if (value === 'chaos' || value.includes('混沌')) return 'chaos'
  if (value === 'fate' || value.includes('命运')) return 'fate'
  return 'neutral'
})

const siteBrandIcon = '/favicon.png'
const badgeIcon = computed(() => props.variant === 'faction-title' ? '◆' : '')
</script>

<template>
  <span class="ranked-identity-badge" :class="[`is-${variant}`, `faction-${factionKey}`, { compact }]" :title="label">
    <img v-if="variant === 'master-title'" class="identity-brand-logo" :src="siteBrandIcon" alt="" aria-hidden="true" />
    <i v-else-if="badgeIcon" aria-hidden="true">{{ badgeIcon }}</i>
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
  --identity-accent: #9aa6ab;
  --identity-text: #e8edef;
  --identity-surface: #182126;
  --identity-deep: #0c1114;
  --identity-glow: rgba(154, 166, 171, .28);
}

.ranked-identity-badge.faction-order {
  --identity-accent: #72c8e8;
  --identity-text: #dff7ff;
  --identity-surface: #12394a;
  --identity-deep: #081c27;
  --identity-glow: rgba(71, 183, 226, .34);
}

.ranked-identity-badge.faction-chaos {
  --identity-accent: #ed7d87;
  --identity-text: #ffe8ea;
  --identity-surface: #4a1b24;
  --identity-deep: #210a0f;
  --identity-glow: rgba(224, 75, 91, .34);
}

.ranked-identity-badge.faction-fate {
  --identity-accent: #c8a0ea;
  --identity-text: #f5eaff;
  --identity-surface: #38234e;
  --identity-deep: #180d25;
  --identity-glow: rgba(174, 116, 224, .36);
}

.ranked-identity-badge.is-tier {
  padding: 3px 6px !important;
  border: 1px solid color-mix(in srgb, var(--identity-accent) 68%, #1a2024);
  border-radius: 3px;
  background: linear-gradient(135deg, var(--identity-surface), var(--identity-deep));
  box-shadow: inset 0 1px rgba(255, 255, 255, .08);
  color: var(--identity-text);
  font-size: var(--ranked-tier-badge-font-size, 14px) !important;
}

.ranked-identity-badge.is-faction-title {
  gap: 5px;
  padding: 5px 10px !important;
  border: 1px solid var(--identity-accent) !important;
  border-radius: 5px;
  background: linear-gradient(135deg, color-mix(in srgb, var(--identity-surface) 88%, white) 0%, var(--identity-surface) 48%, var(--identity-deep) 100%) !important;
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--identity-accent) 40%, #050708), 0 0 13px var(--identity-glow), inset 0 1px rgba(255, 255, 255, .18);
  color: var(--identity-text) !important;
  font-size: var(--ranked-title-badge-font-size, 14px) !important;
  letter-spacing: .04em;
  text-shadow: 0 1px 2px #000;
}

.ranked-identity-badge.is-master-title {
  gap: 5px;
  padding: 5px 10px !important;
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
  --ranked-tier-badge-font-size: 15px;
  --ranked-title-badge-font-size: 15px;
}

.ranked-identity-badge.is-tier.compact {
  padding: 2px 5px !important;
}

.ranked-identity-badge.is-faction-title.compact,
.ranked-identity-badge.is-master-title.compact {
  gap: 3px;
  padding: 2px 6px !important;
}

.ranked-identity-badge > i {
  flex: 0 0 auto;
  color: currentColor;
  font-size: inherit !important;
  font-style: normal;
  filter: drop-shadow(0 0 4px currentColor);
}

.ranked-identity-badge > .identity-brand-logo {
  width: 1.05em;
  height: 1.05em;
  flex: 0 0 1.05em;
  border: 0;
  border-radius: 0;
  object-fit: contain;
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
