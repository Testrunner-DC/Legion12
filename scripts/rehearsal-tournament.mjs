#!/usr/bin/env node
// A2 — headless tournament rehearsal.
//
// Drives one full tournament over HTTP only: create -> registrations -> check-in ->
// start -> rounds -> per-table rulings -> complete -> export. Every write carries an
// Idempotency-Key, and repeat start must be refused.
//
// Environment:
//   L12_REHEARSAL_BASE                 default http://127.0.0.1:8084
//   L12_REHEARSAL_ORGANIZER            default "Aimin"
//   L12_REHEARSAL_ORGANIZER_PASSWORD   required
//   L12_REHEARSAL_PLAYER_PASSWORD      required (accounts 彩排01..彩排08 from A1)
//   L12_REHEARSAL_PLAYERS              default 8
//   L12_REHEARSAL_DECK_CODE            required: L12D2- deck code used by every player
//   L12_REHEARSAL_DECK_NAME            optional: deck name recorded with the code
//   L12_REHEARSAL_EVIDENCE             default artifacts/rehearsal-tournament.json
//
// Tournament writes are version-guarded on the server: every mutation must carry the
// tournament's current version as expectedVersion (If-Match), and each accepted write
// moves it. The rehearsal therefore re-reads the version before each write instead of
// assuming one. A create has no tournament row yet, so it is guarded by the platform
// business version taken from the host summary instead.

import fs from 'node:fs';
import path from 'node:path';

const base = (process.env.L12_REHEARSAL_BASE || 'http://127.0.0.1:8084').replace(/\/+$/, '');
const organizerName = process.env.L12_REHEARSAL_ORGANIZER || 'Aimin';
const organizerPassword = process.env.L12_REHEARSAL_ORGANIZER_PASSWORD || '';
const playerPassword = process.env.L12_REHEARSAL_PLAYER_PASSWORD || '';
const playerCount = Number(process.env.L12_REHEARSAL_PLAYERS || 8);
const deckCode = process.env.L12_REHEARSAL_DECK_CODE || '';
const deckName = process.env.L12_REHEARSAL_DECK_NAME || '';
const evidencePath = process.env.L12_REHEARSAL_EVIDENCE
  || path.join(process.cwd(), 'artifacts', 'rehearsal-tournament.json');

const evidence = { startedAt: new Date().toISOString(), base, steps: [], assertions: [], failures: [] };
const runId = Date.now().toString(16) + '-' + Math.random().toString(16).slice(2, 10);
let keySeed = 0;
const nextKey = (label) => runId + '-' + label + '-' + (++keySeed);

function fail(message, detail) {
  evidence.failures.push({ message, detail });
  console.error('FAIL ' + message + (detail === undefined ? '' : ' :: ' + JSON.stringify(detail).slice(0, 600)));
}

function assert(condition, message, detail) {
  evidence.assertions.push({ message, ok: Boolean(condition) });
  if (!condition) fail(message, detail);
  return Boolean(condition);
}

async function api(method, urlPath, options = {}) {
  const headers = { Accept: 'application/json' };
  if (options.token) headers.Authorization = 'Bearer ' + options.token;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (options.ifMatch) headers['If-Match'] = '"' + options.ifMatch + '"';
  const body = options.body === undefined || options.version === undefined
    ? options.body
    : { ...options.body, expectedVersion: options.version };
  const response = await fetch(base + urlPath, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* keep raw text */ }
  const step = { method, path: urlPath, status: response.status };
  evidence.steps.push(step);
  if (!response.ok) step.error = (json && (json.message || json.code)) || text.slice(0, 300);
  return { status: response.status, json, text, step };
}

async function login(username, password) {
  const result = await api('POST', '/api/auth/login', { body: { username, password } });
  if (result.status !== 200 || !result.json || !result.json.token) {
    throw new Error('login failed for ' + username + ': ' + result.status + ' ' + (result.step.error || ''));
  }
  return result.json.token;
}

const tournamentOf = (result) => (result.json && (result.json.tournament || result.json)) || null;

function matchesOf(tournament) {
  return (tournament.rounds || []).flatMap((round) =>
    (round.matches || []).map((match) => ({ round: round.number, ...match })));
}

