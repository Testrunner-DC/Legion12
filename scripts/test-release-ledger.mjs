import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { evaluateRelease, loadLedger, main } from './release-ledger.mjs'

const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'l12-release-ledger-'))
const git = (...args) => execFileSync('git', ['-C', temp, ...args], { encoding: 'utf8' }).trim()
const write = (relative, value) => {
  const target = path.join(temp, relative)
  fs.mkdirSync(path.dirname(target), { recursive: true })
  fs.writeFileSync(target, typeof value === 'string' ? value : `${JSON.stringify(value, null, 2)}\n`, 'utf8')
}
const commit = message => { git('add', '.'); git('commit', '--quiet', '-m', message); return git('rev-parse', 'HEAD') }

try {
  git('init', '--quiet')
  git('config', 'user.email', 'release-ledger@example.invalid')
  git('config', 'user.name', 'Release Ledger Test')
  write('opcgpro-vue/src/App.vue', '<template>baseline</template>\n')
  write('release-ledger/entries/.keep', '')
  write('release-ledger/config.json', {
    schemaVersion: 1,
    baselineCommit: '0'.repeat(40),
    defaultReleaseTitle: '版本更新',
    categories: { ui: '界面与体验', 'card-effects': '卡牌效果' },
    playerFacingRoots: ['opcgpro-vue/src/', '服务端WebSocket/TwelveLegions/'],
    ignoredPlayerFacingSuffixes: ['.Tests.cs'],
    forbiddenPlayerTerms: ['数据库表'],
  })
  const bootstrap = commit('bootstrap')
  const config = JSON.parse(fs.readFileSync(path.join(temp, 'release-ledger/config.json'), 'utf8'))
  config.baselineCommit = bootstrap
  write('release-ledger/config.json', config)
  const baseline = commit('set ledger baseline')
  config.baselineCommit = baseline
  write('release-ledger/config.json', config)
  git('add', 'release-ledger/config.json')
  git('commit', '--amend', '--quiet', '--no-edit')
  const amendedBaseline = git('rev-parse', 'HEAD')
  // The baseline cannot self-reference its amended SHA in a fixture. Point at the
  // stable parent; range clamping behavior is covered without relying on a fixed point.
  config.baselineCommit = bootstrap
  write('release-ledger/config.json', config)
  git('add', 'release-ledger/config.json')
  git('commit', '--amend', '--quiet', '--no-edit')
  const actualBaseline = git('rev-parse', 'HEAD')

  write('opcgpro-vue/src/App.vue', '<template>player change</template>\n')
  const uncoveredCommit = commit('player change without note')
  assert.throws(() => evaluateRelease(temp, actualBaseline, uncoveredCommit), /未登记的玩家相关源码/)

  write('release-ledger/entries/player-ui.json', {
    schemaVersion: 1,
    id: 'player-ui-result',
    date: '2026-09-25',
    audience: 'players',
    title: '界面体验更新',
    changes: [{ category: 'ui', text: '页面切换后会立即显示正确内容。', paths: ['opcgpro-vue/src/App.vue'] }],
  })
  const coveredCommit = commit('register player change')
  const covered = evaluateRelease(temp, actualBaseline, coveredCommit)
  assert.equal(covered.release.sections[0].title, '界面与体验')
  assert.deepEqual(covered.release.sections[0].items, ['页面切换后会立即显示正确内容。'])

  write('opcgpro-vue/src/Second.vue', '<template>second same-day release</template>\n')
  write('release-ledger/entries/player-ui-second.json', {
    schemaVersion: 1,
    id: 'player-ui-second-release',
    date: '2026-09-25',
    audience: 'players',
    title: '同日再次更新',
    changes: [{ category: 'ui', text: '第二次发布也保留页面改进。', paths: ['opcgpro-vue/src/Second.vue'] }],
  })
  const secondCommit = commit('second player release on same day')
  const second = evaluateRelease(temp, coveredCommit, secondCommit)
  assert.deepEqual(second.release.sections[0].items, ['第二次发布也保留页面改进。'], '单次发布门禁仍只覆盖本次区间')
  assert.equal(second.history.length, 1, '同日的发布日志合并为一日，不相互覆盖')
  assert.deepEqual([...second.history[0].sections[0].items].sort(), [
    '页面切换后会立即显示正确内容。',
    '第二次发布也保留页面改进。',
  ].sort())
  assert.deepEqual(evaluateRelease(temp, actualBaseline, coveredCommit).history[0].sections[0].items,
    ['页面切换后会立即显示正确内容。'], '旧候选不能提前展示未来条目')

  write('服务端WebSocket/TwelveLegions/Internal.cs', '// internal refactor\n')
  write('release-ledger/entries/internal.json', {
    schemaVersion: 1,
    id: 'internal-refactor-only',
    date: '2026-09-25',
    audience: 'internal',
    reason: '只调整内部组织，不改变玩家行为。',
    paths: ['服务端WebSocket/TwelveLegions/Internal.cs'],
  })
  const internalCommit = commit('internal declaration')
  const internal = evaluateRelease(temp, secondCommit, internalCommit)
  assert.equal(internal.release, null)
  assert.deepEqual(internal.history, second.history, '纯内部再次发布也不能清空既有玩家更新日志')
  assert.deepEqual(internal.productFiles, ['服务端WebSocket/TwelveLegions/Internal.cs'], '中文服务端路径必须被账本扫描与覆盖，不得被 Git quotepath 转义绕过')

  const output = path.join(temp, 'generated.ts')
  const summary = path.join(temp, 'summary.json')
  main(['release', '--repo', temp, '--from', coveredCommit, '--to', secondCommit, '--output', output, '--summary', summary])
  const generated = fs.readFileSync(output, 'utf8')
  assert.match(generated, /generatedPlayerReleaseHistory/)
  assert.match(generated, /页面切换后会立即显示正确内容/)
  assert.match(generated, /第二次发布也保留页面改进/)
  assert.equal(JSON.parse(fs.readFileSync(summary, 'utf8')).playerEntryCount, 1)
  assert.equal(JSON.parse(fs.readFileSync(summary, 'utf8')).historyDayCount, 1)

  const playerPath = path.join(temp, 'release-ledger/entries/player-ui.json')
  const invalid = JSON.parse(fs.readFileSync(playerPath, 'utf8'))
  invalid.changes[0].text = '数据库表已经修改。'
  write('release-ledger/entries/player-ui.json', invalid)
  assert.throws(() => loadLedger(temp), /包含内部术语/)

  console.log('[玩家更新日志账本] 回归测试通过：缺失登记、分类聚合、同日多次发布累计、内部发布保留历史、中文路径覆盖、生成结果与禁用术语。')
} finally {
  fs.rmSync(temp, { recursive: true, force: true })
}
