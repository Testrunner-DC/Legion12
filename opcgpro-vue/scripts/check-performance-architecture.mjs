import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = path.resolve(frontendRoot, '..')

const normalizePath = value => value.replaceAll('\\', '/')

function extractScript(source, filename) {
  if (!filename.endsWith('.vue')) return source
  return [...source.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)].map(match => match[1]).join('\n')
}

function calledName(expression) {
  if (ts.isIdentifier(expression)) return expression.text
  if (ts.isPropertyAccessExpression(expression)) {
    const owner = ts.isIdentifier(expression.expression) ? expression.expression.text : ''
    return owner ? `${owner}.${expression.name.text}` : expression.name.text
  }
  return ''
}

export function analyzeSource(source, filename = 'fixture.ts') {
  const script = extractScript(source, filename)
  const sourceFile = ts.createSourceFile(filename, script, ts.ScriptTarget.Latest, true,
    filename.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS)
  const result = { rawFetch: 0, interval: 0, maximumParallelPageLoad: 0 }

  const bindings = new Map()
  const asyncNames = new Set()
  function collect(node) {
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) bindings.set(node.name.text, node.initializer)
    if (ts.isFunctionDeclaration(node) && node.name && node.modifiers?.some(item => item.kind === ts.SyntaxKind.AsyncKeyword)) asyncNames.add(node.name.text)
    ts.forEachChild(node, collect)
  }
  collect(sourceFile)
  const unwrap = node => {
    while (node && (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node))) node = node.expression
    return node
  }
  function arraySize(node, seen = new Set()) {
    node = unwrap(node)
    if (!node) return 0
    if (ts.isIdentifier(node) && bindings.has(node.text) && !seen.has(node.text)) return arraySize(bindings.get(node.text), new Set([...seen, node.text]))
    if (ts.isArrayLiteralExpression(node)) return node.elements.reduce((sum, item) => sum + (ts.isSpreadElement(item) ? arraySize(item.expression, seen) || 1 : 1), 0)
    // Count statically known mapped batches; dynamic collection sizes need runtime evidence.
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression) && node.expression.name.text === 'map') return arraySize(node.expression.expression, seen)
    return 0
  }
  function requestWeight(node, seen = new Set()) {
    node = unwrap(node)
    if (!node) return 0
    if (ts.isVoidExpression(node) || ts.isAwaitExpression(node)) return requestWeight(node.expression, seen)
    if (ts.isCallExpression(node)) {
      const name = calledName(node.expression)
      if (['Promise.all', 'Promise.allSettled'].includes(name)) return arraySize(node.arguments[0])
      if (ts.isPropertyAccessExpression(node.expression) && ['then', 'catch', 'finally'].includes(node.expression.name.text)) return requestWeight(node.expression.expression, seen)
      if (asyncNames.has(name) || /^(?:load|refresh|fetch)[A-Z_]/.test(name) || /(?:Api\.|api\.|platformRequest$)/.test(name)) return 1
      if (ts.isIdentifier(node.expression) && bindings.has(name) && !seen.has(name)) {
        const target = unwrap(bindings.get(name))
        if (target && (ts.isArrowFunction(target) || ts.isFunctionExpression(target))) return requestWeight(target.body, new Set([...seen, name]))
        if (target && ts.isPropertyAccessExpression(target)) return /Api$|api$/.test(calledName(target.expression)) ? 1 : 0
      }
    }
    return 0
  }
  function scanStatements(statements, initial = 0) {
    let pending = initial
    const started = new Map()
    for (const statement of statements) {
      if (ts.isIfStatement(statement)) {
        const branch = node => ts.isBlock(node) ? node.statements : [node]
        pending = Math.max(scanStatements(branch(statement.thenStatement), pending), statement.elseStatement ? scanStatements(branch(statement.elseStatement), pending) : pending)
      } else if (ts.isTryStatement(statement)) {
        pending = Math.max(scanStatements(statement.tryBlock.statements, pending), statement.catchClause ? scanStatements(statement.catchClause.block.statements, pending) : pending)
        if (statement.finallyBlock) pending = scanStatements(statement.finallyBlock.statements, pending)
      } else {
        const expressions = ts.isExpressionStatement(statement) ? [statement.expression]
          : ts.isVariableStatement(statement) ? statement.declarationList.declarations.map(item => item.initializer).filter(Boolean) : []
        for (const expression of expressions) {
          const value = unwrap(expression)
          if (ts.isAwaitExpression(value)) {
            const operand = unwrap(value.expression)
            const existing = ts.isIdentifier(operand) ? started.get(operand.text) || 0 : 0
            result.maximumParallelPageLoad = Math.max(result.maximumParallelPageLoad, pending + (existing ? 0 : requestWeight(operand)))
            pending = Math.max(0, pending - existing)
          } else {
            const count = requestWeight(value)
            pending += count
            if (ts.isVariableStatement(statement)) {
              const declaration = statement.declarationList.declarations.find(item => item.initializer === expression)
              if (declaration && ts.isIdentifier(declaration.name)) started.set(declaration.name.text, count)
            }
          }
        }
      }
      result.maximumParallelPageLoad = Math.max(result.maximumParallelPageLoad, pending)
    }
    return pending
  }
  function visit(node) {
    if (ts.isCallExpression(node)) {
      const name = calledName(node.expression)
      if (name === 'fetch' || name.endsWith('.fetch')) result.rawFetch += 1
      if (name === 'setInterval' || name.endsWith('.setInterval')) result.interval += 1
      if (['Promise.all', 'Promise.allSettled'].includes(name))
        result.maximumParallelPageLoad = Math.max(result.maximumParallelPageLoad, arraySize(node.arguments[0]))
    }
    if (ts.isBlock(node) && (ts.isFunctionLike(node.parent) || ts.isSourceFile(node.parent))) scanStatements(node.statements)
    ts.forEachChild(node, visit)
  }
  visit(sourceFile)
  return result
}

