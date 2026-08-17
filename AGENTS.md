# Legion12 project working rules

These rules apply to every change in this repository.

1. Before fixing a bug, read `docs/BUGFIX-REGISTRY.md`, identify matching prior fixes, and inspect the current local diff. The local worktree is the source of truth; never pull, reset, checkout, or overwrite it from a remote copy.
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

