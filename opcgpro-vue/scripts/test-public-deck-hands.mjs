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
const eligibility = await loadTypeScript('src/l12/openingHandEligibility.ts')
const entry = await loadTypeScript('src/l12/site/publicDeckEntry.ts')
const { samplePublicDeckOpeningHand } = hands.module
const { isNormalOpeningHandCard, normalOpeningHandCopies } = eligibility.module
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

const tombGuard = { id: 'S01-0212', cardType: 'legion', effect: '规则上，此军团构筑时不计入卡组数量，且不能进入手牌和牌库。' }
const multiplyingScarab = { id: 'S02-0201', cardType: 'legion', effect: '规则上，此军团构筑时不计入卡组数量，不能进入手牌和牌库。' }
const normalLegion = { id: 'S01-0202', cardType: 'legion', effect: '普通主牌' }
if (isNormalOpeningHandCard(tombGuard) || isNormalOpeningHandCard(multiplyingScarab)) throw new Error('特殊衍生军团仍可进入起手')
if (!isNormalOpeningHandCard(normalLegion)) throw new Error('普通主牌被错误排除')
const weightedCopies = normalOpeningHandCopies([
  { key: 'tomb', card: tombGuard },
  { key: 'scarab', card: multiplyingScarab },
  { key: 'normal-1', card: normalLegion },
  { key: 'normal-2', card: normalLegion },
], copy => copy.card)
if (JSON.stringify(weightedCopies.map(copy => copy.key)) !== JSON.stringify(['normal-1', 'normal-2']))
  throw new Error('起手资格过滤没有保留普通卡副本权重')
for (let index = 0; index < 10000; index += 1) {
  const sampled = samplePublicDeckOpeningHand(weightedCopies.map(copy => copy.key), () => (index % 997) / 997, 6)
  if (sampled.some(id => id === 'tomb' || id === 'scarab')) throw new Error('确定性大量抽样出现特殊卡')
}

const editorSource = fs.readFileSync('src/l12/L12DeckEditor.vue', 'utf8')
const publicDetailSource = fs.readFileSync('src/l12/site/PublicDeckDetailPage.vue', 'utf8')
if (!editorSource.includes('normalOpeningHandCopies') || !editorSource.includes('eligibleMainDeckCopies'))
  throw new Error('牌库编辑器未使用统一起手资格')
if (!publicDetailSource.includes('normalOpeningHandCopies') || !publicDetailSource.includes('eligibleDeckCopies'))
  throw new Error('公开牌库详情未使用统一起手资格')

const loadedDetails = { guide: { buildIdea: '保留内容' }, versions: [{ version: 1 }] }
const current = { id: 'deck', likes: 1, details: loadedDetails }
const liked = preservePublicDeckDetails(current, { id: 'deck', likes: 2 })
if (liked.details !== loadedDetails || liked.likes !== 2) throw new Error('点赞/复制响应覆盖了已加载详情')
const refreshed = preservePublicDeckDetails(current, { id: 'deck', likes: 3, details: { guide: { buildIdea: '新内容' } } })
if (refreshed.details.guide.buildIdea !== '新内容') throw new Error('接口返回的新详情没有获得优先权')

console.log('公开牌库随机起手与详情保留通过：确定性、无持久化、点赞/复制不清空详情')
