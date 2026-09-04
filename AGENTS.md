# Legion12 project working rules

These rules apply to every change in this repository.

1. Before fixing a bug, read `docs/BUGFIX-REGISTRY.md`, identify matching prior fixes, and inspect the current local diff. The local worktree is the source of truth; never reset, checkout, or overwrite it from a remote copy.
2. Fix shared causes before individual cards. For every card-related bug, scan the complete existing card pool for cards with the same timing, payment, targeting, zone, or presentation pattern. Record the scan query and affected cards.
3. Add a regression guard with every fix: a server test, frontend contract/build check, catalog invariant, or a combination. Review `git diff` after the change and do not replace unrelated local edits.
4. For newly assigned work, first present an implementation plan and wait for explicit approval before modifying files. Once approved, implement the agreed scope and write the result to `docs/BUGFIX-REGISTRY.md`.

## Required bug-fix workflow

1. Search the registry and card-effect status documents.
2. Capture the pre-change local diff and identify overlapping files.
3. Reproduce or encode the bug in a failing test/check where practical.
4. Search the full S1/S2 catalog and all shared effect handlers for the same pattern.
5. Modify the shared framework, then migrate every matching card.
6. Run focused tests, full backend tests, frontend build/type checks, and catalog audits proportional to the change.
7. Append a registry entry containing root cause, same-type scan, files, verification, and rollback guard.

## Required Git completion workflow

Standing user authorization (2026-08-30): every completed change batch that passes its required verification and has no unresolved remote conflict is authorized to be committed and pushed to `origin/main` without requesting another per-batch push confirmation. This standing authorization does not include server deployment, production version switching, service restart, destructive remote operations, purchases, credentials, or other external systems; production deployment still requires explicit authorization for that batch.

1. After each independently completed feature or bug fix, run the required verification before touching remote history. Do not accumulate verified local fixes without publishing them.
2. Fetch the remote and compare the current branch with `origin/main` to detect collaborator commits.
3. If `origin/main` contains new commits, preserve the authoritative local worktree and integrate the remote commits safely. Never use a destructive reset or checkout. Stop for conflict review rather than choosing one side blindly.
4. If the branch is current, or after remote changes have been integrated and the full verification passes again, commit that feature or bug fix with a descriptive Chinese commit message and push it to the remote repository.
5. Record the commit and push result in the task handoff. Do not publish a knowingly failing or partially conflicted tree.

## Automatic task-complexity routing

These rules apply before any implementation plan or task action.

1. Before classification, only perform the minimum read-only inspection needed to understand scope and risk: read applicable `AGENTS.md` and `.codex` configuration, inspect the current branch and `git status`, and locate the directly relevant files or prior registry entries. Do not install dependencies, run broad builds, access external systems, or modify files before classification.
2. Route the task to exactly one project agent by default:
   - `l12_fast`: read-only lookup, copy, documentation, catalog work, or deterministic low-risk mechanical edits that do not change game semantics.
   - `l12_standard`: a known-root-cause local Bug or a bounded single-module UI, data, or card-effect change.
   - `l12_deep`: an unknown root cause, cross-module change, shared card-effect semantics, rule state machine, Prompt/Stack, combat, zone movement, hidden information, log, or replay behavior.
   - `l12_critical`: concurrency, WebSocket ordering/recovery, data consistency, privacy/security, authentication/authorization, database or replay integrity, production recovery, release, rollback, or deployment incident.
3. When a task matches more than one tier, select the highest matching tier. When classification is uncertain, route one tier higher.
4. An execution agent that discovers work above its assigned tier must stop modifying files, preserve the worktree, report the new risk evidence, and request rerouting. It must not silently expand scope.
5. The primary agent owns the implementation plan, final diff review, verification evidence, registry/task-ledger updates, and user handoff. Execution-agent output is never accepted without primary-agent verification.
6. If the runtime cannot dispatch a configured project-agent name, create one execution agent with the exact model and reasoning effort declared in `.codex/agents/` and pass it the corresponding developer instructions. Do not substitute a lower tier silently.
7. Multi-agent parallelism is reserved for independent read-only audits or disjoint files explicitly approved by the primary agent. Never let multiple agents edit the same rule, protocol, registry, generated data, or configuration file concurrently.
8. This repository has unusually long task history. Never fork the complete conversation into a child agent. Use `fork_turns="none"` or the smallest bounded recent-turn window and pass a compact written brief. A child must not receive raw prior tool output, card images, test logs, or repeated user history unless the subtask requires that exact evidence.
9. Treat `D:\GPT\Legion12\app` as the canonical physical checkout after the storage migration. `D:\GPT\Legion12\workspace` and older paths are compatibility junctions only. Generated output, dependency caches, archives, and session logs must stay under the governed D-drive directories documented in `docs/STORAGE-GOVERNANCE.md`.

## Change batches and validation tiers

1. Track every accepted change in `docs/TASK-LEDGER.md`; inserted requests are queued and do not replace an in-progress batch unless the user explicitly reprioritizes them.
2. Each batch contains one shared root cause, its full-pool same-type migration, regression guards, one review, and one synchronization decision. Do not mix unrelated fixes merely to reduce the number of commits.
3. Use `scripts/verify-l12-change.ps1` during development:
   - `Focused` while implementing;
   - `Batch` after the independent feature or Bug is complete;
   - `Release` only before an authorized synchronization or deployment.
4. Prefer deterministic engine scenarios or saved sanitized replay/state fixtures over manually recreating a board. Follow `docs/REGRESSION-FIXTURES.md` and never commit passwords, tokens, room secrets, private hands from real matches, or player identifiers.
5. Before final handoff, compare the final diff with the pre-change baseline and run the rollback-guard checks. Previously fixed UI contracts and rule invariants may not disappear from the same batch.

## Product test isolation

1. Legion12-only work must not run the unfiltered `GrandUMIServer.Tests` suite. That project contains another game's card-effect regressions and has a stable, separately recorded set of failures; rerunning it does not strengthen Legion12 evidence and wastes build time.
2. When Legion12 changes shared platform, account, administration, tournament, persistence, or control-plane code, run only `PlatformStoreTests|ControlPlane` from `GrandUMIServer.Tests`, as encoded by `scripts/verify-l12-change.ps1`.
3. Run the complete unfiltered GrandUMI suite only when the requested work explicitly concerns GrandUMI, or the diff changes GrandUMI gameplay/effect/runtime files outside the `TwelveLegions` product boundary. In that case existing failures are real failures to investigate, not an exclusion list to hide.
4. A primary or execution agent must not add an extra “informational” full GrandUMI run after the Legion12 gate has passed. Report the last separately recorded GrandUMI baseline by reference only when it materially affects the changed shared code.
5. `NU1900` caused solely by an unavailable NuGet vulnerability feed is recorded as an environment warning; compilation or test failures remain blocking.