function sourceFiles(root) {
  const result = []
  const visit = directory => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const absolute = path.join(directory, entry.name)
      if (entry.isDirectory()) visit(absolute)
      else if (/\.(?:ts|tsx|vue)$/.test(entry.name)) result.push(absolute)
    }
  }
  visit(root)
  return result.sort()
}

function mapByPath(entries, label, issues) {
  const result = new Map()
  for (const entry of entries ?? []) {
    const itemPath = normalizePath(String(entry.path ?? ''))
    if (!itemPath || result.has(itemPath)) issues.push(`${label} contains a missing or duplicate path: ${itemPath || '<empty>'}`)
    if (!Number.isInteger(entry.maximumCalls) || entry.maximumCalls < 0)
      issues.push(`${label} ${itemPath} has invalid maximumCalls`)
    if (!String(entry.purpose ?? '').trim()) issues.push(`${label} ${itemPath} must explain its purpose`)
    result.set(itemPath, entry)
  }
  return result
}

export function evaluateArchitecture({ budgets, exceptionDocument, findings, today = new Date() }) {
  const issues = []
  if (budgets?.schema !== 1) issues.push('performance-budgets.json schema must be 1')
  if (exceptionDocument?.schema !== 1) issues.push('performance-exceptions.json schema must be 1')
  const defaults = budgets?.defaults ?? {}
  if (defaults.steadyStateHttpRequestsPerMinute !== 0) issues.push('steady-state HTTP budget must remain zero')
  if (defaults.hiddenTabRequestsPerMinute !== 0) issues.push('hidden-tab HTTP budget must remain zero')
  if (defaults.maximumMutationRequestsPerAction !== 1) issues.push('one action must remain limited to one mutation request')
  if (!Number.isInteger(defaults.maximumParallelPageLoads) || defaults.maximumParallelPageLoads > 3)
    issues.push('default parallel page-load budget must be an integer no greater than 3')
  const runtime = budgets?.runtimeAcceptance ?? {}
  if (runtime.windowSeconds !== 60) issues.push('runtime acceptance window must remain 60 seconds')
  if (!Number.isInteger(runtime.minimumSamples) || runtime.minimumSamples < 20)
    issues.push('runtime acceptance must require at least 20 samples')
  if (!Number.isInteger(runtime.slowRequestThresholdMilliseconds) || runtime.slowRequestThresholdMilliseconds > 1000)
    issues.push('slow-request threshold must be an integer no greater than 1000ms')
  const cappedPercent = (name, value, maximum) => {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > maximum)
      issues.push(`${name} budget must be a percentage no greater than ${maximum}%`)
  }
  cappedPercent('slow-request', runtime.maximumSlowRequestPercent, 5)
  cappedPercent('server-error', runtime.maximumServerErrorPercent, 1)
  cappedPercent('rate-limited', runtime.maximumRateLimitedPercent, 20)
  for (const [route, budget] of Object.entries(budgets?.routes ?? {})) {
    if (!route.startsWith('/')) issues.push(`route budget must use an absolute route: ${route}`)
    if ((budget.maximumInitialApiRequests ?? defaults.maximumInitialApiRequests) > defaults.maximumInitialApiRequests)
      issues.push(`route ${route} silently exceeds the default initial API request budget`)
  }

  const approvedFetch = mapByPath(budgets?.approvedRawFetchModules, 'approvedRawFetchModules', issues)
  const approvedIntervals = mapByPath(budgets?.approvedIntervalModules, 'approvedIntervalModules', issues)
  const exceptions = new Map()
  const ids = new Set()
  const todayText = today.toISOString().slice(0, 10)
  for (const entry of exceptionDocument?.exceptions ?? []) {
    const key = `${entry.rule}:${normalizePath(String(entry.path ?? ''))}`
    if (!entry.id || ids.has(entry.id)) issues.push(`performance exception has a missing or duplicate id: ${entry.id || '<empty>'}`)
    ids.add(entry.id)
    if (!['raw-fetch', 'interval', 'parallel-page-load'].includes(entry.rule))
      issues.push(`${entry.id} uses an unsupported rule`)
    if (!entry.path || String(entry.path).includes('*')) issues.push(`${entry.id} must name one exact source path`)
    if (!Number.isInteger(entry.allowedValue) || entry.allowedValue < 1) issues.push(`${entry.id} has invalid allowedValue`)
    if (!String(entry.owner ?? '').trim() || !String(entry.reason ?? '').trim()) issues.push(`${entry.id} must include owner and reason`)
    if (!/^\d{4}-\d{2}-\d{2}$/.test(String(entry.expiresAt ?? ''))) issues.push(`${entry.id} has invalid expiresAt`)
    else if (entry.expiresAt < todayText) issues.push(`${entry.id} expired on ${entry.expiresAt}`)
    if (exceptions.has(key)) issues.push(`multiple performance exceptions target ${key}`)
    exceptions.set(key, entry)
  }

  const seenExceptions = new Set()
  for (const [file, metric] of findings) {
    const rawBase = approvedFetch.get(file)?.maximumCalls ?? 0
    const intervalBase = approvedIntervals.get(file)?.maximumCalls ?? 0
    const checks = [
      ['raw-fetch', metric.rawFetch, rawBase],
      ['interval', metric.interval, intervalBase],
      ['parallel-page-load', metric.maximumParallelPageLoad, defaults.maximumParallelPageLoads],
    ]
    for (const [rule, actual, base] of checks) {
      const key = `${rule}:${file}`
      const exception = exceptions.get(key)
      const allowed = exception?.allowedValue ?? base
      if (actual > allowed) issues.push(`${file} ${rule} is ${actual}, above allowed value ${allowed}`)
      if (exception) {
        seenExceptions.add(key)
        if (actual <= base) issues.push(`${exception.id} is stale because ${file} is back within the default budget`)
        else if (actual !== exception.allowedValue)
          issues.push(`${exception.id} must lock the exact current value ${actual}, not ${exception.allowedValue}`)
      }
    }
  }

  for (const [key, exception] of exceptions) {
    if (!seenExceptions.has(key)) issues.push(`${exception.id} is stale because its source path or finding no longer exists`)
  }
  return issues
}

