import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'

// Read-only audit of the existing Prompt call sites and engine-exported atomic
// ability inventory. It does not infer rule behavior from card text.
const root = path.resolve(import.meta.dirname, '..')
const server = path.join(root, '服务端WebSocket', 'TwelveLegions')
const sourceInventory = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(root, 'artifacts', 'battle-player-information-stage01', 'effect-ability-inventory.json')
const promptTarget = path.join(root, 'docs', 'l12', 'BATTLE-PLAYER-INFORMATION-PROMPT-MAP-20260928.tsv')
const abilityTarget = path.join(root, 'docs', 'l12', 'BATTLE-PLAYER-INFORMATION-ABILITY-MAP-20260928.tsv')

function walk(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(directory, entry.name)
    return entry.isDirectory() ? walk(full) : entry.isFile() && entry.name.endsWith('.cs') ? [full] : []
  })
}

function callArguments(source, open) {
  const parts = []
  let part = open + 1
  let paren = 1
  let square = 0
  let curly = 0
  let quote = ''
  let verbatim = false
  let lineComment = false
  let blockComment = false
  for (let i = open + 1; i < source.length; i++) {
    const char = source[i]
    const next = source[i + 1]
    if (lineComment) {
      if (char === '\n') lineComment = false
      continue
    }
    if (blockComment) {
      if (char === '*' && next === '/') { blockComment = false; i++ }
      continue
    }
    if (quote) {
      if (verbatim && char === '"' && next === '"') { i++; continue }
      if (!verbatim && char === '\\') { i++; continue }
      if (char === quote) { quote = ''; verbatim = false }
      continue
    }
    if (char === '/' && next === '/') { lineComment = true; i++; continue }
    if (char === '/' && next === '*') { blockComment = true; i++; continue }
    if (char === '"' || char === "'") {
      quote = char
      verbatim = char === '"' && source[i - 1] === '@'
      continue
    }
    if (char === '(') paren++
    else if (char === ')') {
      paren--
      if (paren === 0) {
        parts.push(source.slice(part, i).trim())
        return { parts, end: i }
      }
    } else if (char === '[') square++
    else if (char === ']') square--
    else if (char === '{') curly++
    else if (char === '}') curly--
    else if (char === ',' && paren === 1 && square === 0 && curly === 0) {
      parts.push(source.slice(part, i).trim())
      part = i + 1
    }
  }
  throw new Error(`Unclosed CreatePrompt call at offset ${open}`)
}

function singleLine(value) {
  return (value ?? '').replace(/\s+/g, ' ').trim()
}
function tsv(value) {
  return String(value ?? '').replace(/\t/g, ' ').replace(/\r?\n/g, ' ↵ ')
}
function literal(value) {
  return /^"((?:[^"\\]|\\.)*)"$/.exec(value)?.[1] ?? ''
}
function kindFamily(kind) {
  if (!kind) return 'dynamic'
  if (kind === 'initiative' || kind.startsWith('disaster') || kind === 'trial-order') return 'setup'
  if (kind === 'response' || kind === 'response-target') return 'response'
  if (kind.includes('cost') || kind.includes('resource') || kind.includes('morale')) return 'payment'
  if (kind.includes('slot')) return 'position'
  if (kind.includes('target')) return 'target'
  if (kind.includes('order') || kind.includes('placement')) return 'arrangement'
  if (kind === 'information-confirm') return 'information'
  if (kind === 'optional' || kind === 'option' || kind.startsWith('optional-') || kind === 'opponent-confirm') return 'effect-decision'
  if (kind.includes('card') || kind === 'discard' || kind === 'search' || kind === 'cards') return 'card-selection'
  return 'other'
}
function waitingFallback(kind) {
  if (!kind) return '动态 kind：运行时确认'
  if (kind === 'initiative') return '正在选择先攻或后攻'
  if (kind === 'disaster-ban') return '正在禁用天灾'
  if (kind === 'disaster-pick') return '正在选择天灾'
  if (kind === 'disaster-reveal' || kind === 'disaster-trigger') return '正在确认天灾信息'
  if (kind === 'response') return '正在决定是否响应'
  if (kind === 'optional' || kind === 'option') return '正在决定是否发动效果'
  if (kind === 'slot') return '正在选择战场位置'
  if (kind === 'trial-order') return '正在完成对局准备'
  if (kind.includes('target')) return '正在选择效果对象'
  if (kind.includes('resource') || kind.includes('cost')) return '正在选择如何支付费用'
  if (kind.includes('card') || ['discard', 'search', 'order'].includes(kind)) return '正在完成卡牌选择'
  return '正在完成当前操作'
}

