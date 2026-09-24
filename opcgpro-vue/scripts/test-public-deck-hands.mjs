import fs from 'node:fs'
import path from 'node:path'

const sourcePath = path.join(process.cwd(), 'src/l12/site/publicDeckHands.ts')
const source = fs.readFileSync(sourcePath, 'utf8')
const executable = source
  .replace(/export function samplePublicDeckOpeningHand\(cardIds: readonly string\[\], random: \(\) => number = Math\.random, size = 6\)/,
    'function samplePublicDeckOpeningHand(cardIds, random = Math.random, size = 6)')
const samplePublicDeckOpeningHand = new Function(`${executable}; return samplePublicDeckOpeningHand`)()

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
if (/localStorage|sessionStorage|platformRequest|fetch\(/.test(source)) throw new Error('起手抽取不得持久化或生成网络记录')

console.log('公开牌库随机起手通过：确定性、6 张、无正文修改、无持久化')
