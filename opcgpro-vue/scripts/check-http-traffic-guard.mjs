import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = path.resolve(frontendRoot, '..')
const server = fs.readFileSync(path.join(repositoryRoot, '服务端WebSocket', 'TwelveLegions', 'L12WebSocketServer.cs'), 'utf8')
const reliability = fs.readFileSync(path.join(repositoryRoot, '服务端WebSocket', 'TwelveLegions', 'L12HttpReliability.cs'), 'utf8')
let checks = 0

assert(server.includes('new L12HttpTrafficGuard()')); checks += 1
assert(server.indexOf('L12HttpExceptionBoundary.InvokeAsync') < server.indexOf('_app.Use(async (context, next) =>')); checks += 1
assert(server.includes('trafficGuard.Acquire(context.Request')); checks += 1
assert(server.includes('StatusCodes.Status429TooManyRequests') && server.includes('"rate_limited"')); checks += 1
assert(server.includes('Retry-After, RateLimit-Limit, RateLimit-Remaining')); checks += 1
assert(reliability.includes('maximumPartitions = 8_192') && reliability.includes('_windows.Count >= _maximumPartitions')); checks += 1
assert(reliability.includes('path.StartsWithSegments("/api")') && reliability.includes('HttpMethods.IsOptions(method)')); checks += 1
assert(reliability.includes('AuthenticationLimit') && reliability.includes('MutationLimit') && reliability.includes('ExpensiveLimit')); checks += 1
assert(reliability.includes('AuthenticatedClientReadLimit') && reliability.includes('AuthenticatedClientMutationLimit')); checks += 1
assert(reliability.includes('IsExpensive(method, path)')); checks += 1
assert(reliability.includes('context.Response.HasStarted') && reliability.includes('"internal_error"')); checks += 1
assert(!reliability.includes('{error}"')); checks += 1
assert(reliability.includes('RetryAfter(now, window.ResetAt)') && reliability.includes('RemoveExpired(now)')); checks += 1

console.log(`HTTP traffic reliability architecture: ${checks}/${checks} checks passed`)
