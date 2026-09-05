<script lang="ts">
import { defineComponent, h, type PropType, type VNodeChild } from 'vue'
import type { ArticleInlineRun } from './articleBlocks'

export default defineComponent({
  name: 'ArticleInlineText',
  props: { runs: { type: Array as PropType<ArticleInlineRun[]>, required: true } },
  setup(props) {
    return () => h('span', props.runs.map(run => {
      let content: VNodeChild = run.text
      if (run.bold) content = h('strong', content)
      if (run.italic) content = h('em', content)
      if (run.href) {
        const external = /^https?:\/\//i.test(run.href)
        content = h('a', { href: run.href, target: external ? '_blank' : undefined, rel: external ? 'noopener' : undefined }, content)
      }
      return content
    }))
  },
})
</script>
