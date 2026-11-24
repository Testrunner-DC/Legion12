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
//   L12_REHEARSAL_DECK_CODE            required: deck code used by every player
//   L12_REHEARSAL_EVIDENCE             default artifacts/rehearsal-tournament.json

const fs = require('fs');
const path = require('path');

const base = (process.env.L12_REHEARSAL_BASE || 'http://127.0.0.1:8084').replace(/\/+$/, '');
const organizerName = process.env.L12_REHEARSAL_ORGANIZER || 'Aimin';
const organizerPassword = process.env.L12_REHEARSAL_ORGANIZER_PASSWORD || '';
const playerPassword = process.env.L12_REHEARSAL_PLAYER_PASSWORD || '';
const playerCount = Number(process.env.L12_REHEARSAL_PLAYERS || 8);
const deckCode = process.env.L12_REHEARSAL_DECK_CODE || '';
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
  const response = await fetch(base + urlPath, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
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
    deckVisibility: 'staff',
    disasterMode: 'off',
    banList: 'none',
    roundMinutes: 30,
    checkInMinutes: 30,
    swissRounds: 3,
    registrationVisibility: 'public',
    lateGraceMinutes: 5,
    operationMode: 'managed',
  },
});

async function main() {
  if (!organizerPassword || !playerPassword || !deckCode) {
    throw new Error('缺少环境变量：L12_REHEARSAL_ORGANIZER_PASSWORD / L12_REHEARSAL_PLAYER_PASSWORD / L12_REHEARSAL_DECK_CODE');
  }

  const organizer = await login(organizerName, organizerPassword);
  const players = [];
  for (let index = 1; index <= playerCount; index++) {
    const name = '彩排' + String(index).padStart(2, '0');
    players.push({ name, token: await login(name, playerPassword) });
  }
  assert(players.length === playerCount, playerCount + ' 个彩排账号全部登录成功', players.map((p) => p.name));

  const created = await api('POST', '/api/tournaments', {
    token: organizer, idempotencyKey: nextKey('create'), body: createBody(),
  });
  const createdTournament = tournamentOf(created);
  if (!assert(created.status < 300 && createdTournament && createdTournament.id, '建赛成功', created.step)) return;
  const id = createdTournament.id;
  evidence.tournamentId = id;

  for (const player of players) {
    const registered = await api('POST', '/api/tournaments/' + id + '/registrations', {
      token: player.token, idempotencyKey: nextKey('register'), body: { deckCode },
    });
    assert(registered.status < 300, player.name + ' 报名成功', registered.step);
  }

  const detail = await loadTournament(organizer, id);
  const participants = (detail.tournament.participants || []).filter((row) => row.status !== 'withdrawn');
  assert(participants.length === playerCount, '报名人数＝' + playerCount, participants.length);

  for (const player of players) {
    const checked = await api('POST', '/api/tournaments/' + id + '/pre-check-in', {
      token: player.token, idempotencyKey: nextKey('checkin'), body: { deckCode },
    });
    assert(checked.status < 300, player.name + ' 签到锁牌成功', checked.step);
  }

  const started = await api('POST', '/api/tournaments/' + id + '/start', {
    token: organizer, idempotencyKey: nextKey('start'), body: {},
  });
  assert(started.status < 300, '开赛成功', started.step);

  const restarted = await api('POST', '/api/tournaments/' + id + '/start', {
    token: organizer, idempotencyKey: nextKey('start-replay'), body: {},
  });
  assert(restarted.status >= 400, '重复开赛被拒绝', { status: restarted.status });

  for (let roundIndex = 1; roundIndex <= 3; roundIndex++) {
    const opened = await api('POST', '/api/tournaments/' + id + '/rounds', {
      token: organizer, idempotencyKey: nextKey('round'), body: {},
    });
    assert(opened.status < 300, '第 ' + roundIndex + ' 轮开出', opened.step);

    const roundStart = await api('POST', '/api/tournaments/' + id + '/rounds/' + roundIndex + '/start', {
      token: organizer, idempotencyKey: nextKey('round-start'), body: {},
    });
    assert(roundStart.status < 300, '第 ' + roundIndex + ' 轮开始', roundStart.step);

    const current = await loadTournament(organizer, id);
    const roundMatches = matchesOf(current.tournament).filter((match) => match.round === roundIndex);
    assert(roundMatches.length > 0, '第 ' + roundIndex + ' 轮有桌次', roundMatches.length);

    for (const match of roundMatches) {
      if (match.result) continue;
      const ruled = await api('POST', '/api/tournaments/' + id + '/matches/' + match.id + '/rulings', {
        token: organizer,
        idempotencyKey: nextKey('ruling'),
        body: { kind: 'result', decision: 'player-a', reason: '赛事演练自动裁定' },
      });
      assert(ruled.status < 300, '第 ' + roundIndex + ' 轮桌次 ' + match.id + ' 记录赛果', ruled.step);
    }

    const afterRound = await loadTournament(organizer, id);
    const stillOpen = unresolved(matchesOf(afterRound.tournament).filter((m) => m.round === roundIndex));
    assert(stillOpen.length === 0, '第 ' + roundIndex + ' 轮全部桌次已出结果', stillOpen.map((m) => m.id));

    if (afterRound.tournament.status === 'completed') break;
  }

  const completed = await api('POST', '/api/tournaments/' + id + '/complete', {
    token: organizer, idempotencyKey: nextKey('complete'), body: {},
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
