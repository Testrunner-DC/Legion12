param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$serverProject = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 'GrandUMIServer.csproj' -File | Select-Object -First 1
if ($null -eq $serverProject) { throw 'GrandUMIServer.csproj not found.' }
$sourcePath = Join-Path $serverProject.DirectoryName 'TwelveLegions'
$cards = @{}
foreach ($fileName in @('cards.s1.json', 'cards.s2.json')) {
    $path = Join-Path (Join-Path $sourcePath 'Data') $fileName
    foreach ($card in ([System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)) {
        $cards[$card.id] = $card.nameZh
    }
}

$targetFiles = @(
    'L12CardEffects.cs', 'L12Disasters.cs', 'L12S1ExtendedEffects.cs', 'L12S1FactionEffects.cs',
    'L12S2UniversalEffects.cs', 'L12S2FactionEffects.cs', 'L12S2CounterTactics.cs'
)
$routes = New-Object System.Collections.Generic.List[object]
foreach ($fileName in $targetFiles) {
    $path = Join-Path $sourcePath $fileName
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -notmatch 'case\s+"(?<id>S\d{2}-[A-Za-z0-9]+)"') { continue }
        $id = $Matches.id
        $method = ''
        for ($cursor = $index; $cursor -ge 0; $cursor--) {
            if ($lines[$cursor] -match '^\s*(?:private|public|internal)\s+.*\(') { $method = $lines[$cursor]; break }
        }
        $trigger = switch -Regex ($method) {
            'CompletedTrialTrigger' { 'completed-trial'; break }
            'S2CounterEffect' { 's2-reaction'; break }
            'PromotionEnter' { 'promotion-enter'; break }
            'AfterAttack' { 'after-attack'; break }
            'AfterDamage' { 'after-damage'; break }
            'Enter' { 'enter'; break }
            'Tactic' { 'play'; break }
            'Attack' { 'attack'; break }
            'Death' { 'death'; break }
            'Leave' { 'leave'; break }
            'Reaction' { 'reaction'; break }
            'Disaster' { 'disaster'; break }
            'Active' { 'active'; break }
            default { '' }
        }
        if (![string]::IsNullOrWhiteSpace($trigger) -and $trigger -ne 'completed-trial') {
            $routes.Add([pscustomobject]@{ CardId = $id; Trigger = $trigger; Flow = [string]$cards[$id] })
        }
    }
}

if ($routes.Count -eq 0) {
    throw 'No legacy card-id cases were found. Migration has already completed; refusing to overwrite the checked-in route registry.'
}

$routeRows = ($routes | Sort-Object CardId, Trigger -Unique | ForEach-Object {
    $flow = $_.Flow.Replace('"', '\"')
    "        new(`"$($_.CardId)`", `"$($_.Trigger)`", `"$flow`"),"
}) -join [Environment]::NewLine
$routeSource = @"
using System.Collections.ObjectModel;

namespace TwelveLegions.Server;

public sealed record L12RuntimeEffectRoute(string CardId, string Trigger, string Flow);

/// <summary>
/// 旧的卡号 switch 已迁移为结构化路由数据。每条路由先进入可执行原子解释器，
/// 再按语义流程名调度通过回归验证的复合结算。该表是后台、实战与回放的共用入口。
/// </summary>
public static class L12RuntimeEffectRoutes
{
    private static readonly L12RuntimeEffectRoute[] Routes =
    [
$routeRows
    ];

    private static readonly IReadOnlyDictionary<string, L12VerifiedAtomicProgram> ProgramsByKey =
        new ReadOnlyDictionary<string, L12VerifiedAtomicProgram>(Routes.ToDictionary(RouteKey, ToProgram, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyCollection<L12VerifiedAtomicProgram> AllPrograms { get; } = ProgramsByKey.Values.ToArray();

    public static L12VerifiedAtomicProgram? FindProgram(string cardId, string trigger)
        => ProgramsByKey.TryGetValue(RouteKey(cardId, trigger), out var program) ? program : null;

    private static L12VerifiedAtomicProgram ToProgram(L12RuntimeEffectRoute route)
    {
        var trigger = L12EffectAtomRegistry.Get(L12AtomKinds.Trigger);
        var flow = L12EffectAtomRegistry.Get(L12AtomKinds.CompositeFlow);
        return new L12VerifiedAtomicProgram(route.CardId, route.Trigger,
        [
            new L12EffectAtom("atom-1", L12AtomKinds.Trigger, route.Trigger, 1,
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["timing"] = route.Trigger }),
                trigger.RuntimeExecutable, "structured-route", "trigger"),
            new L12EffectAtom("atom-2", L12AtomKinds.CompositeFlow, route.Flow, 2,
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["flow"] = route.Flow }),
                flow.RuntimeExecutable, "structured-route", "resolution"),
        ], $"{route.CardId}:{route.Trigger}:composite");
    }

    private static string RouteKey(L12RuntimeEffectRoute route) => RouteKey(route.CardId, route.Trigger);
    private static string RouteKey(string cardId, string trigger) => $"{cardId}|{trigger}";
}
"@
[System.IO.File]::WriteAllText((Join-Path $sourcePath 'L12RuntimeEffectRoutes.cs'), $routeSource, [System.Text.UTF8Encoding]::new($false))

foreach ($fileName in $targetFiles) {
    $path = Join-Path $sourcePath $fileName
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $text = [regex]::Replace($text, 'case\s+"(?<id>S\d{2}-[A-Za-z0-9]+)"', {
        param($match)
        $name = [string]$cards[$match.Groups['id'].Value]
        if ([string]::IsNullOrWhiteSpace($name)) { throw "Missing card name for $($match.Groups['id'].Value)" }
        'case "' + $name.Replace('"', '\"') + '"'
    })
    $text = $text.Replace('switch (card.CardId)', 'switch (AtomicFlowKey(item, card))')
    $text = $text.Replace('switch (item.SourceCardId)', 'switch (AtomicFlowKey(item))')
    $text = $text.Replace('switch (disaster.CardId)', 'switch (AtomicFlowKey(item, disaster))')
    $text = $text.Replace('switch (trial.CardId)', 'switch (trial.Name)')
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}

Write-Host "Generated $(@($routes | Sort-Object CardId, Trigger -Unique).Count) atomic routes and migrated card cases."
