<script setup lang="ts">
import { computed } from 'vue'
import { useMobileBattleDock, type BattleDockLane } from './mobileBattleDock'
const props = defineProps<{ lane: BattleDockLane }>()
const dock = useMobileBattleDock()
const target = computed(() => dock?.[props.lane] ?? null)
</script>

<template>
  <Teleport :to="target || 'body'" :disabled="!target"><slot /></Teleport>
</template>
