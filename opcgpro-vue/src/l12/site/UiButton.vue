<script setup lang="ts">
withDefaults(defineProps<{
  type?: 'button' | 'submit' | 'reset'
  tone?: 'neutral' | 'primary' | 'danger' | 'warning'
  selected?: boolean
  busy?: boolean
  disabled?: boolean
}>(), { type: 'button', tone: 'neutral', selected: undefined, busy: false, disabled: false })
</script>

<template>
  <button class="ui-button" :type="type" :data-ui-tone="tone" :aria-pressed="selected"
    :aria-busy="busy || undefined" :disabled="disabled || busy"><slot/></button>
</template>