const unresolved = (matches) => matches.filter((match) => !match.result);

async function loadTournament(token, id) {
  const result = await api('GET', '/api/tournaments/' + id, { token });
  return { result, tournament: tournamentOf(result) };
}

const createBody = () => ({
  tournament: {
    name: '[彩排] ' + runId + ' 赛事演练',
    format: 'swiss',
    visibility: 'public',
    maxPlayers: playerCount,
    startAt: new Date(Date.now() + 3600000).toISOString(),
    ruleset: 'official',
    description: '自动化赛事演练；跑完即归档',
    deckVisibility: 'private',
    disasterMode: 'none',
    banList: '',
    roundMinutes: 30,
    checkInMinutes: 30,
    swissRounds: 3,
    registrationVisibility: 'public',
    lateGraceMinutes: 5,
    operationMode: 'managed',
  },
});

async function versionOf(token, id) {
  const view = await loadTournament(token, id);
  const value = view.tournament && view.tournament.version;
  if (typeof value !== 'number') throw new Error('无法读取赛事版本：' + view.result.text.slice(0, 200));
  return value;
}

async function main() {
  if (!organizerPassword || !playerPassword || !deckCode) {
    throw new Error('缺少环境变量：L12_REHEARSAL_ORGANIZER_PASSWORD / L12_REHEARSAL_PLAYER_PASSWORD / L12_REHEARSAL_DECK_CODE');
  }
  const deck = { deckName, deckCode };

  const organizer = await login(organizerName, organizerPassword);
  const players = [];
  for (let index = 1; index <= playerCount; index++) {
    const name = '彩排' + String(index).padStart(2, '0');
    players.push({ name, token: await login(name, playerPassword) });
  }
  assert(players.length === playerCount, playerCount + ' 个彩排账号全部登录成功', players.map((p) => p.name));

  // A create is guarded by the platform business version, not by a tournament row that
  // does not exist yet; the host summary is the same context the admin UI creates from.
  const context = await api('GET', '/api/tournaments/summaries?section=host&page=1&pageSize=1', { token: organizer });
  const platformVersion = context.json && context.json.platformVersion;
  if (!assert(typeof platformVersion === 'number' && platformVersion > 0, '取得创建上下文版本', context.step)) return;
  evidence.platformVersion = platformVersion;

  const created = await api('POST', '/api/tournaments', {
    token: organizer, idempotencyKey: nextKey('create'), version: platformVersion, body: createBody(),
  });
  const createdTournament = tournamentOf(created);
  if (!assert(created.status < 300 && createdTournament && createdTournament.id, '建赛成功', created.step)) return;
  const id = createdTournament.id;
  evidence.tournamentId = id;

  let version = await versionOf(organizer, id);
  evidence.initialVersion = version;

  for (const player of players) {
    const registered = await api('POST', '/api/tournaments/' + id + '/registrations', {
      token: player.token, idempotencyKey: nextKey('register'), version, body: deck,
    });
    if (assert(registered.status < 300, player.name + ' 报名成功', registered.step)) version = await versionOf(organizer, id);
  }

  const detail = await loadTournament(organizer, id);
  const participants = (detail.tournament.participants || []).filter((row) => row.status !== 'withdrawn');
  assert(participants.length === playerCount, '报名人数＝' + playerCount, participants.length);

  for (const player of players) {
    const checked = await api('POST', '/api/tournaments/' + id + '/pre-check-in', {
      token: player.token, idempotencyKey: nextKey('checkin'), version, body: deck,
    });
    if (assert(checked.status < 300, player.name + ' 签到锁牌成功', checked.step)) version = await versionOf(organizer, id);
  }

  const started = await api('POST', '/api/tournaments/' + id + '/start', {
    token: organizer, idempotencyKey: nextKey('start'), version, body: {},
  });
  if (assert(started.status < 300, '开赛成功', started.step)) version = await versionOf(organizer, id);
  evidence.roundsOpened = 0;

  const restarted = await api('POST', '/api/tournaments/' + id + '/start', {
    token: organizer, idempotencyKey: nextKey('start-replay'), version, body: {},
  });
  assert(restarted.status >= 400, '重复开赛被拒绝', { status: restarted.status });

  // Start already pairs round 1 in the check-in stage, so only later rounds are opened here.
  for (let roundIndex = 1; roundIndex <= 3; roundIndex++) {
    if (roundIndex > 1) {
      const opened = await api('POST', '/api/tournaments/' + id + '/rounds', {
        token: organizer, idempotencyKey: nextKey('round'), version, body: {},
      });
      if (!assert(opened.status < 300, '第 ' + roundIndex + ' 轮开出', opened.step)) break;
      version = await versionOf(organizer, id);
      evidence.roundsOpened = roundIndex;
    }

    for (const player of players) {
      const ready = await api('POST', '/api/tournaments/' + id + '/rounds/' + roundIndex + '/check-in', {
        token: player.token, idempotencyKey: nextKey('round-checkin'), version, body: { ready: true },
      });
      if (assert(ready.status < 300, player.name + ' 第 ' + roundIndex + ' 轮签到', ready.step)) {
        version = await versionOf(organizer, id);
      }
    }

    const roundStart = await api('POST', '/api/tournaments/' + id + '/rounds/' + roundIndex + '/start', {
      token: organizer, idempotencyKey: nextKey('round-start'), version, body: {},
    });
    if (assert(roundStart.status < 300, '第 ' + roundIndex + ' 轮开始', roundStart.step)) version = await versionOf(organizer, id);

    const current = await loadTournament(organizer, id);
    const roundMatches = matchesOf(current.tournament).filter((match) => match.round === roundIndex);
    assert(roundMatches.length > 0, '第 ' + roundIndex + ' 轮有桌次', roundMatches.length);

    for (const match of roundMatches) {
      if (match.result) continue;
      const ruled = await api('POST', '/api/tournaments/' + id + '/matches/' + match.id + '/rulings', {
        token: organizer,
        idempotencyKey: nextKey('ruling'),
        version,
        body: { kind: 'result', decision: 'player-a', reason: '赛事演练自动裁定' },
      });
      if (assert(ruled.status < 300, '第 ' + roundIndex + ' 轮桌次 ' + match.id + ' 记录赛果', ruled.step)) {
        version = await versionOf(organizer, id);
      }
    }

    const afterRound = await loadTournament(organizer, id);
    const stillOpen = unresolved(matchesOf(afterRound.tournament).filter((m) => m.round === roundIndex));
    assert(stillOpen.length === 0, '第 ' + roundIndex + ' 轮全部桌次已出结果', stillOpen.map((m) => m.id));
    evidence.steps.push({ round: roundIndex, matches: roundMatches.length, resolved: roundMatches.length - stillOpen.length });

    if (afterRound.tournament.status === 'completed') break;
  }

  const completed = await api('POST', '/api/tournaments/' + id + '/complete', {
    token: organizer, idempotencyKey: nextKey('complete'), version, body: {},
  });
  assert(completed.status < 300, '赛事完赛归档', completed.step);

  const exported = await api('GET', '/api/tournaments/' + id + '/export.csv', { token: organizer });
  assert(exported.status === 200 && exported.text.length > 0, '导出 CSV 非空',
    { status: exported.status, bytes: exported.text.length });

  const final = await loadTournament(organizer, id);
  evidence.finalStatus = final.tournament.status;
  evidence.rounds = (final.tournament.rounds || []).length;
  evidence.matches = matchesOf(final.tournament).length;
  evidence.standings = (final.tournament.standings || []).slice(0, playerCount);
}

main()
  .catch((error) => fail(error.message, error.stack))
  .finally(() => {
    evidence.finishedAt = new Date().toISOString();
    evidence.ok = evidence.failures.length === 0;
    fs.mkdirSync(path.dirname(evidencePath), { recursive: true });
    fs.writeFileSync(evidencePath, JSON.stringify(evidence, null, 2) + '\n');
    const passed = evidence.assertions.filter((item) => item.ok).length;
    console.log('rehearsal ' + (evidence.ok ? 'PASS' : 'FAIL') + ': ' + passed + '/' + evidence.assertions.length
      + ' 断言通过' + (evidence.failures.length ? '，' + evidence.failures.length + ' 处失败' : '')
      + '；证据 ' + evidencePath);
    process.exit(evidence.ok ? 0 : 1);
  });
