function Get-L12CardRuntimeEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Cards
    )

    $sourcePath = Join-Path $ProjectRoot '服务端WebSocket/TwelveLegions'
    $testPath = Join-Path $ProjectRoot 'TwelveLegions.Tests'
    $excluded = @(
        'AtomicEffects.cs', 'L12RuntimeEffectRoutes.cs',
        'L12AdminControlPlane.cs', 'L12PlatformStore.cs', 'L12RoomManager.cs',
        'L12WebSocketServer.cs', 'MatchRecorder.cs'
    )
    $runtimeFiles = @(Get-ChildItem -LiteralPath $sourcePath -Filter '*.cs' -File | Where-Object {
        $_.Name -notin $excluded -and $_.Name -notmatch '^L12PlatformStore\.'
    })
    $testFiles = @(Get-ChildItem -LiteralPath $testPath -Filter '*.cs' -File)
    $runtimeTexts = @{}
    foreach ($file in $runtimeFiles) {
        $runtimeTexts[$file.FullName] = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    }
    $testTexts = @{}
    foreach ($file in $testFiles) {
        $testTexts[$file.FullName] = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    }

    $result = @{}
    foreach ($card in $Cards) {
        $id = [string]$card.id
        $needle = '"' + $id + '"'
        $sourceNames = New-Object System.Collections.Generic.List[string]
        $categories = New-Object System.Collections.Generic.List[string]
        foreach ($file in $runtimeFiles) {
            if (-not $runtimeTexts[$file.FullName].Contains($needle)) { continue }
            $sourceNames.Add($file.Name)
            $category = switch -Regex ($file.Name) {
                '^L12ActiveAbilities' { '主动注册'; break }
                '^L12(CombatTimeline|AuthorityEvents)' { '时机集合'; break }
                '^L12(S2CounterTactics|PromptsAndSetup)' { '响应池/Prompt'; break }
                '^L12StructuredCardRules|^RuleKernel' { '静态/派生规则'; break }
                '^L12(Actions|GameEngine)' { '行动/状态入口'; break }
                '^L12(SpecialDeckRules|DeckValidator)' { '特殊牌堆/区域'; break }
                default { '权威效果处理' }
            }
            if (-not $categories.Contains($category)) { $categories.Add($category) }
        }

        # 这些类型由通用区域模型驱动，不依赖单卡 CardId 字面量。
        switch ([string]$card.cardType) {
            'master' { if (-not $categories.Contains('主宰入口')) { $categories.Add('主宰入口') } }
            'rune' {
                if (-not $categories.Contains('阵营/士气入口')) { $categories.Add('阵营/士气入口') }
                if ($sourceNames.Count -eq 0) { $sourceNames.Add('L12DeckValidator.cs(按 rune 类型派生)') }
            }
            'divinity' { if (-not $categories.Contains('特殊区入口')) { $categories.Add('特殊区入口') } }
            'trial' { if (-not $categories.Contains('试炼/特殊区入口')) { $categories.Add('试炼/特殊区入口') } }
            'token' { if (-not $categories.Contains('Token/特殊区入口')) { $categories.Add('Token/特殊区入口') } }
        }

        $tests = New-Object System.Collections.Generic.List[string]
        foreach ($file in $testFiles) {
            if ($testTexts[$file.FullName].Contains($needle)) { $tests.Add($file.Name) }
        }
        $result[$id] = [pscustomobject]@{
            Categories = @($categories | Sort-Object -Unique)
            Sources = @($sourceNames | Sort-Object -Unique)
            Tests = @($tests | Sort-Object -Unique)
        }
    }
    return $result
}
