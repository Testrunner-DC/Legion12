import fs from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import { spawnSync } from 'node:child_process'
import { pathToFileURL } from 'node:url'

const normalize = value => String(value).replaceAll('\\', '/').replace(/^\.\//, '')

function fail(message) {
  throw new Error(`[玩家更新日志账本] ${message}`)
}

function runGit(repo, args, allowFailure = false) {
  // Git quotes non-ASCII paths by default. The project keeps player-facing
  // server sources below a Chinese directory name, so quoted octal paths would
  // evade both root classification and exact ledger coverage on Windows/CI.
  const result = spawnSync('git', ['-C', repo, '-c', 'core.quotepath=false', ...args], { encoding: 'utf8' })
  if (result.status !== 0 && !allowFailure)
    fail(`Git 命令失败：git ${args.join(' ')}\n${result.stderr || result.stdout}`)
  return result
}

function readJson(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')) }
  catch (error) { fail(`${normalize(file)} 不是有效 JSON：${error.message}`) }
}

function listEntryFiles(repo) {
  const root = path.join(repo, 'release-ledger', 'entries')
  if (!fs.existsSync(root)) fail('缺少 release-ledger/entries 目录。')
  return fs.readdirSync(root).filter(name => name.endsWith('.json')).sort()
    .map(name => path.join(root, name))
}

function validatePathPattern(pattern, entryId) {
  if (typeof pattern !== 'string' || !pattern.trim()) fail(`${entryId} 含空路径。`)
  const normalized = normalize(pattern.trim())
  if (normalized === '**' || normalized === '*' || normalized.startsWith('../') || path.isAbsolute(pattern))
    fail(`${entryId} 的路径 ${pattern} 过宽或越界；请列出精确文件，或使用目录/**。`)
  if (normalized.includes('*') && !normalized.endsWith('/**'))
    fail(`${entryId} 的路径 ${pattern} 只允许精确文件或目录/**。`)
  return normalized
}

function matches(pattern, file) {
  const normalized = normalize(pattern)
  return normalized.endsWith('/**')
    ? normalize(file).startsWith(normalized.slice(0, -3))
    : normalize(file) === normalized
}

export function loadLedger(repoInput) {
  const repo = path.resolve(repoInput)
  const configPath = path.join(repo, 'release-ledger', 'config.json')
  if (!fs.existsSync(configPath)) fail('缺少 release-ledger/config.json。')
  const config = readJson(configPath)
  if (config.schemaVersion !== 1) fail('config.json schemaVersion 必须为 1。')
  if (!/^[0-9a-f]{40}$/.test(config.baselineCommit || '')) fail('baselineCommit 必须为完整提交 SHA。')
  if (!config.categories || Object.keys(config.categories).length === 0) fail('至少配置一个玩家分类。')
  const ids = new Set()
  const entries = listEntryFiles(repo).map(file => {
    const entry = readJson(file)
    if (entry.schemaVersion !== 1) fail(`${normalize(path.relative(repo, file))} schemaVersion 必须为 1。`)
    if (!/^[a-z0-9][a-z0-9-]{5,79}$/.test(entry.id || '')) fail(`${normalize(file)} 的 id 格式错误。`)
    if (ids.has(entry.id)) fail(`id 重复：${entry.id}。`)
    ids.add(entry.id)
    if (!/^\d{4}-\d{2}-\d{2}$/.test(entry.date || '')) fail(`${entry.id} 的 date 必须为 YYYY-MM-DD。`)
    if (!['players', 'internal'].includes(entry.audience)) fail(`${entry.id} 的 audience 只允许 players 或 internal。`)
    if (entry.audience === 'internal') {
      if (!String(entry.reason || '').trim()) fail(`${entry.id} 是内部变更，必须写明 reason。`)
      if (!Array.isArray(entry.paths) || entry.paths.length === 0) fail(`${entry.id} 是内部变更，必须列出 paths。`)
      if (entry.changes || entry.title) fail(`${entry.id} 是内部变更，不得包含会外显的 title/changes。`)
      entry._paths = entry.paths.map(value => validatePathPattern(value, entry.id))
    } else {
      if (!String(entry.title || '').trim()) fail(`${entry.id} 面向玩家，必须填写 title。`)
      if (!Array.isArray(entry.changes) || entry.changes.length === 0) fail(`${entry.id} 面向玩家，必须至少有一条 changes。`)
      entry._paths = []
      for (const [index, change] of entry.changes.entries()) {
        if (!Object.hasOwn(config.categories, change.category)) fail(`${entry.id} 第 ${index + 1} 条使用未知分类 ${change.category}。`)
        if (!String(change.text || '').trim()) fail(`${entry.id} 第 ${index + 1} 条缺少玩家可读结果。`)
        if (!Array.isArray(change.paths) || change.paths.length === 0) fail(`${entry.id} 第 ${index + 1} 条缺少 paths。`)
        const exposed = `${entry.title}\n${change.text}`
        for (const term of config.forbiddenPlayerTerms || []) {
          if (exposed.includes(term)) fail(`${entry.id} 的玩家文案包含内部术语“${term}”；请改写为玩家能感知的结果。`)
        }
        const paths = change.paths.map(value => validatePathPattern(value, entry.id))
        change._paths = paths
        entry._paths.push(...paths)
      }
    }
    entry._file = normalize(path.relative(repo, file))
    return entry
  })
  return { repo, config, entries }
}

function commitExists(repo, commit, label) {
  if (!/^[0-9a-f]{7,40}$/i.test(commit || '')) fail(`${label} 不是提交 SHA：${commit || '(空)'}`)
  if (runGit(repo, ['cat-file', '-e', `${commit}^{commit}`], true).status !== 0) fail(`${label} 不在本地 Git 历史中：${commit}`)
  return runGit(repo, ['rev-parse', `${commit}^{commit}`]).stdout.trim()
}

function isAncestor(repo, ancestor, descendant) {
  return runGit(repo, ['merge-base', '--is-ancestor', ancestor, descendant], true).status === 0
}

function changedFiles(repo, from, to, pathSpec = []) {
  const result = runGit(repo, ['diff', '--name-only', '--diff-filter=ACMR', `${from}..${to}`, '--', ...pathSpec])
  return result.stdout.split(/\r?\n/).map(normalize).filter(Boolean)
}

function isPotentialPlayerFile(file, config) {
  if (!(config.playerFacingRoots || []).some(root => file.startsWith(normalize(root)))) return false
  return !(config.ignoredPlayerFacingSuffixes || []).some(suffix => file.endsWith(suffix))
}

export function evaluateRelease(repoInput, fromInput, toInput) {
  const ledger = loadLedger(repoInput)
  const to = commitExists(ledger.repo, toInput, '待发布提交')
  let from = commitExists(ledger.repo, fromInput, '当前正式服提交')
  const baseline = commitExists(ledger.repo, ledger.config.baselineCommit, '账本基线提交')
  if (!isAncestor(ledger.repo, from, to)) fail(`当前正式服提交 ${from} 不是待发布提交 ${to} 的祖先；请先处理分叉或回滚关系。`)
  if (isAncestor(ledger.repo, from, baseline) && isAncestor(ledger.repo, baseline, to)) from = baseline
  else if (!isAncestor(ledger.repo, baseline, to)) fail(`待发布提交 ${to} 不包含账本基线 ${baseline}。`)

  const entryPaths = new Set(changedFiles(ledger.repo, from, to, ['release-ledger/entries']))
  const rangedEntries = ledger.entries.filter(entry => entryPaths.has(entry._file))
  const productFiles = changedFiles(ledger.repo, from, to).filter(file => isPotentialPlayerFile(file, ledger.config))
  const uncovered = productFiles.filter(file => !rangedEntries.some(entry => entry._paths.some(pattern => matches(pattern, file))))
  if (uncovered.length > 0) {
    fail(`正式发布区间存在未登记的玩家相关源码：\n- ${uncovered.join('\n- ')}\n` +
      '请在 release-ledger/entries 新增本批 JSON：玩家可感知变化用 audience=players、分类/结果/paths；纯内部变化用 audience=internal、reason/paths。')
  }

  const playerEntries = rangedEntries.filter(entry => entry.audience === 'players')
  const sections = []
  for (const [category, title] of Object.entries(ledger.config.categories)) {
    const items = []
    for (const entry of playerEntries) for (const change of entry.changes) {
      if (change.category === category && !items.includes(change.text.trim())) items.push(change.text.trim())
    }
    if (items.length) sections.push({ title, items })
  }
  const dates = playerEntries.map(entry => entry.date).sort()
  const titles = [...new Set(playerEntries.map(entry => entry.title.trim()))]
  const release = sections.length ? {
    date: dates.at(-1),
    title: titles.length === 1 ? titles[0] : ledger.config.defaultReleaseTitle,
    version: to,
    sections,
  } : null
  return { ...ledger, from, to, baseline, productFiles, rangedEntries, playerEntries, release }
}

function renderTypeScript(release) {
  return `export interface GeneratedPlayerReleaseSection {\n  title: string\n  items: string[]\n}\n\n` +
    `export interface GeneratedPlayerReleaseEntry {\n  date: string\n  title: string\n  version: string\n  sections: GeneratedPlayerReleaseSection[]\n}\n\n` +
    `export const generatedPlayerRelease: GeneratedPlayerReleaseEntry | null = ${JSON.stringify(release, null, 2)}\n`
}

function parseArgs(argv) {
  const [command = 'validate', ...rest] = argv
  const options = { command, repo: process.cwd() }
  for (let index = 0; index < rest.length; index += 1) {
    const key = rest[index]
    if (!key.startsWith('--')) fail(`未知参数：${key}`)
    const value = rest[++index]
    if (value === undefined) fail(`参数 ${key} 缺少值。`)
    options[key.slice(2)] = value
  }
  return options
}

export function main(argv = process.argv.slice(2)) {
  const options = parseArgs(argv)
  if (options.command === 'validate') {
    const { entries } = loadLedger(options.repo)
    console.log(`[玩家更新日志账本] 格式与玩家文案检查通过：${entries.length} 项。`)
    return
  }
  if (options.command !== 'release') fail(`未知命令：${options.command}`)
  if (!options.from || !options.to) fail('release 命令必须提供 --from 与 --to。')
  const result = evaluateRelease(options.repo, options.from, options.to)
  if (options.output) {
    const output = path.resolve(options.output)
    fs.mkdirSync(path.dirname(output), { recursive: true })
    fs.writeFileSync(output, renderTypeScript(result.release), 'utf8')
  }
  const summary = {
    schemaVersion: 1,
    from: result.from,
    to: result.to,
    declaredEntries: result.rangedEntries.map(entry => entry.id),
    playerEntryCount: result.playerEntries.length,
    coveredPlayerFiles: result.productFiles,
    release: result.release,
  }
  if (options.summary) fs.writeFileSync(path.resolve(options.summary), `${JSON.stringify(summary, null, 2)}\n`, 'utf8')
  console.log(`[玩家更新日志账本] 正式发布区间检查通过：${result.from.slice(0, 7)} -> ${result.to.slice(0, 7)}；玩家条目 ${result.playerEntries.length} 项，源码覆盖 ${result.productFiles.length} 个。`)
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { main() } catch (error) { console.error(error.message); process.exitCode = 1 }
}
