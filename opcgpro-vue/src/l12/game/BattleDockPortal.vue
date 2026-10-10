<script setup lang="ts">
import { computed } from 'vue'
import { useMobileBattleDock, type BattleDockLane } from './mobileBattleDock'
import { landscapeTeleportTarget } from '../mobileViewport'
const props = defineProps<{ lane: BattleDockLane }>()
const dock = useMobileBattleDock()
const target = computed(() => dock?.[props.lane] ?? (props.lane === 'route' ? landscapeTeleportTarget() : null))
</script>

<template>
  <Teleport :to="target || 'body'" :disabled="!target"><slot /></Teleport>
</template>
