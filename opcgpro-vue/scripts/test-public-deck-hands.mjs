import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'

async function loadTypeScript(relativePath) {
  const sourcePath = path.join(process.cwd(), relativePath)
  const source = fs.readFileSync(sourcePath, 'utf8')
  const javascript = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return { source, module: await import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}`) }
}

const hands = await loadTypeScript('src/l12/site/publicDeckHands.ts')
const entry = await loadTypeScript('src/l12/site/publicDeckEntry.ts')
const { samplePublicDeckOpeningHand } = hands.module
const { preservePublicDeckDetails } = entry.module

const values = ['A', 'A', 'B', 'C', 'D', 'E', 'F']
const sequence = [0.1, 0.8, 0.2, 0.7, 0.3, 0.6]
const draw = () => {
  let index = 0
  return samplePublicDeckOpeningHand(values, () => sequence[index++], 6)
}
const first = draw()
const second = draw()
if (JSON.stringify(first) !== JSON.stringify(second)) throw new Error('固定随机序列没有得到稳定起手')
if (first.length !== 6) throw new Error('起手数量不是 6')
if (JSON.stringify(values) !== JSON.stringify(['A', 'A', 'B', 'C', 'D', 'E', 'F'])) throw new Error('抽取修改了公开牌库正文')
if (/localStorage|sessionStorage|platformRequest|fetch\(/.test(hands.source)) throw new Error('起手抽取不得持久化或生成网络记录')

const loadedDetails = { guide: { buildIdea: '保留内容' }, versions: [{ version: 1 }] }
const current = { id: 'deck', likes: 1, details: loadedDetails }
const liked = preservePublicDeckDetails(current, { id: 'deck', likes: 2 })
if (liked.details !== loadedDetails || liked.likes !== 2) throw new Error('点赞/复制响应覆盖了已加载详情')
const refreshed = preservePublicDeckDetails(current, { id: 'deck', likes: 3, details: { guide: { buildIdea: '新内容' } } })
if (refreshed.details.guide.buildIdea !== '新内容') throw new Error('接口返回的新详情没有获得优先权')

console.log('公开牌库随机起手与详情保留通过：确定性、无持久化、点赞/复制不清空详情')
