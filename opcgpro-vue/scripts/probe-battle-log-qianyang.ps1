param(
  [ValidateSet('empty-draw', 'target-decline', 'hidden-draw', 'prometheus-empty', 'yin-empty', 'volley-empty', 'volley-positive')] [string]$Scenario = 'empty-draw',
  [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
  [string]$AssemblyDirectory,
  [string]$ExpectedCommit
)

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$testProject = Join-Path $repoRoot 'TwelveLegions.Tests/TwelveLegions.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($AssemblyDirectory)) {
  $targetFramework = (& dotnet msbuild $testProject '-getProperty:TargetFramework' "-p:Configuration=$Configuration").Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "Unable to resolve TargetFramework for $Configuration"
  }
  $testOutput = Join-Path $repoRoot "TwelveLegions.Tests/bin/$Configuration/$targetFramework"
} else {
  $testOutput = (Resolve-Path -LiteralPath $AssemblyDirectory).Path
}
$assemblyPath = Join-Path $testOutput 'GrandUMIServer.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
  throw "Missing $Configuration engine assembly: $assemblyPath. Build the test project first."
}
$engineAssembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
  $version = $engineAssembly.GetCustomAttributes(
    [System.Reflection.AssemblyInformationalVersionAttribute], $false).InformationalVersion
  if (-not $version.EndsWith("+$ExpectedCommit", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Engine assembly commit mismatch: expected $ExpectedCommit, found $version"
  }
}
$catalog = [TwelveLegions.Server.L12Catalog]::Load((Join-Path $testOutput 'Data'))
$ctor = [TwelveLegions.Server.L12GameEngine].GetConstructors()[0]
$deckIndex = if ($Scenario -eq 'prometheus-empty') { 3 } else { 0 }
$argsForGame = @($catalog, 'stage4a-integration', 'STAGE4A', 105311,
  [string[]]@('甲','乙'), [int[]]@($deckIndex,$deckIndex), $true, 'random', $true, $false,
  $null, 2, $null, $null, $null, $null)
$game = $ctor.Invoke($argsForGame)
$game.State.ActivePlayer = if ($Scenario -eq 'yin-empty') { 1 } else { 0 }
$game.State.FirstPlayer = 0
$game.State.Round = 2
$game.State.TurnSerial = 3
$game.State.Phase = [TwelveLegions.Server.L12Phase]::Main
foreach ($player in $game.State.Players) {
  $player.Field[0] = [TwelveLegions.Server.L12CardInstance[]]::new(3)
  $player.Field[1] = [TwelveLegions.Server.L12CardInstance[]]::new(3)
  $player.Hand.Clear(); $player.Graveyard.Clear(); $player.Morale.Clear(); $player.Resolving.Clear()
}
$createCard = [TwelveLegions.Server.L12GameEngine].GetMethod('CreateCard',
  [System.Reflection.BindingFlags]'NonPublic,Instance')
