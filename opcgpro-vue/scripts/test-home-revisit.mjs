import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { homeAccountVerified, homeCanContinueGame } from '../src/l12/site/homeRevisit.ts'
import { tournamentHubSection } from '../src/l12/site/tournamentHubNavigation.ts'

let assertions = 0
const equal = (value, expected, message) => { assert.deepEqual(value, expected, message); assertions++ }
equal(homeAccountVerified('account-a', 'token-a', true), true)
for (const args of [[undefined, 'token', true], ['a', '', true], ['a', 'token', false]])
  equal(homeAccountVerified(...args), false, 'cached identity alone cannot expose personal shortcuts')
const connection = { accountId:'a', status:'online', recoveryPhase:'snapshot-acknowledged', leavingRoom:false,
  game:{ matchId:'fixture-match', phase:'Main' } }
equal(homeCanContinueGame('a', true, connection), true)
for (const change of [{ accountId:'b' }, { status:'offline' }, { status:'connecting' },
  { recoveryPhase:'restoring' }, { leavingRoom:true }, { game:null },
  { game:{ matchId:'', phase:'Main' } }, { game:{ matchId:'fixture-match', phase:'GameOver' } }])
  equal(homeCanContinueGame('a', true, { ...connection, ...change }), false, 'only confirmed ongoing own connection offers continue')
equal(homeCanContinueGame('a', false, connection), false)
equal(homeCanContinueGame(undefined, true, connection), false)
for (const value of ['discover','mine','history','host','create']) equal(tournamentHubSection(value), value)
for (const value of [undefined, null, ['mine'], 'other', '/admin']) equal(tournamentHubSection(value), 'discover')

const source = fs.readFileSync(new URL('../src/l12/site/homePublishedCache.ts', import.meta.url),'utf8')
const module = await import(`data:text/javascript;base64,${Buffer.from(ts.transpileModule(source,{
  compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 },
}).outputText).toString('base64')}`)
const payload = { composition:'{}', legal:'{}', news:[], videos:[], products:[], media:[] }
equal(module.loadPublishedHomeCache({getItem:() => { throw Error('storage blocked') }}), null)
equal(module.loadPublishedHomeCache({getItem:() => '{broken'}), null)
equal(module.loadPublishedHomeCache({getItem: key => key.endsWith('-v2') ? JSON.stringify({version:2,payload}) : null}), payload)
equal(module.loadPublishedHomeCache({getItem: key => key.endsWith('-v1') ? JSON.stringify(payload) : null}), payload)
equal(module.savePublishedHomeCache(payload,{setItem:() => { throw Error('quota') }}), false)
equal(module.savePublishedHomeCache(payload,{setItem:() => undefined}), true)
console.log(`Home revisit and public empty-state support passed: ${assertions} assertions`)
