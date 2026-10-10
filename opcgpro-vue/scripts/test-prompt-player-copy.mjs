import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'

const source = fs.readFileSync('src/l12/game/promptPlayerCopy.ts', 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
const copy = await import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}`)
let assertions = 0
const equal = (actual, expected) => { assert.deepEqual(actual, expected); assertions++ }
const prompt = (extra = {}) => ({ kind: 'card', text: '原始记录文本', validChoices: ['a', 'skip'], minChoose: 1, maxChoose: 1,
  presentation: { title: '选择', situation: '〈天诛〉选择目标。', instruction: '选择1张军团。', choiceConsequences: {} }, ...extra })

for (const phrase of ['不发动本次可选效果。', '不发动当前效果。', '不执行本次可选效果。'])
  equal(copy.compactPromptCopy(phrase), '不发动。')
equal(copy.compactPromptCopy('不执行本次可选段。'), '跳过此段。')
for (const fact of [
  '支付2士气；弃置1张手牌；不支付则不发动。', '本回合兵力+1000；下个结束阶段失效。',
  '对手已选择你的前排左格〈陵墓守卫〉，声明时当前费用2。', '盖伏卡牌仍保持盖伏；不公开手牌。',
  '从左到右先后抽到；先抽完原牌库，再抽底部卡牌。', '取消整次发动；已支付费用不返还。',
  '不受此伤害，不抽牌。', '若对局仍继续，再抽取1张牌。',
  '〈不发动当前效果〉本回合不可进攻。', '不发动当前效果时，仍需弃置2张手牌。',
]) equal(copy.compactPromptCopy(fact), fact)
equal(copy.compactPromptCopy('选择1张军团；实际结果以权威处理为准。'), '选择1张军团')
equal(copy.compactPromptCopy('请选择由哪一方先攻；确认后将继续进行对局准备。'), '请选择由哪一方先攻')
const duplicate = prompt()
const optional = prompt({kind: 'optional'})
optional.presentation.instruction = '请选择一种处理方式。'
equal(copy.promptInstructionCopy(optional), '')
optional.presentation.instruction = '请选择一种处理方式；需弃置1张手牌。'
equal(copy.promptInstructionCopy(optional), '请选择一项；需弃置1张手牌。')
const branchChoice = prompt({ kind: 'option', validChoices: ['top', 'bottom'],
  choiceLabels: { top: '置于牌库顶部', bottom: '置于牌库底部' } })
branchChoice.presentation.instruction = '请决定是否执行〈杨戬〉的效果。'
equal(copy.promptInstructionCopy(branchChoice), '请选择处理方式。')
branchChoice.presentation.instruction = '请决定是否执行〈杨戬〉的效果；已支付2士气不返还。'
equal(copy.promptInstructionCopy(branchChoice), '请决定是否执行〈杨戬〉的效果；已支付2士气不返还。')
optional.presentation.choiceConsequences.skip = '不发动：不受此伤害，不抽牌。'
equal(copy.promptConsequenceCopy(optional, 'skip', '不发动'), '')
optional.presentation.choiceConsequences.pass = '不打出响应牌。'
equal(copy.promptConsequenceCopy(optional, 'pass', '不响应'), '')
duplicate.presentation.choiceConsequences.skip = '不发动本次可选效果。'
equal(copy.promptConsequenceCopy(duplicate, 'skip', '不发动'), '')
duplicate.presentation.choiceConsequences.skip = '不发动；已支付2士气不返还。'
equal(copy.promptConsequenceCopy(duplicate, 'skip', '不发动'), '已支付2士气不返还。')
duplicate.presentation.submissionConsequence = duplicate.presentation.instruction
equal(copy.promptSubmissionCopy(duplicate), '')
duplicate.presentation.instruction = duplicate.presentation.situation
equal(copy.promptInstructionCopy(duplicate), '')
const cost = prompt()
cost.presentation.instruction = '请选择并弃置1张手牌作为费用。'
cost.presentation.submissionConsequence = '弃置所选的1张手牌作为响应费用。'
cost.presentation.paymentStatus = 'pending'
cost.presentation.paymentSummary = '尚未支付弃牌费用；选择手牌并确认后才会弃置。'
equal(copy.promptSubmissionCopy(cost), '')
equal(copy.promptPaymentCopy(cost), '费用待支付')
cost.presentation.submissionConsequence = '弃置所选的2张手牌作为响应费用。'
equal(copy.promptSubmissionCopy(cost), '弃置所选的2张手牌作为响应费用。')
for (const kind of ['response', 'discard-cost', 'target', 'slot', 'card', 'optional', 'response-target']) {
  const item = prompt({ kind, data: { responseContext: '对手使用〈天诛〉。\n已选目标：你的后排右格〈陵墓守卫〉；声明时当前费用2。\n是否响应？' } })
  const before = JSON.stringify(item)
  equal(copy.promptSituationCopy(item).includes('已选目标：你的后排右格〈陵墓守卫〉；声明时当前费用2。'), true)
  equal(copy.promptSituationCopy(item).includes('是否响应？'), kind === 'response')
  item.presentation.situation += '\n' + item.data.responseContext
  equal(copy.promptSituationCopy(item).split('已选目标').length, 2)
  item.presentation.situation = JSON.parse(before).presentation.situation
  equal(JSON.stringify(item), before)
}
const stackedResponse = prompt({ kind: 'response', data: {}, presentation: {
  situation: '对手使用〈天诛〉。\n已选目标：我方前排左格〈陵墓守卫〉。\n是否响应？\n\n你使用〈守护〉。\n是否响应？',
} })
equal(copy.promptSituationCopy(stackedResponse),
  '对手使用〈天诛〉。\n已选目标：我方前排左格〈陵墓守卫〉。\n\n你使用〈守护〉。\n是否响应？')
equal(stackedResponse.presentation.situation.match(/是否响应？/g).length, 2)
const resourcePayment = prompt({ kind: 'resource-payment', minChoose: 2, maxChoose: 2,
  validChoices: ['morale-a', 'god-power', 'cancel'], presentation: {
    title: '光之剑', situation: '〈光之剑〉需要支付2份资源才能继续。可用资源为士气、神力。',
    instruction: '请选择恰好2份可用资源并确认；也可以取消当前操作。',
    paymentStatus: 'pending', paymentSummary: '2份资源（士气、神力）',
    submissionConsequence: '支付所选的2份资源。',
    choiceConsequences: { cancel: '取消整次打出，不支付任何资源。' },
  } })
equal(copy.promptSituationCopy(resourcePayment), '')
equal(copy.promptInstructionCopy(resourcePayment), '请选择要支付的资源。')
equal(copy.promptPaymentCopy(resourcePayment), '待支付：2份资源（士气、神力）')
equal(copy.promptSubmissionCopy(resourcePayment), '确认后支付所选资源。')
equal(copy.promptConsequenceCopy(resourcePayment, 'cancel', '取消'), '取消整次打出，不支付任何资源。')
resourcePayment.presentation.situation += '已支付费用不返还。'
equal(copy.promptSituationCopy(resourcePayment), resourcePayment.presentation.situation)
resourcePayment.presentation.situation = '〈光之剑〉需要支付2份资源才能继续。可用资源为士气、神力。'
resourcePayment.presentation.instruction += '已支付费用不返还。'
equal(copy.promptInstructionCopy(resourcePayment), resourcePayment.presentation.instruction)
for (const [status, summary, expected] of [['paid', '已支付2士气', '已支付2士气'], ['pending', '弃置1张手牌', '待支付：弃置1张手牌'], ['pending', '尚未支付弃牌费用', '尚未支付弃牌费用']]) {
  const item = prompt(); item.presentation.paymentStatus = status; item.presentation.paymentSummary = summary
  equal(copy.promptPaymentCopy(item), expected)
}
equal(copy.promptSituationCopy(prompt({ presentation: undefined, data: { effectText: '规则全文；公开2张牌。' } })), '规则全文；公开2张牌。')
equal(copy.mulliganCopy(3, true), '已选 3 张，换成等量新牌；未选则保留全部。超时保留原手牌。')

// Audited shared narrative templates, including the actual rejected families.
const optionalCopy = (situation, instruction, decline) => prompt({ kind: 'optional',
  validChoices: ['yes', 'no'], choiceLabels: { yes: '发动', no: '不发动' },
  presentation: { situation, instruction, choiceConsequences: { no: decline } } })
for (const [wrapper, instruction, decline, expectedInstruction] of [
  ['〈圣女贞德〉的登场时效果正在结算。', '请选择1张手牌弃置并支付费用，或选择“不发动”；不发动时不会弃牌，也不会获得保护。', '不支付弃牌费用，不获得本次无法被进攻的保护。', '请选择1张手牌弃置并支付费用'],
  ['〈神箭奥德尔〉的登场时效果可以选择是否发动。', '请选择“发动”并承担1点主宰伤害，或选择“不发动”结束本效果。', '不发动：我方主宰不受此伤害，不抽牌，并结束本效果。', '承担1点主宰伤害。'],
  ['〈万物统御之戒〉的登场时效果正在结算。', '请选择“发动”继续支付费用，或选择“不发动”结束本次登场时效果。', '不支付费用，直接结束〈万物统御之戒〉的登场时效果。', '支付费用。'],
  ['〈血斧艾瑞克〉的阵亡时效果正在选择登场对象。', '请选择1张当前符合条件的墓地军团进入位置选择，或选择“不发动”结束本效果。', '结束本效果，不选择登场对象，也不进入位置选择。', '请选择1张当前符合条件的墓地军团进入位置选择。'],
  ['〈亚瑟王〉的登场时效果正在结算。', '请选择“发动”并消耗1符文，或选择“不发动”且不消耗符文。', '不发动本次登场时效果，也不消耗符文。', '消耗1符文。'],
  ['〈赫拉克勒斯〉的登场时效果正在结算。', '请选择是否发动；不发动时不会抽牌，也不会弃牌。', '不发动本次登场时效果，不抽牌也不弃牌。', ''],
  ['〈赫拉克勒斯·晋升〉的登场时效果正在结算。', '请选择是否发动；不发动时双方主宰都不会受到本次伤害。', '不发动本次登场时效果，双方主宰的生命不变。', ''],
  ['〈珀尔修斯〉的登场时效果正在结算。', '请选择1张手牌弃置并支付费用，或选择“不发动”；不发动时手牌和墓地都不会改变。', '不支付弃牌费用，不将〈珀尔修斯·晋升〉加入手牌。', '请选择1张手牌弃置并支付费用'],
  ['〈伊姆何泰普〉的登场时效果正在结算。', '请选择1张符合条件的墓地军团，或选择“不发动”；不发动时墓地和手牌都不会改变。', '不发动本次回收效果，墓地和手牌都不改变。', '请选择1张符合条件的墓地军团'],
  ['〈罗宾汉〉的登场时效果正在结算。', '请选择1张〈侍从骑士〉，或选择“不发动”。', '不发动本次登场时效果，不移动任何〈侍从骑士〉。', '请选择1张〈侍从骑士〉。'],
  ['〈墓地回收来源〉的阵亡时效果正在选择墓地回收对象。', '请选择1张当前符合条件的墓地卡牌，或选择“不发动”结束本效果。', '结束本效果，不移动任何墓地卡牌。', '请选择1张当前符合条件的墓地卡牌。'],
  ['〈赫拉克勒斯·晋升〉的晋升登场效果正在结算。', '请选择1张手牌中的军团展示并放回牌库顶部，或选择“不发动”。', '不支付展示并回顶的费用，也不选择击杀目标。', '请选择1张手牌中的军团展示并放回牌库顶部。'],
  ['〈珀尔修斯·晋升〉的晋升登场效果正在结算。', '请选择1张对方休整军团，或选择“不发动”；本效果没有额外费用。', '不发动本次锁定效果，不影响任何军团。', '请选择1张对方休整军团；本效果没有额外费用。'],
]) {
  const facts = '支付1士气；本回合兵力-1000；下个我方回合开始前无法被进攻。'
  const item = optionalCopy(wrapper + facts, instruction, decline)
  const before = JSON.stringify(item)
  equal(copy.promptSituationCopy(item), facts)
  equal(copy.promptInstructionCopy(item), expectedInstruction)
  equal(copy.promptConsequenceCopy(item, 'no', '不发动'), '')
  equal(JSON.stringify(item), before)
}
for (const consequence of [
  '已支付2士气不返还。', '已经处理的全体兵力-1000不会撤销。',
  '不将卡牌加入手牌，但仍会洗牌。', '不发动当前效果时，仍需弃置2张手牌。',
  '原致命结果继续结算。', '该军团直到下个我方回合开始前无法被进攻。',
  '所选目标失效时不会改选其他目标。', '不支付费用时仍会受到1点伤害。',
  '不移动任何〈侍从骑士〉，但仍会洗牌。',
]) equal(copy.promptConsequenceCopy(optionalCopy('', '', consequence), 'no', '不发动'), consequence)
equal(copy.promptConsequenceCopy(optionalCopy('', '', '结束本效果；已经弃置的牌不会返回。'), 'no', '不发动'), '已经弃置的牌不会返回。')
const magician = optionalCopy('〈宫廷魔术师〉的登场时效果正在结算。选择1张反击战术，也可以不发动。', '请选择1张反击战术，或选择“不发动”。', '不选择对象，结束〈宫廷魔术师〉的登场时效果。')
equal(copy.promptSituationCopy(magician), '选择1张反击战术。')
equal(copy.promptConsequenceCopy(magician, 'no', '不发动'), '')
const prose = '〈测试〉的登场时效果正在结算。卡牌原文不作改写。'
equal(copy.promptSituationCopy(prompt({presentation: undefined, data: {effectText: prose}})), prose)

// Inventory every server prompt producer, not a small list of reported cards.
const root = '../服务端WebSocket/TwelveLegions'
const inventory = []
for (const file of fs.readdirSync(root).filter(file => file.endsWith('.cs'))) {
  const text = fs.readFileSync(path.join(root, file), 'utf8')
  text.split('\n').forEach((line, index) => {
    if (/\b(?:CreatePrompt|CreateAnonymousHandChoicePrompt|CreateActivationStepPrompt|WithPromptNarrative)\s*\(/.test(line))
      inventory.push({ file, line: index + 1, source: line.trim() })
  })
}
assert(inventory.length > 100, 'Full producer inventory must include the shared engine and card families')
const consumers = ['PromptOverlay.vue', 'GameBoard.vue', 'GameActions.vue']
for (const file of consumers) assert(fs.readFileSync(`src/l12/game/${file}`, 'utf8').includes("'./promptPlayerCopy'"), `${file} bypasses shared copy`)
assert(fs.readFileSync('src/l12/game/GameBoard.vue','utf8').includes('prompt.presentation?.title?.trim() || prompt.text.trim()'), 'Legacy inline prompts must retain their task title after instruction deduplication')
const otherSurfaces = ['BattleUtilityDock.vue', 'DefenseDecisionExplanation.vue', 'MasterOverlay.vue', 'PlayerMat.vue', 'GraveyardOverlay.vue', '../GamePage.vue']
const utility = fs.readFileSync('src/l12/game/BattleUtilityDock.vue', 'utf8')
assert(!utility.includes('权威') && !utility.includes('独立进入“对局治理”'), 'Match confirmation copy must not expose processing internals')
assert(utility.includes('接受：本局以平局结束。拒绝：继续对局。'))
assert(utility.includes('接受或拒绝后均不能再申请'), 'Draw frequency restriction is required information')
const out = '../artifacts/player-prompt-copy'
fs.mkdirSync(out, { recursive: true })
fs.writeFileSync(`${out}/inventory.json`, JSON.stringify({ producers: inventory, consumers, otherSurfaces, exclusions: ['battle logs', 'admin', 'catalog / rule prose', 'commands / engine settlement'], assertions }, null, 2))
console.log(`Prompt copy: ${assertions} assertions; ${inventory.length} producer sites in ${new Set(inventory.map(item => item.file)).size} files inventoried.`)
