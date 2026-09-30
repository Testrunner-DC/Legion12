import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import {createApp} from 'vue'
import '/src/style.css'
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
net.l12State.endpoint=location.origin.replace(/^http/,'ws')+'/ws'
const account=id=>({id,username:id,role:'player',createdAt:'2026-09-30T00:00:00Z',publicHistory:false})
platform.platformState.token='token-a';platform.platformState.account=account('account-a')
const Summary=(await import('/src/l12/site/SeasonSummaryNotice.vue')).default
createApp(Summary).mount('#app')
window.__seasonSummaryTest={switchAccount(id){return platform.login(id,'Password123!')},refresh(){window.dispatchEvent(new CustomEvent('l12-resource-seasonSummaryNotifications'))}}
`
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-season-summary-notifications'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'season-summary-notification-fixture',
    resolveId(id) { if (id === '/__season_summary__.js') return id },
    load(id) { if (id === '/__season_summary__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__season_summary__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__season_summary__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const summary = (id, seasonName) => ({
  id, seasonId: `season-${id}`, seasonName, faction: '秩序', placed: true,
  rankLabel: '冠冕', factionRank: 1, overallRank: 2, sevenValue: 123456,
  displayValue: '七曜值 123,456', wins: 46, losses: 14, winRate: 76.7,
  factionTitle: '秩序冠首', masterTitles: [], titles: ['秩序冠首'],
  availableAt: '2026-09-30T00:00:00Z',
})
const deferred = () => {
  let resolve
  const promise = new Promise(done => { resolve = done })
  return { promise, resolve }
}
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))
const waitForSignal = (promise, label) => Promise.race([
  promise,
  sleep(5_000).then(() => { throw new Error(`Timed out waiting for ${label}`) }),
])
const acknowledgementId = request => decodeURIComponent(new URL(request.url()).pathname.split('/').at(-2))
let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  const url = `http://127.0.0.1:${port}/__season_summary__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    const accountAStarted = deferred()
    const releaseAccountA = deferred()
    await page.route('**/api/auth/login', route => route.fulfill({ json: {
      account: { id: 'account-b', username: 'account-b', role: 'player', createdAt: '2026-09-30T00:00:00Z', publicHistory: false },
      token: 'token-b',
    } }))
    await page.route('**/api/me/season-summary-notifications**', async route => {
      const authorization = route.request().headers().authorization
      if (authorization === 'Bearer token-a') {
        accountAStarted.resolve()
        await releaseAccountA.promise
        await route.fulfill({ json: [summary('a', 'A账号赛季')] })
      } else await route.fulfill({ json: [summary('b', 'B账号赛季')] })
    })
    await page.goto(url)
    await waitForSignal(accountAStarted.promise, 'account A delayed GET')
    await page.evaluate(() => window.__seasonSummaryTest.switchAccount('account-b'))
    await page.getByRole('heading', { name: /B账号赛季/ }).waitFor()
    releaseAccountA.resolve()
    await sleep(100)
    assert.equal(await page.getByRole('heading', { name: /B账号赛季/ }).count(), 1)
    assert.equal(await page.getByRole('heading', { name: /A账号赛季/ }).count(), 0,
      'the stale account A response must not replace account B')
    await page.close()
  }

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    let getCount = 0
    let acknowledged = false
    const acknowledgeStarted = deferred()
    const staleGetStarted = deferred()
    const releaseStaleGet = deferred()
    await page.route('**/api/me/season-summary-notifications**', async route => {
      if (route.request().method() === 'POST') {
        acknowledged = true
        acknowledgeStarted.resolve()
        await route.fulfill({ status: 204, body: '' })
        return
      }
      getCount += 1
      if (getCount === 2) {
        staleGetStarted.resolve()
        await releaseStaleGet.promise
        await route.fulfill({ json: [summary('ack', '确认竞态赛季')] })
      } else await route.fulfill({ json: acknowledged ? [] : [summary('ack', '确认竞态赛季')] })
    })
    await page.goto(url)
    await page.getByRole('heading', { name: /确认竞态赛季/ }).waitFor()
    await sleep(100)
    await page.evaluate(() => window.__seasonSummaryTest.refresh())
    await waitForSignal(staleGetStarted.promise, 'acknowledge stale GET')
    await page.getByRole('button', { name: '保存并确认' }).click()
    await waitForSignal(acknowledgeStarted.promise, 'acknowledge POST')
    await page.getByRole('dialog').waitFor({ state: 'detached' })
    releaseStaleGet.resolve()
    await sleep(150)
    assert.equal(await page.getByRole('dialog').count(), 0,
      'a stale GET must not resurrect an acknowledged notification')
    assert(getCount >= 3, 'acknowledge success must be followed by an authoritative refresh')
    await page.close()
  }

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    let getCount = 0
    const staleGetStarted = deferred()
    const releaseStaleGet = deferred()
    await page.route('**/api/me/season-summary-notifications**', async route => {
      if (route.request().method() === 'POST') {
        await route.fulfill({ status: 500, json: { message: 'injected acknowledge failure' } })
        return
      }
      getCount += 1
      if (getCount === 2) {
        staleGetStarted.resolve()
        await releaseStaleGet.promise
      }
      await route.fulfill({ json: [summary('retry', '确认失败重试赛季')] })
    })
    await page.goto(url)
    await page.getByRole('heading', { name: /确认失败重试赛季/ }).waitFor()
    await sleep(100)
    await page.evaluate(() => window.__seasonSummaryTest.refresh())
    await waitForSignal(staleGetStarted.promise, 'failed acknowledge stale GET')
    await page.getByRole('button', { name: '保存并确认' }).click()
    releaseStaleGet.resolve()
    await page.getByRole('heading', { name: /确认失败重试赛季/ }).waitFor()
    assert.equal(await page.getByRole('dialog').count(), 1,
      'a failed acknowledge must restore the server-unread notification')
    await page.close()
  }

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    const unread = new Set(['first-fails', 'second-succeeds'])
    const acknowledgements = []
    const firstStarted = deferred()
    const releaseFirstFailure = deferred()
    const secondRefreshServed = deferred()
    await page.route('**/api/me/season-summary-notifications**', async route => {
      if (route.request().method() === 'POST') {
        const id = acknowledgementId(route.request())
        acknowledgements.push(id)
        if (id === 'first-fails') {
          firstStarted.resolve()
          await releaseFirstFailure.promise
          await route.fulfill({ status: 503, json: { message: 'injected late first failure' } })
        } else {
          unread.delete(id)
          await route.fulfill({ status: 204, body: '' })
        }
        return
      }
      const result = [...unread].map(id => summary(id, id === 'first-fails' ? '先确认后失败赛季' : '后确认成功赛季'))
      if (!unread.has('second-succeeds')) secondRefreshServed.resolve()
      await route.fulfill({ json: result })
    })
    await page.goto(url)
    await page.getByRole('heading', { name: /先确认后失败赛季/ }).waitFor()
    await page.getByRole('button', { name: '保存并确认' }).click()
    await waitForSignal(firstStarted.promise, 'first acknowledgement pending')
    await page.getByRole('heading', { name: /后确认成功赛季/ }).waitFor()
    await page.getByRole('button', { name: '保存并确认' }).click()
    await waitForSignal(secondRefreshServed.promise, 'second acknowledgement authoritative refresh')
    await page.getByRole('dialog').waitFor({ state: 'detached' })
    releaseFirstFailure.resolve()
    await page.getByRole('heading', { name: /先确认后失败赛季/ }).waitFor()
    assert.deepEqual(acknowledgements, ['first-fails', 'second-succeeds'])
    assert.equal(await page.getByRole('heading', { name: /后确认成功赛季/ }).count(), 0,
      'the successful later acknowledgement must stay dismissed')
    await page.close()
  }

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    const unread = new Set(['first-succeeds', 'second-fails'])
    const acknowledgements = []
    const firstStarted = deferred()
    const secondStarted = deferred()
    const releaseFirstSuccess = deferred()
    const releaseSecondFailure = deferred()
    const firstRefreshServed = deferred()
    await page.route('**/api/me/season-summary-notifications**', async route => {
      if (route.request().method() === 'POST') {
        const id = acknowledgementId(route.request())
        acknowledgements.push(id)
        if (id === 'first-succeeds') {
          firstStarted.resolve()
          await releaseFirstSuccess.promise
          unread.delete(id)
          await route.fulfill({ status: 204, body: '' })
        } else {
          secondStarted.resolve()
          await releaseSecondFailure.promise
          await route.fulfill({ status: 503, json: { message: 'injected late second failure' } })
        }
        return
      }
      const result = [...unread].map(id => summary(id, id === 'first-succeeds' ? '先确认成功赛季' : '后确认失败赛季'))
      if (!unread.has('first-succeeds')) firstRefreshServed.resolve()
      await route.fulfill({ json: result })
    })
    await page.goto(url)
    await page.getByRole('heading', { name: /先确认成功赛季/ }).waitFor()
    await page.getByRole('button', { name: '保存并确认' }).click()
    await waitForSignal(firstStarted.promise, 'first acknowledgement awaiting success')
    await page.getByRole('heading', { name: /后确认失败赛季/ }).waitFor()
    await page.getByRole('button', { name: '保存并确认' }).click()
    await waitForSignal(secondStarted.promise, 'second acknowledgement awaiting failure')
    releaseFirstSuccess.resolve()
    await waitForSignal(firstRefreshServed.promise, 'first acknowledgement authoritative refresh')
    await page.getByRole('dialog').waitFor({ state: 'detached' })
    releaseSecondFailure.resolve()
    await page.getByRole('heading', { name: /后确认失败赛季/ }).waitFor()
    assert.deepEqual(acknowledgements, ['first-succeeds', 'second-fails'])
    assert.equal(await page.getByRole('heading', { name: /先确认成功赛季/ }).count(), 0,
      'the successful earlier acknowledgement must stay dismissed')
    await page.close()
  }

  {
    const page = await browser.newPage({ viewport: { width: 1024, height: 600 } })
    let postCount = 0
    let unread = true
    const postStarted = deferred()
    const releasePost = deferred()
    await page.route('**/api/me/season-summary-notifications**', async route => {
      if (route.request().method() === 'POST') {
        postCount += 1
        postStarted.resolve()
        await releasePost.promise
        unread = false
        await route.fulfill({ status: 204, body: '' })
        return
      }
      await route.fulfill({ json: unread ? [summary('same-id', '重复确认赛季')] : [] })
    })
    await page.goto(url)
    await page.getByRole('heading', { name: /重复确认赛季/ }).waitFor()
    await page.getByRole('button', { name: '保存并确认' }).evaluate(button => {
      button.click()
      button.click()
    })
    await waitForSignal(postStarted.promise, 'duplicate acknowledgement POST')
    await sleep(100)
    assert.equal(postCount, 1, 'the same notification must have only one acknowledgement in flight')
    releasePost.resolve()
    await page.getByRole('dialog').waitFor({ state: 'detached' })
    await sleep(100)
    assert.equal(postCount, 1, 'the same notification must not be acknowledged twice')
    await page.close()
  }

  console.log('Season summary notification races: account switch, stale reads, failure recovery, interleaved acknowledgements, and same-ID deduplication passed')
} finally {
  await browser?.close()
  await server.close()
}
