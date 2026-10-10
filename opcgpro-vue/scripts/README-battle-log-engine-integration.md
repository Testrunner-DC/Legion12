# Battle log real-engine integration check

After the backend Batch or Release build, run `npm run test:battle-log-engine` from `opcgpro-vue`. The test rebuilds `TwelveLegions.Tests.csproj` with `--no-restore --no-incremental` in **Release** by default, resolves that project's target framework, and passes the exact output directory and expected Git commit to the PowerShell probe before projecting the emitted events through the frontend log model. The probe rejects an assembly whose informational version does not match that commit, so an old Debug or Release binary cannot silently pass.

To test Debug explicitly, set `L12_BATTLE_LOG_CONFIGURATION=Debug` for this command. Restore NuGet dependencies first if the isolated checkout has no assets file. The probe can also be run directly with `-Configuration Debug|Release`, optional `-AssemblyDirectory`, and optional `-ExpectedCommit` when inspecting one scenario; direct probe use assumes the matching assembly has already been built.

This is a standalone cross-runtime check, separate from `check:ui-contracts` and the Release frontend gate so static UI checks do not require .NET or PowerShell. It covers real 〈乾坤·阳〉 two-segment results, paid morale, an actually processed public target, three viewer projections, replay parsing, reconnect duplicates, and a hidden-target negative case.
