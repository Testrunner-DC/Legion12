# Legion12 project working rules

These rules apply to every change in this repository.

## Permanent execution principles (user update 2026-10-13)

- Every diagnostic or verification action must answer a concrete unresolved question or validate an affected risk. Do not repeat unchanged-version production sampling, whole-project scans or still-valid same-source checks merely for completeness. Old-version observations are not evidence that a local optimization worked; deploy the accepted candidate to the authorized isolated test environment and measure a real bounded workload. Necessary version, identity and availability checks for a cutover/shared-host safety guard are not performance-benefit claims.
- Deployment and Bug closure are fixed workflows, not repeatedly reinvented plans. Fix a recurring shared cause, encode its regression and update the existing procedure before the next attempt. Reuse batch-wide and standing authorization for ordinary in-scope steps; do not ask again for command parameters, receipt names, retries proven before cutover or the same approved calculation correction. Consolidate materially new trust, scope or destructive-data risks into one decision; never interpret this rule as permission to bypass safety review or remove integrity protection.
- Bug closure uses the existing dedicated queue workflow: original scenario/ruling, named evidence, exact deployed version and one preview/apply/readback sequence. User update 2026-10-17: old reports need current-version correctness, not mandatory historical replay or report-to-fix DAG reconstruction; reuse valid current evidence and close independently as fixed, rule-correct or checked-unreproduced with truthful scope, as specified in `docs/BUG-MANAGEMENT-WORKFLOW.md`. Do not reopen the same work without new evidence, repeatedly ask the user to remember a player's report, or equate a merely related topic with an exact duplicate.
- User update 2026-10-23: unresolved card-rule meaning, category membership or effect-order interactions must be asked of the human before choosing an expected test result, implementing a change or closing a report. Existing explicit rulings remain reusable; this does not require repeated permission for routine technical steps. God power (神力) itself counts as morale (士气) and may be affected by morale-related effects, subject to their other conditions. Never assume it must be excluded merely because it is god power. Hang only the ambiguous item and continue independent work.

## Generated-output placement (user reaffirmed 2026-10-16)

- Do not create project build, browser-profile, acceptance, capture, package or large temporary outputs physically on C. Source being on D does not prove the output location. Use governed D paths for active cache/evidence and E for long-term archives; resolve every ancestor junction before validating the actual drive.
- At every custom acceptance/probe entry, dot-source `ops/windows/L12GeneratedStorage.ps1` and call `Assert-L12GeneratedPath` on the selected output root before creating files. The build initializer applies the same guard to cache/dependency/evidence/archive roots and sets TEMP/TMP/TMPDIR. Reuse existing valid evidence instead of producing another copy.
- `Initialize-L12BuildEnvironment.ps1` changes dependency/temp environment, not MSBuild output locations. Custom `dotnet test` calls must use `Invoke-L12TestRun` and explicitly bind build outputs to a validated primary-cache root (the existing `--artifacts-path` pattern); a pinned existing assembly may instead use `--no-build --no-restore`. Do not assume a D checkout's default `bin/obj` is the governed active cache, or relabel a direct run as supervised after the fact.
- Delete proven-obsolete, regenerable build outputs, duplicate dependency caches and abandoned copies after their task lease ends; do not transfer them to E just to retain another large copy. Keep compact acceptance/failure records and one necessary active build. E is for genuinely valuable long-term data, not unlimited generated-output retention. Preserve unique business data, necessary rollback/recovery artifacts and unresolved private evidence.
- C's legacy workspace is a compatibility entry, not a new output root. Older source/evidence may remain behind preserved read-only references; never append generated data to a reference whose physical target is still C. Do not move live global Codex sessions, unknown/private evidence or runtime databases to satisfy this rule.

## Permanent visual baseline

- Unless the user explicitly requests a typography exception, all rendered interface text uses the shared Chinese sans-serif (黑体) font stack; distinguish hierarchy with font weight and existing size, not decorative, serif, or monospace families. Text baked into card images and logos is not restyled.
- Typography must be assigned by information role and usable space: card-effect prose, controls, metadata, and micro-labels may use separate responsive sizes and line heights. Do not restore a project-wide 14px minimum or mechanically wrap every declaration in one minimum-size token; validate legibility together with containment, wrapping, and the exact supported viewports.
- Select controls and their expanded options must follow the dark site palette, including dialogs, Teleport content, disabled states, and light operating-system themes. Never introduce a pure-white native dropdown. Preserve native keyboard interaction and visible focus.
- After typography/control changes, check narrow-screen containment, clipping, overlap and readability; keep the shared theme guard in the normal frontend verification chain.

