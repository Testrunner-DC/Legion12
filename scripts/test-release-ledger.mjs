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
  const internal = evaluateRelease(temp, coveredCommit, internalCommit)
  assert.equal(internal.release, null)
  assert.deepEqual(internal.productFiles, ['服务端WebSocket/TwelveLegions/Internal.cs'], '中文服务端路径必须被账本扫描与覆盖，不得被 Git quotepath 转义绕过')

  const output = path.join(temp, 'generated.ts')
  const summary = path.join(temp, 'summary.json')
  main(['release', '--repo', temp, '--from', actualBaseline, '--to', coveredCommit, '--output', output, '--summary', summary])
  assert.match(fs.readFileSync(output, 'utf8'), /页面切换后会立即显示正确内容/)
  assert.equal(JSON.parse(fs.readFileSync(summary, 'utf8')).playerEntryCount, 1)

  const playerPath = path.join(temp, 'release-ledger/entries/player-ui.json')
  const invalid = JSON.parse(fs.readFileSync(playerPath, 'utf8'))
  invalid.changes[0].text = '数据库表已经修改。'
  write('release-ledger/entries/player-ui.json', invalid)
  assert.throws(() => loadLedger(temp), /包含内部术语/)

  console.log('[玩家更新日志账本] 回归测试通过：缺失登记、分类聚合、中文路径覆盖、内部声明、生成结果与禁用术语。')
} finally {
  fs.rmSync(temp, { recursive: true, force: true })
}
