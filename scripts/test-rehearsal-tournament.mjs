#!/usr/bin/env node
// Regression guard for scripts/rehearsal-tournament.mjs.
//
// The rehearsal drives the tournament API over HTTP only, so its contract with the
// server is the whole product. A fake server here enforces the parts the script got
// wrong in practice: every write refuses to run without an Idempotency-Key and a
// matching expectedVersion, a create is guarded by the platform business version, and
// a start already pairs round 1 in the check-in stage. The second phase mutates a copy
// of the script so it stops sending expectedVersion and asserts the guard bites.

import assert from 'node:assert/strict';
import http from 'node:http';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scriptPath = path.join(repoRoot, 'scripts', 'rehearsal-tournament.mjs');
const playerCount = 8;
const tablesPerRound = playerCount / 2;

function createRehearsalServer() {
  const state = { platformVersion: 7, sequence: 0, tournament: null };

  const view = () => ({
    id: state.tournament.id,
    version: state.tournament.version,
    status: state.tournament.status,
    participants: state.tournament.participants,
    rounds: state.tournament.rounds.map((round) => ({
      number: round.number,
      status: round.status,
      matches: round.matches.map((match) => ({ ...match })),
    })),
    standings: state.tournament.standings,
  });

  const newRound = (number) => ({
    number,
    status: 'checkin',
    matches: Array.from({ length: tablesPerRound }, (_, index) => ({
      id: 'r' + number + 'm' + (index + 1),
      result: null,
    })),
  });

  function requireWrite(headers, body) {
    if (!headers['idempotency-key']) return { status: 400, body: { code: 'idempotency_key_required' } };
    if (body.expectedVersion === undefined || body.expectedVersion === null) {
      return { status: 428, body: { code: 'expected_version_required' } };
    }
    if (body.expectedVersion !== state.tournament.version) {
      return { status: 409, body: { code: 'tournament_version_conflict' } };
    }
    return null;
  }

  const server = http.createServer((request, response) => {
    const chunks = [];
    request.on('data', (chunk) => chunks.push(chunk));
    request.on('end', () => {
      const raw = Buffer.concat(chunks).toString('utf8');
      const body = raw ? JSON.parse(raw) : {};
      const url = new URL(request.url, 'http://127.0.0.1');
      const segments = url.pathname.split('/').filter(Boolean);
      const send = (status, payload, contentType = 'application/json') => {
        response.writeHead(status, { 'Content-Type': contentType });
        response.end(typeof payload === 'string' ? payload : JSON.stringify(payload));
      };

      // POST /api/auth/login
      if (request.method === 'POST' && url.pathname === '/api/auth/login') {
        // Real tokens are opaque ASCII; a Chinese username must not leak into a header value.
        return send(200, { token: 'token-' + Buffer.from(String(body.username)).toString('hex') });
      }
      // GET /api/tournaments/summaries
      if (request.method === 'GET' && url.pathname === '/api/tournaments/summaries') {
        return send(200, { platformVersion: state.platformVersion });
      }
      // POST /api/tournaments
      if (request.method === 'POST' && url.pathname === '/api/tournaments') {
        if (!request.headers['idempotency-key']) return send(400, { code: 'idempotency_key_required' });
        if (body.expectedVersion === undefined) return send(428, { code: 'expected_version_required' });
        if (body.expectedVersion !== state.platformVersion) return send(409, { code: 'platform_version_conflict' });
        state.tournament = {
          id: 'rehearsal' + (++state.sequence),
          version: 1,
          status: 'registration',
          participants: [],
          rounds: [],
          standings: [],
        };
        return send(200, { tournament: view() });
      }

      if (!state.tournament) return send(404, { code: 'tournament_not_found' });
      const id = segments[2];
      if (id !== state.tournament.id) return send(404, { code: 'tournament_not_found' });

      // GET /api/tournaments/{id}
      if (request.method === 'GET' && segments.length === 3) return send(200, view());
      // GET /api/tournaments/{id}/export.csv
      if (request.method === 'GET' && segments[3] === 'export.csv') {
        return send(200, 'table,name\n1,彩排01\n', 'text/csv');
      }
      if (request.method !== 'POST') return send(405, { code: 'method_not_allowed' });

      const reject = requireWrite(request.headers, body);
      if (reject) return send(reject.status, reject.body);

      const action = segments[3];
      const accept = (mutate) => {
        mutate();
        state.tournament.version += 1;
        return send(200, { tournament: view() });
      };

      if (action === 'registrations') {
        if (state.tournament.status !== 'registration') return send(409, { code: 'registration_closed' });
        return accept(() => state.tournament.participants.push({
          accountId: 'p' + state.tournament.participants.length, status: 'registered', deck: body.deckCode || '',
        }));
      }
      if (action === 'pre-check-in') {
        if (state.tournament.status !== 'registration') return send(409, { code: 'check_in_closed' });
        return accept(() => {});
      }
      if (action === 'start') {
        if (state.tournament.status !== 'registration') return send(409, { code: 'already_started' });
        return accept(() => {
          state.tournament.status = 'running';
          state.tournament.rounds.push(newRound(1));
        });
      }
      if (action === 'rounds' && segments.length === 4) {
        if (state.tournament.status !== 'running') return send(409, { code: 'not_running' });
        const previous = state.tournament.rounds.at(-1);
        if (!previous || previous.status !== 'completed') return send(409, { code: 'previous_round_open' });
        return accept(() => state.tournament.rounds.push(newRound(previous.number + 1)));
      }
      if (action === 'rounds' && segments[5] === 'check-in') {
        const round = state.tournament.rounds.find((item) => item.number === Number(segments[4]));
        if (!round) return send(404, { code: 'round_not_found' });
        if (round.status !== 'checkin') return send(409, { code: 'check_in_closed' });
        return accept(() => {});
      }
      if (action === 'rounds' && segments[5] === 'start') {
        const round = state.tournament.rounds.find((item) => item.number === Number(segments[4]));
        if (!round) return send(404, { code: 'round_not_found' });
        if (round.status !== 'checkin') return send(409, { code: 'round_not_in_checkin' });
        return accept(() => { round.status = 'running'; });
      }
      if (action === 'matches' && segments[5] === 'rulings') {
        const matchId = segments[4];
        const round = state.tournament.rounds.find((item) => item.matches.some((match) => match.id === matchId));
        if (!round) return send(404, { code: 'match_not_found' });
        return accept(() => {
          const match = round.matches.find((item) => item.id === matchId);
          if (match.result) return;
          match.result = body.decision || 'player-a';
          if (round.matches.every((item) => item.result)) round.status = 'completed';
        });
      }
      if (action === 'complete') {
        if (state.tournament.rounds.some((round) => round.status !== 'completed')) {
          return send(409, { code: 'rounds_open' });
        }
        return accept(() => { state.tournament.status = 'completed'; });
      }
      return send(404, { code: 'not_found' });
    });
  });

  return server;
}

