import { readFileSync } from 'node:fs'

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const panel = read('../src/l12/site/AdminArticlesPanel.vue')
const platform = read('../src/l12/platform.ts')
const server = read('../../服务端WebSocket/TwelveLegions/L12WebSocketServer.cs')
const store = read('../../服务端WebSocket/TwelveLegions/L12PlatformStore.ModianImports.cs')

const contracts = [
  [panel.includes('data-ui-contract="modian-draft-import"')
    && panel.includes('同步所选为草稿') && panel.includes('绝不自动发布，也不附加来源链接。'),
  '资讯后台必须明确以人工检查、选择、生成草稿收口，并告知不会发布或附加来源链接'],
  [panel.includes('<article v-for="item in modianPreview.items"')
    && panel.includes(':aria-labelledby="`modian-title-${item.updateId}`"')
    && !panel.includes('<label v-for="item in modianPreview.items"'),
  '同步条目不得用嵌套 label；主选择框必须有独立可访问名称'],
  [panel.includes("item.state === 'remote-changed'") && panel.includes('允许重导')
    && panel.includes('覆盖本地草稿') && panel.includes('window.confirm'),
  '远端变化与覆盖本地编辑必须经过分层显式确认'],
  [platform.includes("'/api/admin/articles/modian/preview'")
    && platform.includes("'/api/admin/articles/modian/import'")
    && platform.includes("headers: { 'Idempotency-Key': idempotencyKey }"),
  '前端同步调用必须使用固定后台入口并携带幂等键'],
  [server.match(/\/api\/admin\/articles\/modian\/(?:preview|import)/g)?.length === 2
    && server.match(/L12Permission\.AdminContentDraft/g)?.length >= 2,
  '摩点检查与导入入口必须受草稿权限保护'],
  [store.includes('row.Link = string.Empty;') && store.includes('ImportSourceFingerprint')
    && !store.includes('SourceUrl'),
  '导入来源标识只能进入私有去重元数据，文章可见链接必须保持为空'],
]

const failures = contracts.filter(([ok]) => !ok).map(([, message]) => message)
if (failures.length) {
  failures.forEach(message => console.error(`FAIL: ${message}`))
  process.exit(1)
}
console.log(`Modian import UI contracts passed (${contracts.length}).`)
