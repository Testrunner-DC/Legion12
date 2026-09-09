# Legion12 project working rules

These rules apply to every change in this repository.

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

1. After each independently completed feature or bug fix, run the required verification before touching remote history. Do not accumulate verified local fixes without publishing them.
2. Fetch the remote and compare the current branch with `origin/main` to detect collaborator commits.
3. If `origin/main` contains new commits, preserve the authoritative local worktree and integrate the remote commits safely. Never use a destructive reset or checkout. Stop for conflict review rather than choosing one side blindly.
4. If the branch is current, or after remote changes have been integrated and the full verification passes again, commit that feature or bug fix with a descriptive Chinese commit message and push it to the remote repository.
5. Record the commit and push result in the task handoff. Do not publish a knowingly failing or partially conflicted tree.
6. Every production deployment must publish one player-facing update-log entry tied to the deployed commit/version. Group the entry by affected area, name the cards and functions changed, and state the resulting behavior plainly; a vague summary such as “修复多项卡效” is not sufficient. Keep internal diagnostics out of the player log. A rollback must not advertise the failed version as deployed.
7. After production verification succeeds, close the linked backend Bug loop in the same release workflow. Mark a report `resolved` only when its original scenario has a named regression test and the deployed commit/version is verified; link duplicates to the primary report before closing them. Rejected requests and insufficient reports must record the reason. Leave unverified reports open or confirmed, and never bulk-close them merely because related code changed.

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

## Change batches and validation tiers

0. Start a new independent task by reading `docs/HANDOFF.md`, checking its recorded versions against Git and the requested scope, and locating only the relevant ledger/registry entries. Do not load or delegate the entire historical conversation. Update the handoff after a batch when its state changes; distinguish development, remote and deployed commits.
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