const prompts = []
for (const file of walk(server)) {
  const source = fs.readFileSync(file, 'utf8')
  const re = /\bCreatePrompt\s*\(/g
  for (const match of source.matchAll(re)) {
    const open = source.indexOf('(', match.index)
    const { parts, end } = callArguments(source, open)
    if (parts[0] === 'int playerIndex' && parts[1] === 'string kind') continue
    const kind = literal(parts[1])
    const data = parts.find(part => part.startsWith('data:')) ?? parts[9] ?? ''
    const whole = source.slice(match.index, end + 1)
    const hasNarrative = whole.includes('WithPromptNarrative(')
    const hasChoices = /\b(?:skip|cancel|yes|no|decline)\b/.test(parts[3] ?? '')
    const choiceMode = /\["choiceMode"\]\s*=\s*"([^"]+)"/.exec(whole)?.[1] ?? ''
    const line = source.slice(0, match.index).split('\n').length
    prompts.push({
      source: path.relative(root, file).replaceAll('\\', '/'),
      line,
      kind,
      kindExpression: singleLine(parts[1]),
      family: kindFamily(kind),
      textExpression: singleLine(parts[2]),
      choicesExpression: singleLine(parts[3]),
      minExpression: singleLine(parts[4]),
      maxExpression: singleLine(parts[5]),
      continuationExpression: singleLine(parts[6]),
      privacyExpression: singleLine(parts.find(part => part.startsWith('isPrivate:')) ?? parts[8] ?? 'default: true'),
      dataExpression: singleLine(data),
      narrativeSource: hasNarrative ? 'callsite WithPromptNarrative' : kind === 'initiative'
        ? 'DefaultSystemPromptNarrative' : 'runtime data/BuildPromptPresentation fallback; inspect caller',
      titleSource: 'narrative.Title > data.sourceName > text prefix > PromptKindTitle(kind)',
      situationSource: 'narrative.Situation > data.effectText > prompt text',
      instructionSource: 'narrative.Instruction > PromptInstruction(kind,min,max,uiPattern)',
      waitingFallback: waitingFallback(kind),
      waitingSource: 'narrative.WaitingAction when present; otherwise PromptWaitingSummary(kind)',
      choiceConsequenceSource: 'narrative.ChoiceConsequences or empty; legacy label/data only for button name',
      paymentSource: 'narrative.PaymentStatus/PaymentSummary or null',
      submissionSource: 'narrative.SubmissionConsequence or null',
      choiceModeInCall: choiceMode || 'runtime data/ApplyDirectBoardChoiceMode',
      exitChoiceLiteralSeen: hasChoices ? 'candidate literal; inspect runtime choices' : 'not in choices expression',
      submitSemantics: 'resolvePrompt with promptId/binding; client instant/pure decision or min/max confirm; server continuation decides effect',
      exitSemantics: 'skip/cancel/no/decline only when in ValidChoices; meaning from ChoiceConsequences and continuation, not token alone',
      consumer: 'PromptOverlay; GameBoard for board-target/board-slot/resource-selection/board-selection/mixed-board-payment choiceMode',
    })
  }
}
prompts.sort((a, b) => a.source.localeCompare(b.source) || a.line - b.line)
if (prompts.length !== 125) throw new Error(`Expected 125 CreatePrompt call sites, found ${prompts.length}`)
const promptColumns = Object.keys(prompts[0])
fs.writeFileSync(promptTarget,
  [promptColumns.join('\t'), ...prompts.map(row => promptColumns.map(key => tsv(row[key])).join('\t'))].join('\n') + '\n')

