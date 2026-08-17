<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionEvent, Phase } from '../types'

const props = defineProps<{ events: ActionEvent[] }>()
const emit = defineEmits<{ phaseChange: [phase: Phase | null] }>()
type Stage = { phase: Phase; title: string; details: string[] }
const current = ref<Stage | null>(null)
const queue: Stage[] = []
let lastSequence = 0
let timer: ReturnType<typeof setTimeout> | null = null
let running = false

function phaseFrom(text: string): Phase | null {
  if (text.includes('触发天灾')) return 'Disaster'
  if (text.includes('重置阶段')) return 'Reset'
  if (text.includes('抽牌阶段')) return 'Draw'
  if (text.includes('士气阶段')) return 'Morale'
  if (text.includes('主要阶段')) return 'Main'
  if (text.includes('结束阶段')) return 'End'
  return null
}
function enqueue(events: ActionEvent[]) {
  let stage: Stage | null = null
  for (const event of events.sort((a, b) => a.sequence - b.sequence)) {
    if (event.type === 'phase') {
      const phase = phaseFrom(event.text)
      if (!phase) continue
      stage = { phase, title: event.text, details: [] }
      queue.push(stage)
    } else if (stage && ['phase-detail', 'draw-skipped'].includes(event.type)) {
      stage.details.push(event.text)
    }
  }
  playNext()
}
function playNext() {
  if (running || !queue.length) return
  running = true
  current.value = queue.shift()!
  emit('phaseChange', current.value.phase)
  timer = setTimeout(() => {
    running = false
    if (queue.length) playNext()
    else { current.value = null; emit('phaseChange', null) }
  }, current.value.phase === 'Main' ? 260 : 400)
}

onMounted(() => { lastSequence = Math.max(0, ...props.events.map(event => event.sequence)) })
watch(() => props.events.map(event => event.sequence).join(','), () => {
  const fresh = props.events.filter(event => event.sequence > lastSequence)
  if (!fresh.length) return
  lastSequence = Math.max(lastSequence, ...fresh.map(event => event.sequence))
  enqueue(fresh)
})
onBeforeUnmount(() => { if (timer) clearTimeout(timer) })
</script>

<template><span class="phase-playback-silent" aria-hidden="true" /></template>

<style scoped>.phase-playback-silent{display:none}</style>
