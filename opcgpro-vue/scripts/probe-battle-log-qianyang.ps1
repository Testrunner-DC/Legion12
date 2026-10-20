param(
  [ValidateSet('empty-draw', 'target-decline', 'hidden-draw', 'prometheus-empty', 'yin-empty', 'volley-empty', 'volley-positive', 'mengpo-selected-targets', 'oiran-log', 'court-move')] [string]$Scenario = 'empty-draw',
  [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
  [string]$AssemblyDirectory,
  [string]$ExpectedCommit,
  [ValidateSet(0, 1)] [int]$ActorIndex = 0,
  [switch]$Restore,
  [string]$ExpectedAssemblySha256
)

$ErrorActionPreference = 'Stop'
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
if ($Scenario -eq 'court-move' -and ((Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash -cne $ExpectedAssemblySha256 -or $ExpectedAssemblySha256 -cne 'A8756BC87B9E0238BAC64C41F94050EB5520B45789651F7B160E3A19592B24DA')) {
  throw 'COURT_MOVE_PINNED_ENGINE_REQUIRED'
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
# This scenario explicitly drives both response decisions; do not auto-pass them.
if ($Scenario -eq 'yin-empty') { $argsForGame[8] = $false }
if ($Scenario -eq 'court-move') { $argsForGame[7] = 'none'; $argsForGame[8] = $false }
$game = $ctor.Invoke($argsForGame)
if ($Scenario -eq 'mengpo-selected-targets') {
  $baseDeck = $catalog.DeckAt(0)
  $mengPoDeck = [TwelveLegions.Server.L12PresetDeckDefinition]::new()
  $mengPoDeck.Name = '孟婆目标合成界面回归'
  $mengPoDeck.MasterId = 'S01-01M2'
  $mengPoDeck.CardIds = $baseDeck.CardIds
  $mengPoDeck.MoraleIds = $baseDeck.MoraleIds
  $mengPoDeck.SpecialIds = [System.Collections.Generic.List[string]]::new()
  $decks = [TwelveLegions.Server.L12PresetDeckDefinition[]]@($baseDeck, $baseDeck)
  $decks[$ActorIndex] = $mengPoDeck
  $presetCtor = [TwelveLegions.Server.L12GameEngine].GetConstructors() |
    Where-Object { $_.GetParameters()[5].ParameterType -eq $decks.GetType() }
  $argsForGame[5] = $decks
  $argsForGame[8] = $false
  $game = $presetCtor.Invoke($argsForGame)
}
$game.State.ActivePlayer = if ($Scenario -eq 'yin-empty') { 1 } else { 0 }
if ($Scenario -in @('mengpo-selected-targets', 'oiran-log')) { $game.State.ActivePlayer = $ActorIndex }
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
function Command($type, $card, $prompt, $choice, $selectedIds, $ability, $bottomIds) {
  $commandCtor = [TwelveLegions.Server.L12Command].GetConstructors()[0]
  $values = foreach ($parameter in $commandCtor.GetParameters()) {
    switch ($parameter.Name) {
      'Type' { $type }
      'CardInstanceId' { $card }
      'PromptId' { $prompt }
      'Choice' { $choice }
      'Ability' { $ability }
      'TopCardInstanceIds' { if ($null -eq $bottomIds) { $null } else { ,[System.Collections.Generic.List[string]]::new() } }
      'BottomCardInstanceIds' {
        if ($null -eq $bottomIds) { $null } else {
          $bottom = [System.Collections.Generic.List[string]]::new()
          foreach ($id in $bottomIds) { $bottom.Add($id) }
          ,$bottom
        }
      }
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
if ($Scenario -eq 'court-move') {
  # Synthetic board copied from the already accepted authority regression. Only
  # subsequent real Handle commands produce payment and completed snapshots.
  $game.State.ActivePlayer = $ActorIndex; $game.State.FirstPlayer = $ActorIndex
  $game.State.Round = 3; $game.State.TurnSerial = 5
  foreach ($participant in $game.State.Players) {
    $participant.TemporaryMorale = 0; $participant.Relic = $null; $participant.ExtraRelics.Clear()
  }
  $owner = $game.State.Players[$ActorIndex]; $other = 1 - $ActorIndex
  $lubu = $createCard.Invoke($game, @('S01-0101', 'reported-lu-bu')); $lubu.OwnerIndex = $ActorIndex
  $magician = $createCard.Invoke($game, @('S02-0003', 'reported-court-magician')); $magician.OwnerIndex = $ActorIndex
  $owner.Field[1][0] = $lubu; $owner.Field[1][1] = $magician
  $first = [TwelveLegions.Server.L12MoraleCard]::new(); $first.InstanceId = 'reported-move-payment-a'; $first.CardId = 'S01-01C1'
  $second = [TwelveLegions.Server.L12MoraleCard]::new(); $second.InstanceId = 'reported-move-payment-b'; $second.CardId = 'S01-01C1'; $second.IsGodPower = $true
  $owner.Morale.Add($first); $owner.Morale.Add($second)
  $miyamoto = $createCard.Invoke($game, @('S01-0405', 'reported-miyamoto')); $miyamoto.OwnerIndex = $other
  $kusanagi = $createCard.Invoke($game, @('S01-0417', 'reported-kusanagi')); $kusanagi.OwnerIndex = $other
  $game.State.Players[$other].Field[0][0] = $miyamoto; $game.State.Players[$other].Relic = $kusanagi
  $options = [System.Text.Json.JsonSerializerOptions]::new(); $options.PropertyNamingPolicy = [System.Text.Json.JsonNamingPolicy]::CamelCase; $options.MaxDepth = 128
  # Serialize each projection immediately, before advancing the engine.
  $initialJson = [System.Text.Json.JsonSerializer]::Serialize($game.SnapshotFor($ActorIndex), $options)
  $moveCtor = [TwelveLegions.Server.L12Command].GetConstructors()[0]
  $moveArgs = foreach ($parameter in $moveCtor.GetParameters()) {
    switch ($parameter.Name) {
      'Type' { 'move' }; 'CardInstanceId' { 'reported-court-magician' }
      'Row' { 0 }; 'Slot' { 1 }; default { $parameter.DefaultValue }
    }
  }
  Accept ($moveCtor.Invoke([object[]]$moveArgs)) $ActorIndex
  $prompt = $game.State.PendingPrompts[0]
  if ($game.State.PendingPrompts.Count -ne 1 -or $prompt.Kind -ne 'resource-payment' -or $prompt.Continuation -ne 'move-morale-choice') { throw 'COURT_MOVE_REAL_PAYMENT_REQUIRED' }
  if ($Restore) {
    $flags = [System.Reflection.BindingFlags]'NonPublic,Instance'
    $randomState = [TwelveLegions.Server.L12GameEngine].GetProperty('RandomState', $flags).GetValue($game)
    $signalSequence = [TwelveLegions.Server.L12GameEngine].GetProperty('CardFactSignalSequence', $flags).GetValue($game)
    if ($null -eq $randomState) { throw 'COURT_MOVE_REAL_V2_RANDOM_STATE_REQUIRED' }
    $restoreMethod = [TwelveLegions.Server.L12GameEngine].GetMethod('RestoreCheckpoint', [System.Reflection.BindingFlags]'NonPublic,Static')
    $game = $restoreMethod.Invoke($null, @($catalog, $game.SerializeFullState(), $randomState, $signalSequence, $false, $false, $null))
    $prompt = $game.State.PendingPrompts[0]
  }
  $paymentJson = [System.Text.Json.JsonSerializer]::Serialize($game.SnapshotFor($ActorIndex), $options)
  Accept (Command 'resolvePrompt' $null $prompt.PromptId $null @('reported-move-payment-a')) $ActorIndex
  $completedJson = [System.Text.Json.JsonSerializer]::Serialize($game.SnapshotFor($ActorIndex), $options)
  if ($game.State.Players[$ActorIndex].Field[0][1].InstanceId -cne 'reported-court-magician' -or $null -ne $game.State.Players[$ActorIndex].Field[1][1] -or !$game.State.Players[$ActorIndex].Morale[0].Tapped -or $game.State.PendingPrompts.Count -ne 0) { throw 'COURT_MOVE_REAL_SETTLEMENT_REQUIRED' }
  [System.Text.Json.JsonSerializer]::Serialize(@{
    scenario = $Scenario; actor = $ActorIndex; restored = [bool]$Restore
    engineSha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash; commit = $ExpectedCommit
    initial = [System.Text.Json.JsonDocument]::Parse($initialJson).RootElement.Clone()
    payment = [System.Text.Json.JsonDocument]::Parse($paymentJson).RootElement.Clone()
    completed = [System.Text.Json.JsonDocument]::Parse($completedJson).RootElement.Clone()
    expected = @{ sourceId = 'reported-court-magician'; paymentId = 'reported-move-payment-a'; row = 0; slot = 1; legalSlots = @('0:1', '1:2') }
  }, $options)
  exit
}
if ($Scenario -in @('mengpo-selected-targets', 'oiran-log')) {
  $owner = $game.State.Players[$ActorIndex]
  $other = 1 - $ActorIndex
  if ($Scenario -eq 'mengpo-selected-targets') {
    $owner.MasterTapped = $false
    $morale = [TwelveLegions.Server.L12MoraleCard]::new()
    $morale.CardId = 'S01-01C1'; $morale.InstanceId = 'mengpo-payment'
    $owner.Morale.Add($morale)
    $target = $createCard.Invoke($game, @('S01-0204', 'mengpo-target'))
    $nonTarget = $createCard.Invoke($game, @('S01-0004', 'mengpo-non-target'))
    $target.OwnerIndex = $other; $nonTarget.OwnerIndex = $other
    $game.State.Players[$other].Field[0][0] = $target
    $game.State.Players[$other].Field[1][2] = $nonTarget
    Accept (Command 'activateAbility' "master-$ActorIndex" $null $null $null 'mengpoSilence') $ActorIndex
    $prompt = $game.State.PendingPrompts[0]
    Accept (Command 'resolvePrompt' $null $prompt.PromptId $target.InstanceId) $ActorIndex
    for ($step = 0; $step -lt 4 -and $game.State.PendingPrompts[0].Kind -ne 'response'; $step++) {
      $prompt = $game.State.PendingPrompts[0]
      Accept (Command 'resolvePrompt' $null $prompt.PromptId $morale.InstanceId) $ActorIndex
    }
    if ($game.State.PendingPrompts[0].Kind -ne 'response') { throw 'Missing MengPo response barrier' }
    $expected = @{ targetId = $target.InstanceId; targetName = $target.Name; nonTargetId = $nonTarget.InstanceId }
  } else {
    $gift = $createCard.Invoke($game, @('S01-0419', 'oiran-gift'))
    $gift.OwnerIndex = $ActorIndex; $owner.Hand.Add($gift)
    while ($owner.MoraleDeck.Count -gt 0) {
      $morale = $owner.MoraleDeck[0]; $owner.MoraleDeck.RemoveAt(0)
      $morale.Tapped = $false; $owner.Morale.Add($morale)
    }
    $owner.Library.Clear()
    foreach ($id in @('S01-0401', 'S01-0402', 'S01-0403')) {
      $candidate = $createCard.Invoke($game, @($id, "oiran-$id"))
      $candidate.OwnerIndex = $ActorIndex; $owner.Library.Add($candidate)
    }
    $chosen = $owner.Library[0]
    Accept (Command 'playCard' $gift.InstanceId) $ActorIndex
    for ($step = 0; $step -lt 12 -and $game.State.PendingPrompts[0].Kind -eq 'response'; $step++) {
      $prompt = $game.State.PendingPrompts[0]
      Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass') $prompt.PlayerIndex
    }
    $prompt = $game.State.PendingPrompts[0]
    if ($prompt.Data['choiceMode'] -ne 'required-add') { throw 'Missing Oiran private choice' }
    Accept (Command 'resolvePrompt' $null $prompt.PromptId $chosen.InstanceId) $ActorIndex
    $prompt = $game.State.PendingPrompts[0]
    Accept (Command 'resolvePrompt' $null $prompt.PromptId $null $null $null @($prompt.ValidChoices)) $ActorIndex
    for ($step = 0; $step -lt 12 -and $game.State.PendingPrompts.Count -gt 0 -and $game.State.PendingPrompts[0].Kind -eq 'response'; $step++) {
      $prompt = $game.State.PendingPrompts[0]
      Accept (Command 'resolvePrompt' $null $prompt.PromptId 'pass') $prompt.PlayerIndex
    }
    $prompt = $game.State.PendingPrompts[0]
    if ($null -eq $prompt -or -not $prompt.ValidChoices.Contains('mode:none')) { throw 'Missing Oiran optional ready choice' }
    Accept (Command 'resolvePrompt' $null $prompt.PromptId 'mode:none') $ActorIndex
    $expected = @{ cardId = $chosen.InstanceId; cardName = $chosen.Name }
  }
  if ($Restore) {
    $restoreMethod = [TwelveLegions.Server.L12GameEngine].GetMethod('RestoreCheckpoint',
      [System.Reflection.BindingFlags]'NonPublic,Static')
    $flags = [System.Reflection.BindingFlags]'NonPublic,Instance'
    $randomState = [TwelveLegions.Server.L12GameEngine].GetProperty('RandomState', $flags).GetValue($game)
    $signalSequence = [TwelveLegions.Server.L12GameEngine].GetProperty('CardFactSignalSequence', $flags).GetValue($game)
    if ($null -eq $randomState) { throw 'A real V2 deterministic random state is required' }
    $game = $restoreMethod.Invoke($null, @($catalog, $game.SerializeFullState(),
      $randomState, $signalSequence, $false, $false, $null))
  }
  $options = [System.Text.Json.JsonSerializerOptions]::new()
  $options.PropertyNamingPolicy = [System.Text.Json.JsonNamingPolicy]::CamelCase
  $options.MaxDepth = 128
  [System.Text.Json.JsonSerializer]::Serialize(@{
    scenario = $Scenario; actor = $ActorIndex; restored = [bool]$Restore; expected = $expected
    engineSha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    commit = $ExpectedCommit
    views = @{
      owner = $game.SnapshotFor($ActorIndex); opponent = $game.SnapshotFor($other)
      spectator = $game.SnapshotForSpectator(); referee = $game.SnapshotForReferee()
    }
  }, $options)
  exit
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
