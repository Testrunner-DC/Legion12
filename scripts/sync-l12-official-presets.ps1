$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$serverData = Get-ChildItem -LiteralPath $projectRoot -Directory |
  ForEach-Object { Join-Path $_.FullName 'TwelveLegions/Data' } |
  Where-Object { Test-Path -LiteralPath (Join-Path $_ 'cards.s1.json') } |
  Select-Object -First 1
if (-not $serverData) { throw 'TwelveLegions server data directory was not found.' }
$webData = Join-Path $projectRoot 'opcgpro-vue/public/data/l12'

$existingS1 = Get-Content -LiteralPath (Join-Path $serverData 'preset-decks.s1.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$asgard = [ordered]@{
  name = ('"\u963f\u65af\u52a0\u5fb7\u9884\u7ec4 S1"' | ConvertFrom-Json)
  masterId = 'S01-03M2'
  cardIds = @(
    'S01-0316','S01-0316','S01-0316','S01-0312','S01-0312','S01-0312','S01-0311','S01-0311',
    'S01-0307','S01-0307','S01-0307','S01-0002','S01-0002','S01-0002','S01-0306','S01-0306','S01-0306',
    'S01-0308','S01-0308','S01-0308','S01-0305','S01-0305','S01-0305','S01-0303','S01-0303','S01-0303',
    'S01-0001','S01-0001','S01-0001','S01-0015','S01-0015','S01-0015','S01-0318','S01-0318','S01-0318',
    'S01-0319','S01-0319','S01-0319','S01-0016','S01-0016'
  )
  moraleIds = @('S01-03C1','S01-03C1','S01-03C1','S01-03C1','S01-03C1','S01-03C1','S01-03C1','S01-03C1')
  specialIds = @()
}

$sunCity = [ordered]@{
  name = ('"\u592a\u9633\u57ce\u9884\u7ec4 S1"' | ConvertFrom-Json)
  masterId = 'S01-02M3'
  cardIds = @(
    'S01-0212','S01-0212','S01-0212','S01-0214','S01-0214','S01-0214','S01-0210','S01-0210','S01-0210',
    'S01-0209','S01-0209','S01-0209','S01-0207','S01-0207','S01-0205','S01-0205','S01-0205','S01-0204','S01-0204','S01-0204',
    'S01-0201','S01-0201','S01-0201','S01-0216','S01-0217','S01-0218','S01-0219','S01-0220','S01-0215','S01-0215','S01-0215',
    'S01-0222','S01-0222','S01-0222','S01-0224','S01-0221','S01-0221','S01-0221','S01-0002','S01-0002','S01-0002','S01-0016','S01-0016'
  )
  moraleIds = @('S01-02C1','S01-02C1','S01-02C1','S01-02C1','S01-02C1','S01-02C1')
  specialIds = @()
}

$olympus = [ordered]@{
  name = ('"\u5965\u6797\u5339\u65af\u9884\u7ec4 S2"' | ConvertFrom-Json)
  masterId = 'S02-05M2'
  cardIds = @(
    'S02-0005','S02-0517','S02-0517','S02-0517','S02-0003','S02-0003','S02-0501','S02-0501','S02-0501',
    'S02-0502','S02-0502','S02-0502','S02-0503','S02-0503','S02-0503','S02-0504','S02-0504','S02-0504',
    'S02-0505','S02-0505','S02-0505','S02-0506','S02-0506','S02-0506','S02-0507','S02-0507','S02-0507',
    'S02-0511','S02-0511','S02-0511','S01-0002','S01-0002','S01-0002','S02-0520','S02-0520','S02-0522','S02-0522','S02-0522','S02-0521','S02-0521'
  )
  moraleIds = @('S02-05C1','S02-05C1','S02-05C1','S02-05C1','S02-05C1','S02-05C1','S02-05C1','S02-05C1')
  specialIds = @()
}

$otherworld = [ordered]@{
  name = ('"\u5f7c\u754c\u9884\u7ec4 S2"' | ConvertFrom-Json)
  masterId = 'S02-06M1'
  cardIds = @(
    'S02-0609','S02-0609','S02-0609','S02-0618','S02-0618','S02-0617','S02-0617','S02-0617','S02-0604','S02-0604','S02-0604',
    'S02-0003','S02-0003','S02-0610','S02-0610','S01-0002','S01-0002','S01-0002','S02-0606','S02-0606','S02-0606',
    'S02-0603','S02-0603','S02-0603','S02-0605','S02-0605','S02-0605','S02-0607','S02-0607','S02-0608','S02-0608','S02-0608',
    'S02-0602','S02-0602','S02-0602','S02-0620','S02-0620','S02-0620','S02-0621','S02-0622','S02-0622','S02-0622'
  )
  moraleIds = @('S02-06C1','S02-06C1','S02-06C1','S02-06C1','S02-06C1','S02-06C1','S02-06C1','S02-06C1')
  specialIds = @('S02-06S4')
}

$s1 = @($existingS1[0], $existingS1[1], $sunCity, $asgard)
$s2 = @($olympus, $otherworld)

function Assert-Preset([object]$preset) {
  $countedMain = @($preset.cardIds | Where-Object { $_ -ne 'S01-0212' }).Count
  $requiredMorale = if ($preset.masterId -eq 'S01-02M3') { 6 } else { 8 }
  if ($countedMain -lt 40 -or $countedMain -gt 50) { throw "$($preset.name): counted main deck is $countedMain, expected 40-50" }
  if ($preset.moraleIds.Count -ne $requiredMorale) { throw "$($preset.name): invalid morale count" }
}

@($s1 + $s2) | ForEach-Object { Assert-Preset $_ }
$jsonS1 = $s1 | ConvertTo-Json -Depth 6
$jsonS2 = $s2 | ConvertTo-Json -Depth 6

Set-Content -LiteralPath (Join-Path $serverData 'preset-decks.s1.json') -Value $jsonS1 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $serverData 'preset-decks.s2.json') -Value $jsonS2 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $webData 'preset-decks.s1.json') -Value $jsonS1 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $webData 'preset-decks.s2.json') -Value $jsonS2 -Encoding UTF8

Write-Host 'Synchronized 6 official preset decks.'