if ($Scenario -in @('empty-draw', 'target-decline', 'hidden-draw')) {
  $source = $createCard.Invoke($game, @('S02-0105', "stage4a-qianyang-$Scenario"))
  $game.State.Players[0].Hand.Add($source)
  if ($Scenario -eq 'target-decline') {
    $target = $createCard.Invoke($game, @('S02-0003', 'stage4a-public-target'))
    $game.State.Players[1].Field[0][0] = $target
  }
  if ($Scenario -eq 'hidden-draw') {
    $target = $createCard.Invoke($game, @('S02-0003', 'stage4a-private-target'))
    $target.Hidden = $true
    $game.State.Players[1].Field[1][0] = $target
  }
  while ($game.State.Players[0].Morale.Count -lt 3) {
    $morale = $game.State.Players[0].MoraleDeck[0]
    $game.State.Players[0].MoraleDeck.RemoveAt(0)
    $morale.Tapped = $false
    $game.State.Players[0].Morale.Add($morale)
  }
}
function Command($type, $card, $prompt, $choice, $selectedIds, $ability) {
  $commandCtor = [TwelveLegions.Server.L12Command].GetConstructors()[0]
  $values = foreach ($parameter in $commandCtor.GetParameters()) {
    switch ($parameter.Name) {
      'Type' { $type }
      'CardInstanceId' { $card }
      'PromptId' { $prompt }
      'Choice' { $choice }
      'Ability' { $ability }
      'CardInstanceIds' {
        if ($null -eq $selectedIds) { $null }
        else {
          $ids = [System.Collections.Generic.List[string]]::new()
          foreach ($id in $selectedIds) { $ids.Add($id) }
          ,$ids
        }
      }
      default { $parameter.DefaultValue }
    }
  }
  return $commandCtor.Invoke([object[]]$values)
}
function Accept($command, $actor) {
  $result = $game.Handle($actor, $command)
  if (-not $result.Accepted) { throw $result.Error }
}
if ($Scenario -eq 'prometheus-empty') {
  $source = $createCard.Invoke($game, @('S02-05M2', 'stage4a-prometheus-empty'))
  $game.State.Players[0].Field[0][0] = $source
  $game.State.Players[0].Library.Clear()
  $power = [TwelveLegions.Server.L12MoraleCard]::new()
  $power.InstanceId = 'stage4a-prometheus-power'
  $power.CardId = 'S02-05C1'
  $power.IsGodPower = $true
  $game.State.Players[0].Morale.Add($power)
  Accept (Command 'activateAbility' $source.InstanceId $null $null $null 'prometheusTopThree') 0
  while ($game.State.PendingPrompts.Count -gt 0 -and $game.State.PendingPrompts[0].Kind -eq 'response') {
    $prompt = $game.State.PendingPrompts[0]
    Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass' $null) $prompt.PlayerIndex
  }
} elseif ($Scenario -eq 'yin-empty') {
  $source = $createCard.Invoke($game, @('S02-0106', 'stage4a-yin-empty'))
  $source.Hidden = $true
  $source.SetRound = 2
  $game.State.Players[0].Field[1][0] = $source
  $game.State.Players[0].Library.Clear()
  $tactic = $createCard.Invoke($game, @('S01-0219', 'stage4a-yin-base'))
  $game.State.Players[1].Hand.Add($tactic)
  for ($index = 0; $index -lt $tactic.CurrentCost; $index++) {
    $morale = [TwelveLegions.Server.L12MoraleCard]::new()
    $morale.CardId = 'S01-02C1'
    $morale.InstanceId = "stage4a-yin-morale-$index"
    $game.State.Players[1].Morale.Add($morale)
  }
  Accept (Command 'playCard' $tactic.InstanceId $null $null $null) 1
  $prompt = $game.State.PendingPrompts[0]
  Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass' $null) $prompt.PlayerIndex
  $prompt = $game.State.PendingPrompts[0]
  Accept (Command 'resolvePrompt' $null $prompt.PromptId $source.InstanceId $null) $prompt.PlayerIndex
  while ($game.State.PendingPrompts.Count -gt 0 -and $game.State.PendingPrompts[0].Kind -eq 'response') {
    $prompt = $game.State.PendingPrompts[0]
    Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass' $null) $prompt.PlayerIndex
  }
} elseif ($Scenario -in @('volley-empty', 'volley-positive')) {
  $source = $createCard.Invoke($game, @('S01-0005', "stage4a-$Scenario"))
  $game.State.Players[0].FreeTacticCount = 1
  $game.State.Players[0].Hand.Add($source)
  if ($Scenario -eq 'volley-positive') {
    $target = $createCard.Invoke($game, @('S01-0103', 'stage4a-volley-public-target'))
    $game.State.Players[1].Field[0][0] = $target
  }
  Accept (Command 'playCard' $source.InstanceId $null $null $null) 0
  $prompt = $game.State.PendingPrompts[0]
  Accept (Command 'resolvePrompt' $null $prompt.PromptId 'mode:front' $null) $prompt.PlayerIndex
  while ($game.State.PendingPrompts.Count -gt 0 -and $game.State.PendingPrompts[0].Kind -eq 'response') {
    $prompt = $game.State.PendingPrompts[0]
    Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass' $null) $prompt.PlayerIndex
  }
}
if ($Scenario -in @('prometheus-empty', 'yin-empty', 'volley-empty', 'volley-positive')) {
  $options = [System.Text.Json.JsonSerializerOptions]::new()
  $options.PropertyNamingPolicy = [System.Text.Json.JsonNamingPolicy]::CamelCase
  [System.Text.Json.JsonSerializer]::Serialize(@{
    scenario = $Scenario
    owner = $game.SnapshotFor(0).RecentEvents
    opponent = $game.SnapshotFor(1).RecentEvents
    spectator = $game.SnapshotForSpectator().RecentEvents
  }, $options)
  exit
}
Accept (Command 'playCard' $source.InstanceId $null $null $null) 0
if ($Scenario -eq 'target-decline') {
  $prompt = $game.State.PendingPrompts[0]
  Accept (Command 'resolvePrompt' $null $prompt.PromptId $null @($target.InstanceId)) $prompt.PlayerIndex
}
$prompt = $game.State.PendingPrompts[0]
Accept (Command 'resolvePrompt' $null $prompt.PromptId $(if ($Scenario -ne 'target-decline') { 'mode:draw' } else { 'mode:none' }) $null) $prompt.PlayerIndex
if ($Scenario -ne 'target-decline') {
  $prompt = $game.State.PendingPrompts[0]
  Accept (Command 'resolvePrompt' $null $prompt.PromptId $prompt.ValidChoices[0] $null) $prompt.PlayerIndex
}
$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.PropertyNamingPolicy = [System.Text.Json.JsonNamingPolicy]::CamelCase
[System.Text.Json.JsonSerializer]::Serialize(@{
  scenario = $Scenario
  owner = $game.SnapshotFor(0).RecentEvents
  opponent = $game.SnapshotFor(1).RecentEvents
  spectator = $game.SnapshotForSpectator().RecentEvents
  moraleAfter = $game.State.Players[0].Morale.Count
}, $options)