export function runArchitectureCheck({ root = repositoryRoot, today } = {}) {
  const frontend = path.join(root, 'opcgpro-vue')
  const budgets = JSON.parse(fs.readFileSync(path.join(root, 'ops', 'performance-budgets.json'), 'utf8'))
  const exceptionDocument = JSON.parse(fs.readFileSync(path.join(root, 'ops', 'performance-exceptions.json'), 'utf8'))
  const findings = new Map()
  for (const absolute of sourceFiles(path.join(frontend, 'src'))) {
    const relative = normalizePath(path.relative(frontend, absolute))
    findings.set(relative, analyzeSource(fs.readFileSync(absolute, 'utf8'), relative))
  }
  const issues = evaluateArchitecture({ budgets, exceptionDocument, findings, today })
  if (issues.length) {
    for (const issue of issues) console.error(`PERF ARCH FAIL: ${issue}`)
    return 1
  }
  const totals = [...findings.values()].reduce((sum, item) => ({
    rawFetch: sum.rawFetch + item.rawFetch,
    interval: sum.interval + item.interval,
    maximumParallelPageLoad: Math.max(sum.maximumParallelPageLoad, item.maximumParallelPageLoad),
  }), { rawFetch: 0, interval: 0, maximumParallelPageLoad: 0 })
  console.log(`Performance architecture lock passed: ${findings.size} sources, ${totals.rawFetch} fetch calls, ${totals.interval} intervals, max fan-out ${totals.maximumParallelPageLoad}`)
  return 0
}

const invokedPath = process.argv[1] ? path.resolve(process.argv[1]) : ''
if (invokedPath === fileURLToPath(import.meta.url)) process.exitCode = runArchitectureCheck()