1. Before fixing a bug, read `docs/BUGFIX-REGISTRY.md`, identify matching prior fixes, and inspect the current local diff. The local worktree is the source of truth; never reset, checkout, or overwrite it from a remote copy.
2. Fix shared causes before individual cards. For every card-related bug, scan the complete existing card pool for cards with the same timing, payment, targeting, zone, or presentation pattern. Record the scan query and affected cards.
3. Add a regression guard with every fix: a server test, frontend contract/build check, catalog invariant, or a combination. Review `git diff` after the change and do not replace unrelated local edits.
4. For reported Bugs, present the evidence and proposed fix and obtain explicit user approval before implementation. Existing approval remains valid; do not ask again for routine implementation choices within its scope. Directly requested non-Bug work may proceed within the user's authorization. Record Bug results in `docs/BUGFIX-REGISTRY.md`.

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

1. User update (2026-09-17): group related fixes into one bounded lifecycle-family batch and commit/push after that batch's verification. Do not create a separate commit or release run for every small resolver or documentation receipt. Update `docs/EFFECT-LIFECYCLE-ROADMAP.md` as progress changes; include code, regressions and records in the same batch. Preserve unrelated existing changes even when there are no active parallel tasks.
2. Fetch the remote and compare the current branch with `origin/main` to detect collaborator commits.
3. If `origin/main` contains new commits, preserve the authoritative local worktree and integrate the remote commits safely. Never use a destructive reset or checkout. Stop for conflict review rather than choosing one side blindly.
4. If the branch is current, or after remote changes have been integrated and the full verification passes again, commit that feature or bug fix with a descriptive Chinese commit message and push it to the remote repository.
5. Record the commit and push result in the task handoff. Do not publish a knowingly failing or partially conflicted tree.
6. Every production deployment must publish one player-facing update-log entry tied to the deployed commit/version in the existing left-sidebar `SiteShell.vue` “更新日志” modal. Do not create or publish a News/“资讯” article for a version update unless the user explicitly asks for that separate publication. Group the entry by affected area, name the cards and player-visible functions changed, and state the resulting behavior plainly; a vague summary such as “修复多项卡效” is not sufficient. Keep backend administration, moderation, audit, storage maintenance, internal diagnostics, and other non-player-facing implementation details out of the player log. A rollback must not advertise the failed version as deployed.
7. After production verification succeeds, close the linked backend Bug loop in the same release workflow. Mark a report `resolved` only when its original scenario has a named regression test and the deployed commit/version is verified; link duplicates to the primary report before closing them. Rejected requests and insufficient reports must record the reason. Leave unverified reports open or confirmed, and never bulk-close them merely because related code changed.

## Deployment authorization and compact execution (user update 2026-10-13)

- The single deployment procedure is `docs/DEPLOY-HK.md`, section “部署标准：四步完成”. Other guides link to it instead of adding another approval or verification chain.
- A human's explicit batch-wide deployment authorization covers ordinary in-scope preparation, tool corrections, bounded retries after confirmed pre-switch failure, and safe same-candidate/latest-data recovery. Reuse that authorization; do not ask again for each command, timeout, receipt, or count of classified historical records. Maintenance completion is included when the human requested it. Honor actual human deadlines; never turn a tool timeout into an invented authorization expiry.
- Finish the combined environment/package/tool/data preflight before the disruptive maintenance/cutover phase, classify all blockers, and consolidate any genuinely new decisions into one question. A passing mock or source review is not evidence that the real executable/permission/POSIX-path/re-entry chain works.
- Keep necessary boundary checks, but reuse still-valid same-source evidence and ask only about materially new scope, trust, destructive data handling or unexplained data risk. Do not weaken checks, fabricate stop permits, overwrite latest databases, or silently turn a one-time exception into permanent bypasses.
- Use one release-session summary referencing the standard logs/raw proofs; do not create separate approval tasks or duplicate checklists for every intermediate receipt. Once a collaborator has completed the deployment, reconcile their receipt and do not deploy or change maintenance again merely to finish Main's old plan.
- Documentation-only updates use proportional mapped/document validation. They do not trigger application Release, rebuilding an already accepted package, deployment, maintenance changes, online Bug closure or new project goals.

## Single-primary execution and optional delegation

The user approved this operating model on 2026-09-09 for immediate use, without a pilot. Detailed procedures live only in `docs/WORKSTREAM-COORDINATION.md`.

