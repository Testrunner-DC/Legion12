#!/usr/bin/env node
// Read-only release state. A Git push never implies a test or production deploy.
import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const SHA = /^[0-9a-f]{40}$/
const SOURCE = Object.freeze({
  git: 'origin refs/heads/main (live ls-remote)',
  testrun: 'https://legion-12.com/testrun/health',
  production: 'https://legion-12.com/health',
})

function validSha(value) {
  const normalized = String(value ?? '').trim().toLowerCase()
  return SHA.test(normalized) ? normalized : null
}

export function normalizeHealth(value, source, observedAt) {
  const commit = validSha(value?.serverVersion)
  const maintenance = value?.maintenance
  const consistent = (value?.status === 'ok' && maintenance === false)
    || (value?.status === 'maintenance' && maintenance === true)
  if (value?.service !== 'twelve-legions' || !commit || !consistent) {
    return { status: 'unknown', commit: null, maintenance: null, observedAt, source }
  }
  return { status: 'verified', commit, maintenance, observedAt, source }
}

export function buildStatus({ head, branch, dirtyCount, remoteHead, testrunHealth, productionHealth, observedAt }) {
  const localCommit = validSha(head)
  const gitCommit = validSha(remoteHead)
  const testrun = normalizeHealth(testrunHealth, SOURCE.testrun, observedAt)
  const production = normalizeHealth(productionHealth, SOURCE.production, observedAt)
  return {
    schema: 1,
    observedAt,
    development: {
      commit: localCommit,
      branch: typeof branch === 'string' && branch.trim() ? branch.trim() : null,
      worktree: Number.isInteger(dirtyCount) ? (dirtyCount === 0 ? 'clean' : 'dirty') : 'unknown',
      changedPaths: Number.isInteger(dirtyCount) ? dirtyCount : null,
      source: 'local Git HEAD and porcelain status',
    },
    git: {
      status: gitCommit ? 'verified' : 'unknown',
      commit: gitCommit,
      relationToDevelopment: gitCommit && localCommit ? (gitCommit === localCommit ? 'same' : 'different') : 'unknown',
      observedAt,
      source: SOURCE.git,
    },
    testrun,
    production,
    maintenance: {
      status: production.status === 'verified' ? 'verified' : 'unknown',
      active: production.status === 'verified' ? production.maintenance : null,
      observedAt,
      source: SOURCE.production,
    },
  }
}

function git(repo, ...args) {
  try {
    return execFileSync('git', ['-C', repo, ...args], { encoding: 'utf8', timeout: 10000, stdio: ['ignore', 'pipe', 'ignore'] }).trim()
  } catch { return null }
}

async function health(url) {
  try {
    const response = await fetch(url, { cache: 'no-store', signal: AbortSignal.timeout(8000) })
    if (!response.ok) return null
    return await response.json()
  } catch { return null }
}

export async function collectStatus({ repo = resolve(dirname(fileURLToPath(import.meta.url)), '..'), offline = false } = {}) {
  const observedAt = new Date().toISOString()
  const head = git(repo, 'rev-parse', 'HEAD')
  const branch = git(repo, 'branch', '--show-current')
  const porcelain = git(repo, 'status', '--porcelain=v1', '--untracked-files=all')
  const dirtyCount = porcelain === null ? null : (porcelain ? porcelain.split(/\r?\n/).length : 0)
  let remoteHead = null
  let testrunHealth = null
  let productionHealth = null
  if (!offline) {
    const remote = git(repo, 'ls-remote', 'origin', 'refs/heads/main')
    remoteHead = remote?.split(/\s+/)[0] ?? null
    ;[testrunHealth, productionHealth] = await Promise.all([
      health(SOURCE.testrun), health(SOURCE.production),
    ])
  }
  return buildStatus({ head, branch, dirtyCount, remoteHead, testrunHealth, productionHealth, observedAt })
}

export function renderMarkdown(state) {
  const short = value => value ? value.slice(0, 12) : '未知'
  const known = value => value === null ? '未知' : (value ? '开启' : '关闭')
  const dev = state.development
  const clean = dev.worktree === 'clean' ? '干净' : dev.worktree === 'dirty' ? `有 ${dev.changedPaths} 项变动` : '未知'
  return [
    `发布状态读回：${state.observedAt}`,
    '',
    '| 维度 | 当前读数 | 权威来源 |',
    '| --- | --- | --- |',
    `| 开发候选 | ${short(dev.commit)} · ${dev.branch ?? '未知分支'} · ${clean} | ${dev.source} |`,
    `| GitHub 主线 | ${short(state.git.commit)} · 与候选${state.git.relationToDevelopment === 'same' ? '相同' : state.git.relationToDevelopment === 'different' ? '不同' : '关系未知'} | ${state.git.source} |`,
    `| 测试服 | ${short(state.testrun.commit)} · ${state.testrun.status === 'verified' ? '已核验' : '未知'} | ${state.testrun.source} |`,
    `| 正式服 | ${short(state.production.commit)} · ${state.production.status === 'verified' ? '已核验' : '未知'} | ${state.production.source} |`,
    `| 正式服维护 | ${known(state.maintenance.active)} | ${state.maintenance.source} |`,
    '',
    '各维度独立读取；Git 推送、Release 通过和服务器部署不互相推定。未知表示本次没有取得可核验读数。',
  ].join('\n')
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2)
  if (args.some(arg => !['--json', '--offline'].includes(arg))) {
    process.stderr.write('Usage: node scripts/release-status.mjs [--json] [--offline]\n')
    process.exitCode = 2
  } else {
    const state = await collectStatus({ offline: args.includes('--offline') })
    process.stdout.write(`${args.includes('--json') ? JSON.stringify(state, null, 2) : renderMarkdown(state)}\n`)
  }
}
