param([ValidateSet('empty-draw', 'target-decline', 'hidden-draw')] [string]$Scenario = 'empty-draw')

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$testOutput = Join-Path $repoRoot 'TwelveLegions.Tests/bin/Debug/net10.0'
[System.Reflection.Assembly]::LoadFrom((Join-Path $testOutput 'GrandUMIServer.dll')) | Out-Null
$catalog = [TwelveLegions.Server.L12Catalog]::Load((Join-Path $testOutput 'Data'))
$ctor = [TwelveLegions.Server.L12GameEngine].GetConstructors()[0]
$argsForGame = @($catalog, 'stage4a-integration', 'STAGE4A', 105311,
  [string[]]@('甲','乙'), [int[]]@(0,0), $true, 'random', $true, $false,
  $null, 2, $null, $null, $null, $null)
$game = $ctor.Invoke($argsForGame)
$game.State.ActivePlayer = 0
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
function Command($type, $card, $prompt, $choice, $selectedIds) {
  $commandCtor = [TwelveLegions.Server.L12Command].GetConstructors()[0]
  $values = foreach ($parameter in $commandCtor.GetParameters()) {
    switch ($parameter.Name) {
      'Type' { $type }
      'CardInstanceId' { $card }
      'PromptId' { $prompt }
      'Choice' { $choice }
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