1. The primary agent implements work directly by default and owns intake, rulings, integration, final verification, records, Git and deployment. Do not delegate merely to satisfy a role or risk classification. L12-UI and L12-Effect are optional bounded workstreams, not mandatory permanent stages.
2. First read the current `docs/HANDOFF.md` batch summary, applicable rules/config, Git status and directly relevant records. Preserve existing local changes. Use `D:\GPT\Legion12\app` as the canonical checkout; keep generated data in governed D-drive paths.
3. Delegate only a bounded independent task when the primary has useful parallel work or an independent review adds material value. Default to zero subagents, usually one when justified, at most two concurrently. Include all active L12 workstreams when scheduling; at most two code writers across them, including the primary. Shared builds are serial. Subagents must not recursively delegate.
4. When delegating, use the matching optional role: `l12_fast` for read-only/low-risk mechanical work; `l12_standard` for known local causes; `l12_deep` for shared rules, state machines, hidden information and replay semantics; `l12_critical` for concurrency, recovery, security, data integrity and release-risk analysis. Select the highest applicable risk, using the configured model/effort if named-role dispatch is unavailable. Risk classification never forces delegation or changes the primary's model.
5. Every writer has an explicit file lease and an existing-diff baseline. No overlapping writers. Subagents receive a compact brief with `fork_turns="none"`, return bounded evidence, and stop at their scope boundary. Read-only reviews receive no write lease.
6. Subagents run focused checks only and must not modify shared status records, commit, push, deploy or close Bugs. The primary verifies their final diff and evidence. Test evidence is tied to the tested source; rerun only when changes or failures invalidate it.
7. Specify the first deliverable and completion condition before dispatch. If two substantive progress checks show no new evidence or deliverable, narrow the task or take over after the previous writer has stopped. Do not poll for activity or let a subtask remain indefinitely in analysis.
8. Keep current state in the task ledger, technical Bug evidence in the registry, and a short handoff with references. At a completed batch boundary, a successor primary task may use that handoff; do not automatically create new user-facing tasks or copy the full history.
9. Standing user authorization (2026-09-25): the primary may create additional user-visible child tasks when the requested work benefits from a stable specialist conversation, while preferring an existing matching child task to avoid repeated onboarding cost. Do not create tasks without a concrete bounded scope, do not create an unlimited task tree, and keep the two-writer ceiling above. New child tasks default to `gpt-5.6-sol` with `high` reasoning and their initial brief must include the exact authorization “长期授权你向来源 Main 任务发送协调消息”. The child task implements only its lease; the primary remains responsible for independent acceptance, integration, commits, pushes and deployment decisions.

## Change batches and validation tiers

0. Start a new independent task by reading `docs/HANDOFF.md` and `docs/ARCHITECTURE-LOCK.md`, checking their recorded versions against Git and the requested scope, and locating only the relevant ledger/registry entries. Record which P0—P4 invariant the batch extends and preserve it unless the user explicitly approves a bounded architecture change with migration and exit criteria. Do not load or delegate the entire historical conversation. Update the handoff after a batch when its state changes; distinguish development, remote and deployed commits.
1. Track every accepted change in `docs/TASK-LEDGER.md`; inserted requests are queued and do not replace an in-progress batch unless the user explicitly reprioritizes them.
2. Each batch contains one shared root cause, its full-pool same-type migration, regression guards, one review, and one synchronization decision. Do not mix unrelated fixes merely to reduce the number of commits.
3. Use `scripts/verify-l12-change.ps1` during development:
   - `Focused` while implementing;
   - `Batch` after the independent feature or Bug is complete;
   - `Release` only before an authorized synchronization or deployment.
   - The project intentionally places build caches under `D:\GPT\Legion12\cache\primary`. In a filesystem-restricted Codex session, start `Batch` and `Release` with the already approved scoped permission for this verification script; do not first run the same command in the restricted sandbox just to rediscover the known MSBuild temp-directory denial. Lightweight checks that do not write this cache may remain sandboxed.
4. Prefer deterministic engine scenarios or saved sanitized replay/state fixtures over manually recreating a board. Follow `docs/REGRESSION-FIXTURES.md` and never commit passwords, tokens, room secrets, private hands from real matches, or player identifiers.
5. Before final handoff, compare the final diff with the pre-change baseline and run the rollback-guard checks. Previously fixed UI contracts and rule invariants may not disappear from the same batch.

## Product test isolation

1. Legion12 rules and gameplay regressions live in `TwelveLegions.Tests`; platform, account, administration, tournament, persistence and control-plane regressions live in `TwelveLegions.Platform.Tests`.
2. The retired GrandUMI gameplay/effect/runtime suite is not a Legion12 verification dependency and must not be recreated or added to the release gate.
3. Run the platform project in full when shared platform or control-plane code changes; do not use name filters to hide failures inside that dedicated project.
4. `NU1900` caused solely by an unavailable NuGet vulnerability feed is recorded as an environment warning; compilation or test failures remain blocking.
