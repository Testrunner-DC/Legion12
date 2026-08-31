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

    # Tests may declare stable semantic coverage without repeating a card id.
    # Supported xUnit traits: ability:<protocol id>, type:<cardType>, entry:<shared entry>.
    $semanticTestFiles = @{}
    foreach ($file in $testFiles) {
        $matches = [regex]::Matches($testTexts[$file.FullName],
            '\[\s*Trait\(\s*"L12Evidence"\s*,\s*"(?<selector>(?:ability|type|entry):[^"]+)"\s*\)\s*\]')
        foreach ($match in $matches) {
            $selector = $match.Groups['selector'].Value
            if (-not $semanticTestFiles.ContainsKey($selector)) {
                $semanticTestFiles[$selector] = New-Object System.Collections.Generic.List[string]
            }
            if (-not $semanticTestFiles[$selector].Contains($file.Name)) {
                $semanticTestFiles[$selector].Add($file.Name)
            }
        }
    }

    $entrySelectorByCategory = @{
        '主动注册' = 'active'
        '时机集合' = 'timing'
        '响应池/Prompt' = 'response'
        '静态/派生规则' = 'structured-rule'
        '行动/状态入口' = 'action'
        '特殊牌堆/区域' = 'special-deck'
        '权威效果处理' = 'effect-handler'
        '主宰入口' = 'master'
        '阵营/士气入口' = 'rune'
        '特殊区入口' = 'divinity'
        '试炼/特殊区入口' = 'trial'
        'Token/特殊区入口' = 'token'
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

        $semanticSelectors = New-Object System.Collections.Generic.List[string]
        $semanticSelectors.Add("type:$([string]$card.cardType)")
        foreach ($category in $categories) {
            if ($entrySelectorByCategory.ContainsKey($category)) {
                $semanticSelectors.Add("entry:$($entrySelectorByCategory[$category])")
            }
        }

        $abilityIds = New-Object System.Collections.Generic.List[string]
        foreach ($file in $runtimeFiles) {
            $text = $runtimeTexts[$file.FullName]
            foreach ($line in ($text -split "`r?`n")) {
                if (-not $line.Contains($needle)) { continue }
                foreach ($token in [regex]::Matches($line, '"(?<value>[a-z][A-Za-z0-9_-]+)"')) {
                    $abilityId = $token.Groups['value'].Value
                    if (-not $abilityIds.Contains($abilityId)) { $abilityIds.Add($abilityId) }
                }
            }
            $armPattern = [regex]::Escape($needle) + '\s*=>\s*\[(?<body>.*?)\](?:\s*,|\s*\r?\n)'
            foreach ($arm in [regex]::Matches($text, $armPattern,
                [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
                foreach ($ability in [regex]::Matches($arm.Groups['body'].Value,
                    'new\(\s*"(?<ability>[a-z][A-Za-z0-9_-]+)"')) {
                    $abilityId = $ability.Groups['ability'].Value
                    if (-not $abilityIds.Contains($abilityId)) { $abilityIds.Add($abilityId) }
                }
            }
        }
        foreach ($abilityId in $abilityIds) { $semanticSelectors.Add("ability:$abilityId") }

        foreach ($selector in ($semanticSelectors | Sort-Object -Unique)) {
            if (-not $semanticTestFiles.ContainsKey($selector)) { continue }
            foreach ($fileName in $semanticTestFiles[$selector]) {
                if (-not $tests.Contains($fileName)) { $tests.Add($fileName) }
            }
        }
        $result[$id] = [pscustomobject]@{
            Categories = @($categories | Sort-Object -Unique)
            Sources = @($sourceNames | Sort-Object -Unique)
            Tests = @($tests | Sort-Object -Unique)
        }
    }
    return $result
}
