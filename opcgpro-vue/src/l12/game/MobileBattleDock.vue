<script setup lang="ts">
import { onBeforeUnmount, type ComponentPublicInstance } from 'vue'
import { landscapeTeleportTarget } from '../mobileViewport'
import { useMobileBattleDock, type BattleDockLane } from './mobileBattleDock'
const dock = useMobileBattleDock()!
function bind(lane: BattleDockLane, element: Element | ComponentPublicInstance | null) {
  dock[lane] = element instanceof HTMLElement ? element : null
}
onBeforeUnmount(() => { for (const lane of ['tools', 'context', 'primary', 'utility'] as const) dock[lane] = null })
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <aside class="mobile-battle-dock" aria-label="对战操作停靠区" data-ui-contract="mobile-battle-dock">
      <div :ref="element => bind('tools', element)" class="mobile-battle-dock__tools" aria-label="对局辅助功能" />
      <div :ref="element => bind('context', element)" class="mobile-battle-dock__context" aria-label="当前操作" aria-live="polite" />
      <div :ref="element => bind('primary', element)" class="mobile-battle-dock__primary" />
      <div :ref="element => bind('utility', element)" class="mobile-battle-dock__utility" />
    </aside>
  </Teleport>
</template>

<style src="./MobileBattleDock.css"></style>
