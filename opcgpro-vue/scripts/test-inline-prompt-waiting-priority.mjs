import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {parse} from '@vue/compiler-sfc'
import ts from 'typescript'
import {computed,reactive,ref} from 'vue'

// Execute the actual SFC computed expressions, not a second implementation of
// prompt routing. Browser hit testing is a separate acceptance layer.
const files={board:'GameBoard.vue',overlay:'PromptOverlay.vue'}
const sources=Object.fromEntries(Object.entries(files).map(([key,file])=>[key,
 readFileSync(new URL(`../src/l12/game/${file}`,import.meta.url),'utf8')]))
const initializers={}
for(const [kind,text] of Object.entries(sources)){
 const descriptor=parse(text).descriptor
 const source=ts.createSourceFile(files[kind],descriptor.scriptSetup.content,ts.ScriptTarget.Latest,true,ts.ScriptKind.TS)
 initializers[kind]=new Map()
 for(const statement of source.statements)if(ts.isVariableStatement(statement))for(const declaration of statement.declarationList.declarations){
  if(!ts.isIdentifier(declaration.name)||!declaration.initializer)continue
  assert.ok(!initializers[kind].has(declaration.name.text))
  initializers[kind].set(declaration.name.text,declaration.initializer.getText(source))
 }
}
function evaluate(kind,name,environment){
 assert.ok(initializers[kind].has(name),`actual ${kind}.${name} initializer must exist`)
 const javascript=ts.transpileModule(`const result=${initializers[kind].get(name)};`,{
  compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.None}}).outputText
 return new Function(...Object.keys(environment),javascript+';return result;')(...Object.values(environment))
}
function fixture(you){
 const game=reactive({you,phase:'Main',prompts:[],waitingPrompt:null,
  players:[0,1].map(playerIndex=>({playerIndex,field:[[{instanceId:'field-'+playerIndex},null,null],[null,null,null]]}))})
 const props=reactive({game,readOnly:false})
 const board={props,computed,boardControlMinimized:ref(false),deferredEntryPromptIds:ref(new Set())}
 for(const name of ['presentationPrompts','boardTargetPrompt','boardSlotPrompt','resourceSelectionPrompt','visibleInlinePromptIds','activeBoardPromptIds',
  'promptOverlayMounted','visibleBoardPromptIds','routedPromptModal'])board[name]=evaluate('board',name,board)
 const overlay={props:{game,get suppressedPromptIds(){return board.activeBoardPromptIds.value},suppressedPromptId:null},
  computed,waitingDefense:ref(false),isMulliganPhase:ref(false),isDisasterPreparation:ref(false)}
 for(const name of ['prompt','waitingPrompt','visible'])overlay[name]=evaluate('overlay',name,overlay)
 return {game,props,board,overlay}
}
let checks=0
for(const you of [0,1]){
 const f=fixture(you)
 for(const kind of ['target','slot','resource-payment']){
  const prompt={promptId:`owner-${you}-${kind}`,playerIndex:you,kind,text:'合成选择',
   validChoices:kind==='target'?['field-'+you]:kind==='slot'?['0:1']:['resource-'+you],minChoose:1,maxChoose:1,data:{}}
  f.game.prompts=[prompt];f.game.waitingPrompt={playerIndex:1-you,kind:'target',waitingSummary:'对手正在选择'}
  assert.deepEqual(f.board.activeBoardPromptIds.value,[prompt.promptId])
  assert.equal(f.overlay.prompt.value,null,'the board, not the overlay, owns this choice')
  assert.equal(f.overlay.waitingPrompt.value,null,`${kind}: opponent waiting must not cover owner choice`)
  assert.equal(f.overlay.visible.value,false)
  assert.equal(f.board.routedPromptModal.value,false)
  checks++
  f.board.boardControlMinimized.value=true
  assert.deepEqual(f.board.visibleBoardPromptIds.value,[])
  assert.equal(f.overlay.waitingPrompt.value,null,'minimized owner controls still take priority')
  assert.equal(f.board.routedPromptModal.value,false)
  f.board.boardControlMinimized.value=false
  checks++
 }
 const stillAuthoritative=f.game.prompts
 f.board.deferredEntryPromptIds.value=new Set([stillAuthoritative[0].promptId])
 assert.equal(f.game.prompts,stillAuthoritative,'deferring presentation must not remove the authority prompt')
 assert.equal(f.board.resourceSelectionPrompt.value,null)
 assert.deepEqual(f.board.visibleBoardPromptIds.value,[])
 assert.deepEqual(f.board.activeBoardPromptIds.value,[stillAuthoritative[0].promptId])
 assert.equal(f.overlay.prompt.value,null)
 assert.equal(f.overlay.waitingPrompt.value,null)
 f.board.deferredEntryPromptIds.value=new Set()
 assert.equal(f.board.resourceSelectionPrompt.value.promptId,stillAuthoritative[0].promptId)
 checks++
 // All transitions reuse the same reactive instance, without remounting.
 f.game.prompts=[]
 assert.ok(f.overlay.waitingPrompt.value&&f.overlay.visible.value&&f.board.routedPromptModal.value)
 checks++
 f.game.prompts=[{promptId:'owner-card',playerIndex:you,kind:'card',text:'合成手牌选择',
  validChoices:['hand-card'],minChoose:1,maxChoose:1,data:{}}]
 assert.equal(f.overlay.prompt.value?.promptId,'owner-card')
 assert.equal(f.overlay.waitingPrompt.value,null)
 assert.equal(f.board.routedPromptModal.value,true)
 checks++
 f.game.waitingPrompt=null;f.game.prompts=[]
 assert.equal(f.overlay.visible.value,false);assert.equal(f.board.routedPromptModal.value,false)
 checks++
 f.props.readOnly=true
 assert.equal(f.board.promptOverlayMounted.value,false)
 f.game.phase='DisasterPreparation'
 assert.equal(f.board.promptOverlayMounted.value,true,'public disaster preparation still mounts its existing overlay')
 assert.equal(f.board.routedPromptModal.value,true)
 checks++
}
assert.match(sources.board,/<PromptOverlay[^>]+:suppressed-prompt-ids="activeBoardPromptIds"/)
console.log(`Owner inline prompt and opponent waiting priority: ${checks}/${checks} same-instance checks passed`)
