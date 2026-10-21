import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const root=path.resolve(import.meta.dirname,'..')
const source=fs.readFileSync(path.join(root,'src/l12/site/DeckMotionDiagnostics.vue'),'utf8')
const parent=fs.readFileSync(path.join(root,'src/l12/site/DeckLibraryPage.vue'),'utf8')
const setup=parse(source).descriptor.scriptSetup.content
assert.match(parent, /<DeckMotionDiagnostics v-if="route\.query\.motionDiagnostics === '1'"/)
assert.doesNotMatch(setup, /\b(fetch|WebSocket|XMLHttpRequest|sendBeacon|localStorage|sessionStorage)\b|document\.(cookie|body)|\.(innerText|textContent|innerHTML)/)
assert.match(setup, /onMounted\(\(\) => \{ void capture\(\) \}\)/)
assert.match(setup, /if \(epoch !== generation\) return/)
assert.match(setup, /navigator\.clipboard\.writeText\(report\.value\)/)
const tree=ts.createSourceFile('probe.ts',setup,ts.ScriptTarget.Latest,true,ts.ScriptKind.TS)
const node=tree.statements.find(item=>ts.isFunctionDeclaration(item)&&item.name?.text==='sampleRows')
assert(node)
const js=ts.transpileModule(node.getText(tree),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext}}).outputText
const style={animationName:'none',animationDuration:'0.000001s',animationIterationCount:'1',animationPlayState:'running',transform:'none',transitionDuration:'0.000001s',getPropertyValue:()=> '50s'}
const track={dataset:{motionReady:'true'},style},cover={style},fan={style}
const viewport={classList:{contains:()=>true},matches:()=>false,querySelector:q=>q===':focus-visible'?null:q==='.hot-deck-track'?track:q==='.deck-cover'?cover:fan,getBoundingClientRect:()=>({width:325}),scrollLeft:0}
const sample=Function('document','getComputedStyle',js+';return sampleRows')({querySelectorAll:()=>[viewport,viewport,viewport]},el=>el.style)
const report=sample()
assert.equal(report.length,2,'only the two public UI tracks are inspected')
assert.equal(report[0].duration,'0.000001s','never normalize a real disabled-animation cause into a passing value')
assert.equal(report[0].iterations,'1')
assert.equal(report[0].coverTransition,'0.000001s')
assert.equal(report[0].pausedClass,true)
assert.equal(report[0].hovered,false)
assert.equal(report[0].focusVisible,false)
style.animationName='hot-decks-slide';style.animationDuration='50s';style.animationIterationCount='infinite';style.transform='matrix(1, 0, 0, 1, -24, 0)'
assert.equal(sample()[0].transform,'matrix(1, 0, 0, 1, -24, 0)')
assert.equal(sample()[0].iterations,'infinite')
console.log('Deck motion diagnostics: actual sampler, opt-in, truthful disabled states, two-row bound, stale capture and no identity/network/storage reads passed')