function runRehearsal(serverPath, base, extra = {}) {
  const evidencePath = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'l12-rehearsal-')), 'evidence.json');
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [serverPath], {
      env: {
        ...process.env,
        L12_REHEARSAL_BASE: base,
        L12_REHEARSAL_ORGANIZER: 'Admin',
        L12_REHEARSAL_ORGANIZER_PASSWORD: 'organizer-secret',
        L12_REHEARSAL_PLAYER_PASSWORD: 'player-secret',
        L12_REHEARSAL_DECK_CODE: 'L12D2-REGRESSION',
        L12_REHEARSAL_DECK_NAME: '回归牌库',
        L12_REHEARSAL_EVIDENCE: evidencePath,
        ...extra,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.on('close', (code) => {
      const evidence = fs.existsSync(evidencePath) ? JSON.parse(fs.readFileSync(evidencePath, 'utf8')) : null;
      fs.rmSync(path.dirname(evidencePath), { recursive: true, force: true });
      resolve({ code, stdout, stderr, evidence });
    });
  });
}

const server = createRehearsalServer();
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const base = 'http://127.0.0.1:' + server.address().port;

try {
  const passed = await runRehearsal(scriptPath, base);
  assert.equal(passed.code, 0, 'rehearsal should pass against the contract server: ' + passed.stdout + passed.stderr);
  assert.match(passed.stdout, /rehearsal PASS/, 'rehearsal must report PASS');
  assert.ok(passed.evidence, 'rehearsal must write evidence');
  assert.equal(passed.evidence.ok, true, 'evidence must record success');
  assert.equal(passed.evidence.finalStatus, 'completed', 'the tournament must reach the completed state');
  assert.equal(passed.evidence.rounds, 3, 'three Swiss rounds must be opened and ruled');
  assert.equal(passed.evidence.matches, playerCount / 2 * 3, 'every table of every round must get a result');

  // Negative phase: drop the expectedVersion threading and prove the guard refuses it.
  const source = fs.readFileSync(scriptPath, 'utf8');
  const mutated = source.replace(
    /const body = options\.body === undefined \|\| options\.version === undefined[\s\S]*?: \{ \.\.\.options\.body, expectedVersion: options\.version \};/,
    'const body = options.body;',
  );
  assert.notEqual(mutated, source, 'mutation must rewrite the expectedVersion threading');
  const mutatedPath = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'l12-rehearsal-mutant-')), 'rehearsal-mutant.mjs');
  fs.writeFileSync(mutatedPath, mutated);
  try {
    const failed = await runRehearsal(mutatedPath, base);
    assert.equal(failed.code, 1, 'a rehearsal without expectedVersion must fail');
    assert.match(failed.stdout + failed.stderr, /expected_version_required|建赛成功/,
      'the failure must come from the version precondition');
    assert.equal(failed.evidence.ok, false, 'evidence must record the failure');
  } finally {
    fs.rmSync(path.dirname(mutatedPath), { recursive: true, force: true });
  }

  console.log('rehearsal contract regression: PASS (' + passed.evidence.steps.length + ' steps, mutant rejected)');
} finally {
  await new Promise((resolve) => server.close(resolve));
}
