import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

if (!process.argv.includes('--child')) {
  const failures = []
  for (const timezone of ['UTC', 'Asia/Shanghai', 'America/Los_Angeles']) {
    const result = spawnSync(process.execPath, [fileURLToPath(import.meta.url), '--child'],
      { encoding: 'utf8', env: { ...process.env, TZ: timezone } })
    if (result.status !== 0) failures.push(`${timezone}: ${result.stdout}${result.stderr}`)
    else process.stdout.write(result.stdout)
  }
  assert.equal(failures.length, 0, failures.join('\n'))
  console.log('Admin global UTC calendar contract passed in three client timezones.')
} else {
  const ts = createRequire(import.meta.url)('typescript')
  const platform = readFileSync(new URL('../src/l12/platform.ts', import.meta.url), 'utf8')
  const tree = ts.createSourceFile('platform.ts', platform, ts.ScriptTarget.Latest, true)
  assert.equal(tree.parseDiagnostics.length, 0)
  const declarations = tree.statements.flatMap(statement => ts.isVariableStatement(statement)
    ? [...statement.declarationList.declarations] : [])
  const adminApi = declarations.find(node => node.name.getText(tree) === 'adminApi')
  assert(adminApi && ts.isObjectLiteralExpression(adminApi.initializer))
  const member = adminApi.initializer.properties.find(node => node.name?.getText(tree) === 'globalAnalytics')
  assert(member && ts.isPropertyAssignment(member))
  const helpers = tree.statements.filter(node => ts.isFunctionDeclaration(node)
    && ['localDateBoundary', 'analyticsUtcDate'].includes(node.name?.text))
  const compiled = ts.transpileModule(`${helpers.map(node => node.getText(tree)).join('\n')}\nconst invoke=${member.initializer.getText(tree)}`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022 },
  }).outputText
  const calls = []
  const invoke = new Function('platformRequest', `${compiled}\nreturn invoke`)(url => { calls.push(url); return Promise.resolve({}) })
  const failures = []
  let passed = 0
  async function check(name, work) {
    try { await work(); passed++ } catch (error) { failures.push(`${name}: ${error.message}`) }
  }
  await check('literal inclusive dates', async () => {
    await invoke({ from: '2026-09-16', to: '2026-10-14' })
    const params = new URL(calls.at(-1), 'http://synthetic.local').searchParams
    assert.equal(params.get('fromDate'), '2026-09-16')
    assert.equal(params.get('toDate'), '2026-10-14')
    assert.equal(params.has('fromUtc'), false)
    assert.equal(params.has('toUtc'), false)
  })
  await check('optional defaults', async () => {
    await invoke({})
    assert.equal(calls.at(-1), '/api/admin/analytics/global')
  })
  await check('leap-day remains the same natural day', async () => {
    await invoke({ from: '2024-02-29', to: '2024-02-29' })
    assert.equal(new URL(calls.at(-1), 'http://synthetic.local').searchParams.get('toDate'), '2024-02-29')
  })
  for (const invalid of ['2026-02-30', '2026-9-16', 'not-a-date', '0000-01-01']) {
    await check(`invalid date rejected before request: ${invalid}`, async () => {
      const before = calls.length
      assert.throws(() => invoke({ from: invalid }), /日期/)
      assert.equal(calls.length, before)
    })
  }
  const summary = readFileSync(new URL('../src/l12/site/AdminGlobalAnalyticsSummary.vue', import.meta.url), 'utf8')
  const script = summary.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1]
  const summaryTree = ts.createSourceFile('summary.ts', script, ts.ScriptTarget.Latest, true)
  const dateText = summaryTree.statements.flatMap(node => ts.isVariableStatement(node)
    ? [...node.declarationList.declarations] : []).find(node => node.name.getText(summaryTree) === 'dateText')
  assert(dateText)
  const formatter = new Function(ts.transpileModule(`const format=${dateText.initializer.getText(summaryTree)}`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022 },
  }).outputText + '\nreturn format')()
  await check('calendar display uses UTC rather than the client date', async () => {
    assert.equal(formatter(new Date('2026-09-15T23:30:00Z')), '2026-09-15')
    assert.equal(formatter(new Date('2026-09-16T00:30:00Z')), '2026-09-16')
  })
  await check('explicit sampling and registration definitions', async () => {
    assert(summary.includes('UTC自然日'))
    assert(summary.includes('采样分钟峰值均值'))
    assert(summary.includes('新增活跃'))
    assert(summary.includes('新增注册'))
    assert(summary.includes('sampleMinutes'))
    assert(summary.includes("timeZone: 'UTC'"))
  })
  assert.equal(failures.length, 0, failures.join('\n'))
  console.log(`Global UTC query and display ${process.env.TZ}: ${passed}/9 actual-source cases passed.`)
}
