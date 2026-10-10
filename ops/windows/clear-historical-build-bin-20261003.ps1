[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference = 'Stop'
# Fixed, individually reviewed outputs only. Never remove their parent worktrees/cache records.
$targets = @(
  'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\b2-independent-20260930\bin',
  'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\tsukuyomi-independent-20260930\bin',
  'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\season-a3-3-final-r2\bin',
  'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\season-a3-3-final\bin',
  'C:\Users\neptu\Documents\ChatGPT\Legion12\.cache\season-a3-2-final\bin',
  'D:\GPT\Legion12\cache\tasks\prompt-b1-card1\bin',
  'D:\GPT\Legion12\cache\tasks\prompt-b1-card2\bin',
  'D:\GPT\Legion12\cache\tasks\prompt-b1-card3\bin',
  'D:\GPT\Legion12\cache\tasks\prompt-b1-card4\bin',
  'D:\GPT\Legion12\cache\tasks\prompt-b1-card5\bin'
)
$running = @(Get-Process | Where-Object { $_.ProcessName -match '^(dotnet|MSBuild|testhost|vstest.console)$' })
if ($running.Count) { throw 'Active build/test processes found. Refusing cache cleanup.' }
$validated = foreach ($target in $targets) {
  if (-not (Test-Path -LiteralPath $target)) { continue }
  $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
  if (-not $resolved.Equals($target, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected resolved target: $target" }
  $item = Get-Item -LiteralPath $resolved -Force
  if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked target refused: $target" }
  $queue = [Collections.Generic.Queue[string]]::new()
  $queue.Enqueue($resolved)
  $bytes = 0L
  while ($queue.Count) {
    foreach ($child in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
      if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked child refused: $($child.FullName)" }
      if ($child.Name -eq '.git') { throw "Git metadata refused: $target" }
      if ($child.PSIsContainer) { $queue.Enqueue($child.FullName); continue }
      if ($child.Extension -match '^\.(db|sqlite|journal|jsonl|trx|log|png|zip|gz|bak|patch)$' -or $child.Name -match '(\.db-|\.sqlite-)') {
        throw "Runtime/evidence file refused: $($child.FullName)"
      }
      if ($child.LastWriteTimeUtc -ge [datetime]'2026-10-01T00:00:00Z') { throw "Recent output refused: $($child.FullName)" }
      $bytes += $child.Length
    }
  }
  [pscustomobject]@{ Path = $resolved; LogicalBytes = $bytes }
}
Get-PSDrive C,D | Select-Object Name,Used,Free
foreach ($target in $validated) {
  if ($PSCmdlet.ShouldProcess($target.Path, 'Delete reviewed historical rebuildable bin output')) {
    Remove-Item -LiteralPath $target.Path -Recurse -Force
    $target
  }
}
Get-PSDrive C,D | Select-Object Name,Used,Free
