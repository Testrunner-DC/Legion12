<script setup lang="ts">
import { landscapeTeleportTarget } from '../mobileViewport'
import { useMobileBattleDock } from './mobileBattleDock'
const dock = useMobileBattleDock()
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()" :disabled="!dock?.utility"><slot /></Teleport>
</template>
