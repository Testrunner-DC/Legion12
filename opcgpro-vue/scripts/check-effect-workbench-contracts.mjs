import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const panel = read('../src/l12/site/AdminEffectWorkbenchPanel.vue')
const platform = read('../src/l12/platform.ts')
const server = read('../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const store = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.EffectWorkbench.cs')

for (const marker of [
  'data-ui-contract="effect-workbench"',
  '草稿', '结构校验', '场景预演', '人工复核', '差异确认并发布', '发布与回退记录',
  '卡牌资料与生效范围', '能力与效果段', '勘误记录', '呈现样式',
  '按钮 / 弹框 / 动画 / 日志 / 回放文本',
  '所属产品', '收录产品', 'Cost 边界', '响应', '公开分支',
]) assert(panel.includes(marker), `Effect workbench UI is missing ${marker}`)

assert(panel.includes('v-for="option in styles"') && panel.includes('option.desktopPreview')
  && panel.includes('option.mobilePreview') && panel.includes('option.legendBody'),
  'Presentation styles must carry visual legends and desktop/mobile examples')
assert(panel.includes("hasPermission('admin.effects.review')"), 'Workbench mutations must be permission gated')
assert(platform.includes('EffectWorkbenchView') && platform.includes('/workbench/draft')
  && platform.includes('/workbench/validate') && platform.includes('/workbench/review')
  && platform.includes('/workbench/publish') && platform.includes('/workbench/rollback'),
  'Frontend API must expose the complete immutable publication workflow')
assert(server.includes('/api/admin/effect-workbench/styles') && server.includes('/workbench/draft')
  && server.includes('EffectWorkbenchAction(request, cardId, body, "publish")'),
  'Backend API must expose style legends and the workbench workflow')
assert(store.includes('SourceStructureHash(effect)') && store.includes('item.StructureHash.Equals')
  && store.includes('效果一致性开发再发布'),
  'Workbench must bind drafts to existing atoms and reject semantic drift')
assert(store.includes('EffectWorkbenchVersionRow') && store.includes('PublishedVersionId')
  && store.includes('ApplyPublishedSceneTexts'),
  'Published versions must be immutable and project scene text through the existing runtime source')
assert(!store.includes('switch (effect.CardId)') && !store.includes('switch(effect.CardId)'),
  'Workbench must not introduce card-id switches')

console.log('Effect workbench contracts passed: unified draft/validate/preview/review/publish/rollback, atom guards and style legends.')