const inventory = JSON.parse(fs.readFileSync(sourceInventory, 'utf8'))
if (inventory.CardCount !== 324 || inventory.Abilities.length !== 686)
  throw new Error(`Unexpected ability inventory size: ${inventory.CardCount}/${inventory.Abilities.length}`)
const catalog = ['cards.s1.json', 'cards.s2.json', 'cards.st.json']
  .flatMap(file => JSON.parse(fs.readFileSync(path.join(server, 'Data', file), 'utf8')))
const cardTypes = new Map(catalog.map(card => [card.id, card.cardType]))
const abilities = inventory.Abilities.map(row => {
  const ability = row.Definition
  const kinds = [...new Set(ability.Atoms.map(atom => atom.Kind))]
  const scenes = ability.Presentations
  const categories = []
  if (kinds.some(kind => kind.startsWith('cost.')) || ability.Atoms.some(atom => atom.Stage === 'cost')) categories.push('payment')
  if (kinds.includes('selection.target')) categories.push('target')
  if (ability.ExecutionModel === 'reaction' || ability.Trigger.includes('reaction')) categories.push('response')
  if (kinds.includes('operation.composite-flow')
    || scenes.some(scene => (scene.SegmentCount ?? 0) > 1 || scene.BranchId)) categories.push('multi-segment-or-branch')
  if (kinds.some(kind => ['operation.move-zone', 'operation.move', 'operation.draw', 'operation.shuffle'].includes(kind))) categories.push('zone-or-movement')
  if (ability.ExecutionModel.includes('continuous') || kinds.includes('duration.apply')) categories.push('continuous-or-duration')
  if (['disaster', 'trial', 'destruction'].includes(cardTypes.get(row.CardId))
    || ['disaster', 'trial', 'trial-complete', 'trial-completed'].includes(ability.Trigger)
    || kinds.includes('operation.advance-trial')) categories.push('disaster-or-trial')
  return {
    cardId: row.CardId,
    cardName: row.Name,
    cardType: cardTypes.get(row.CardId) ?? '',
    abilityId: ability.AbilityId,
    trigger: ability.Trigger,
    executionModel: ability.ExecutionModel,
    atomKinds: kinds.join(';'),
    sceneIds: scenes.map(scene => scene.SceneId).join(';'),
    segmentScenes: scenes.filter(scene => scene.SegmentIndex).map(scene => `${scene.SceneId}#${scene.SegmentIndex}/${scene.SegmentCount}`).join(';'),
    branchScenes: scenes.filter(scene => scene.BranchId).map(scene => `${scene.SceneId}#${scene.BranchId}`).join(';'),
    routeCandidates: row.RouteCandidates.join(';'),
    entryEvidence: row.EntryEvidence,
    categories: categories.join(';') || 'other',
  }
})
const abilityColumns = Object.keys(abilities[0])
fs.writeFileSync(abilityTarget,
  [abilityColumns.join('\t'), ...abilities.map(row => abilityColumns.map(key => tsv(row[key])).join('\t'))].join('\n') + '\n')
const groups = [...new Set(abilities.flatMap(row => row.categories.split(';')))]
  .map(category => ({
    category,
    abilities: abilities.filter(row => row.categories.split(';').includes(category)).length,
    cards: new Set(abilities.filter(row => row.categories.split(';').includes(category)).map(row => row.cardId)).size,
  }))
console.log(JSON.stringify({
  promptCalls: prompts.length,
  promptKinds: [...new Set(prompts.map(row => row.kind || `<dynamic:${row.kindExpression}>`))].sort(),
  promptNarrativeDirect: prompts.filter(row => row.narrativeSource === 'callsite WithPromptNarrative').length,
  cards: inventory.CardCount,
  abilities: abilities.length,
  scenes: inventory.Abilities.reduce((sum, row) => sum + row.Definition.Presentations.length, 0),
  groups,
  promptTarget,
  abilityTarget,
}, null, 2))
