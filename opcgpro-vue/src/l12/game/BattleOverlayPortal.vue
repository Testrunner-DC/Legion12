<script setup lang="ts">
import { landscapeTeleportTarget } from '../mobileViewport'
import { useMobileBattleDock } from './mobileBattleDock'
const props = withDefaults(defineProps<{ force?: boolean }>(), { force: false })
const dock = useMobileBattleDock()
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()" :disabled="!props.force && !dock?.utility"><slot /></Teleport>
</template>
