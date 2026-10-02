<script setup lang="ts">
withDefaults(defineProps<{
  kind?: 'info' | 'success' | 'warning' | 'error' | 'empty'
  busy?: boolean
}>(), { kind: 'info', busy: false })
</script>

<template>
  <div class="ui-notice" :data-ui-kind="kind" :role="kind === 'error' ? 'alert' : kind === 'empty' ? undefined : 'status'"
    :aria-live="kind === 'error' ? 'assertive' : kind === 'empty' ? undefined : 'polite'"
    :aria-busy="busy || undefined" aria-atomic="true"><div class="ui-notice-copy"><slot/></div><div v-if="$slots.action" class="ui-notice-action"><slot name="action"/></div></div>
</template>
