[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

function Read-Source([string]$FileName) {
    $file = Get-ChildItem -LiteralPath $ProjectRoot -Filter $FileName -Recurse -File | Select-Object -First 1
    if ($null -eq $file) { throw "Missing source file: $FileName" }
    return [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
}

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) { throw $Message }
}

$plans = Read-Source 'L12PublicTriggerEffectPlans.cs'
$composite = Read-Source 'L12CompositeEffectPlans.cs'
$faction = Read-Source 'L12S2FactionEffects.cs'
$s1Extended = Read-Source 'L12S1ExtendedEffects.cs'
$s1Faction = Read-Source 'L12S1FactionEffects.cs'
$s2Universal = Read-Source 'L12S2UniversalEffects.cs'
$atomicPrograms = Read-Source 'AtomicEffects.cs'
$atomicRuntime = Read-Source 'L12AtomicRuntimeIntegration.cs'
$trialAdvancePlans = Read-Source 'L12TrialAdvanceEffectPlans.cs'
$attackPlans = Read-Source 'L12AttackPublicTriggerPlans.cs'
$entryPlans = Read-Source 'L12EnterPublicTriggerPlans.cs'
$kernel = Read-Source 'L12RuleKernelIntegration.cs'
$counter = Read-Source 'L12S2CounterTactics.cs'
$prompts = Read-Source 'L12PromptsAndSetup.cs'
$actions = Read-Source 'L12Actions.cs'
$gm = Read-Source 'L12GmCommands.cs'
$authority = Read-Source 'L12AuthorityEvents.cs'
$zones = Read-Source 'L12AuthoritativeCardZones.cs'
$cardEffects = Read-Source 'L12CardEffects.cs'
$continuations = Read-Source 'L12EffectContinuations.cs'
$remaining = Read-Source 'L12S2RemainingEffects.cs'
$runtimeDirectory = (Get-ChildItem -LiteralPath $ProjectRoot -Filter 'L12PublicTriggerEffectPlans.cs' -Recurse -File |
    Select-Object -First 1).Directory.FullName
$allRuntime = (@(Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.cs' -File | ForEach-Object {
    [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
}) -join "`n")
$remainingPromptTokenCount = [regex]::Matches($allRuntime, '\bCreatePrompt\(').Count
if ($remainingPromptTokenCount -gt 136) {
    throw "Resolution prompt inventory regressed above the ruling-closure ratchet: $remainingPromptTokenCount > 136"
}

foreach ($cardId in @(
    'S02-04M1', 'S02-0523', 'S01-02M3', 'S02-02M1', 'S02-01S1',
    'S01-0105', 'S01-0207', 'S01-0208', 'S01-0309', 'S01-0021', 'S01-0213',
    'S01-0223', 'S01-0320', 'S01-0224', 'S02-0202', 'S02-0203', 'S02-0205', 'S01-0206', 'S01-0407',
    'S01-0204', 'S01-0414', 'S01-0417',
    'S02-0304', 'S02-0305', 'S02-05M1', 'S02-06M1', 'S02-0102', 'S02-06S4'
)) {
    Assert-Contains $plans $cardId "Public trigger declaration plan is missing card $cardId."
}

foreach ($cardId in @('S01-0101', 'S01-0108', 'S01-0311', 'S02-0001', 'S02-0012', 'S02-01M1', 'S01-01C1')) {
    Assert-Contains $plans $cardId "Batch 6J-B public trigger declaration plan is missing card $cardId."
}
Assert-Contains $plans 'Batch6JBPublicTriggerPlans' 'Batch 6J-B triggers need one shared data-driven declaration table.'
Assert-Contains $plans 'PrepareBatch6JBPublicTriggerCandidate' 'Batch 6J-B public conditions and once reservations need one shared candidate filter.'
Assert-Contains $kernel '.Where(PrepareBatch6JBPublicTriggerCandidate)' 'Every TriggerBatch entry must filter Batch 6J-B candidates before ordering.'
Assert-Contains $kernel 'activation.TriggerCandidateId == candidate.CandidateId' 'A trigger candidate with an open declaration must not create duplicate PendingActivations.'
Assert-Contains $plans 'PublicTriggerStep("target-morale", "returnCost"' 'Lu Bu must declare the exact four-morale cost before stack entry.'
Assert-Contains $plans 'MoveGraveToLibraryBottom(player, physicalCosts)' 'Gustav must commit his ordered grave cost before stack entry.'
Assert-Contains $plans 'PublicTriggerStep("target-morale", "moraleTarget"' 'Mulan must declare the opponent morale target before stack entry.'
Assert-Contains $plans 'new L12CompositeEffectSegmentSpec("prayer-private"' 'Prayer private preview must reuse the shared ordinary-payment transaction.'
Assert-Contains $cardEffects 'PublicTriggerDeclared(item, "moraleTarget")' 'Mulan resolution must consume only the immutable declared morale target.'
Assert-Contains $cardEffects 'PublicTriggerDeclared(item, "mode") == "mode:use"' 'Lu Bu resolution must consume only the immutable declared mode.'
Assert-Contains $s1Faction 'PublicTriggerDeclared(item, "mode") == "mode:use"' 'Gustav resolution must consume only the immutable declared mode.'
Assert-Contains $s2Universal 'PublicTriggerDeclared(item, "mode") != "mode:use"' 'Exorcist resolution must consume only the immutable declared mode.'
Assert-Contains $s2Universal 'CreateTriggerCandidate(item.Controller, prayer, "prayer-private"' 'Prayer refusal must queue its private preview as an independent trigger candidate.'
Assert-Contains $remaining '["ability"] = "wukongReturnMorale"' 'Wukong return must queue its optional morale trigger through the shared declaration route.'
Assert-Contains $prompts '["ability"] = "factionZeroRecovery"' 'Tianting zero-morale recovery must queue a public trigger candidate instead of a direct prompt.'
$ringDeclarationStart = $entryPlans.IndexOf('case "ring":', [StringComparison]::Ordinal)
$ringDeclarationEnd = $entryPlans.IndexOf('case "arthur":', $ringDeclarationStart, [StringComparison]::Ordinal)
if ($ringDeclarationStart -lt 0 -or $ringDeclarationEnd -lt 0) {
    throw 'Ring entry declaration branch is missing.'
}
$ringDeclaration = $entryPlans.Substring($ringDeclarationStart, $ringDeclarationEnd - $ringDeclarationStart)
Assert-Contains $ringDeclaration 'player.Library.Count == 0' 'Ring declaration may only inspect the public library count.'
if ($ringDeclaration.Contains('player.Library.Any(')) {
    throw 'Ring declaration must not inspect hidden universal-card match existence.'
}
foreach ($legacy6JB in @(
    's2-prayer-private-cost', 's2-exorcist-return', 'gustav-ready-choice', 'gustav-ready-return',
    's2-wukong-return-morale', 'Continuation == "faction-zero-recovery"',
    '["action"] = "lubu-ready"', '["action"] = "mulan-lock-morale"'
)) {
    if ($allRuntime.IndexOf($legacy6JB, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6J-B public declaration returned: $legacy6JB"
    }
}
foreach ($legacy6JBAction in @(
    'battle-until-dawn-draw-choice', 'empty-city-draw-choice',
    'camp-followup-choice', 'camp-followup-pay', 'scout-followup-choice', 'scout-followup-pay'
)) {
    if ($allRuntime.IndexOf($legacy6JBAction, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6J-B hand/response declaration returned: $legacy6JBAction"
    }
}

foreach ($cardId in @(
    'S01-0001', 'S01-0112', 'S01-0115', 'S01-0207', 'S01-0210', 'S01-0303',
    'S01-0304', 'S01-0306', 'S01-0313', 'S01-0403', 'S01-0407', 'S02-0002',
    'S02-01S1', 'S02-0301', 'S02-0508', 'S02-0518', 'S02-0601', 'S02-0615'
)) {
    Assert-Contains $plans ('["' + $cardId + '|') "Batch 6I-B public trigger plan is missing card $cardId."
}
Assert-Contains $plans 'PrepareBatch6IBPublicTriggerCandidate' 'Batch 6I-B needs one shared pre-batch candidate filter.'
Assert-Contains $kernel '.Where(PrepareBatch6IBPublicTriggerCandidate)' 'Every TriggerBatch entry must filter Batch 6I-B candidates before ordering.'
Assert-Contains $plans 'candidate.Data["return-morale-prepaid"] = "true"' 'Jing Ke must prepay the exact declared morale before stack entry.'
Assert-Contains $plans 'candidate.Data["cleanupReservation"] = pendingKey' 'Alice must reserve her once-per-turn use while declaration is pending.'
Assert-Contains $plans 'player.UsedAbilities.Add(onceKey)' 'Alice must finalize her once-per-turn use before stack entry.'
Assert-Contains $s1Extended 'PublicTriggerDeclared(item, "entryCards")' 'Uesugi must resolve only the privately declared counter tactics.'
Assert-Contains $s1Extended 'PublicTriggerDeclared(item, "entrySlot1")' 'Uesugi must resolve only the publicly declared back-row slots.'
Assert-Contains $faction 'PublicTriggerDeclared(item, "recoverTarget")' 'Theseus must resolve only the declared promotion target.'
$gwenEffectText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5qC85rip6I6J5a6J6Zi15Lqh5pe25pWI5p6c'))
$legacyLamorakName = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('5YWw6ams5rSb5YWL'))
Assert-Contains $faction 'AddEvent("reveal", item.Controller' 'Theseus must reveal the declared promotion before adding it to hand.'
Assert-Contains $faction $gwenEffectText 'Gwen must use her own printed name in the migrated resolver.'
if ($faction.IndexOf($legacyLamorakName, [StringComparison]::Ordinal) -ge 0) {
    throw 'The legacy Gwen log must not misname Lamorak.'
}
$legacyDeathHandlers = $s1Extended + "`n" + $s1Faction + "`n" + $s2Universal + "`n" + $faction
foreach ($legacyDeathPrompt in @(
    'teach-death', 'sunwu-recover', 'jingke-kill', 'kenshin-set-counters',
    'ryoma-summon-card', 'ryoma-summon-slot', 'tutankhamun-top', 'nitocris-summon',
    'harald-kill', 'oddr-tap', 's2-alice-ready', 's2-xiaotian-death',
    's2-optional-death-draw', 's2-lamorak-death', 's2-arthur-summon', 's2-arthur-summon-slot'
)) {
    if ($legacyDeathHandlers.IndexOf('["action"] = "' + $legacyDeathPrompt + '"', [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6I-B declaration prompt returned: $legacyDeathPrompt"
    }
}

foreach ($cardId in @(
    'S01-0104', 'S01-0106', 'S01-0203', 'S01-0208', 'S01-0301', 'S01-0306', 'S01-0311',
    'S01-0402', 'S01-0405', 'S01-0406', 'S01-0408', 'S01-0413', 'S01-0416', 'S02-0103',
    'S02-0509', 'S02-0511', 'S02-0517', 'S02-0519', 'S02-0605', 'S02-0606', 'S02-0607',
    'S02-0608', 'S02-0612', 'S02-0617'
)) {
    Assert-Contains $attackPlans ('["' + $cardId + '"]') "Attack public trigger plan is missing card $cardId."
}
Assert-Contains $attackPlans 'AttackPublicTriggerPlans' 'Attack declarations need a shared data-driven plan table.'
Assert-Contains $attackPlans 'TryQueueAttackPublicTriggerCandidates' 'Attack triggers must share one candidate entry.'
Assert-Contains $attackPlans 'CreateTriggerCandidate(controller, source, trigger, candidateText, candidateData, source)' 'Attack candidates must retain a last-known source snapshot.'
Assert-Contains $attackPlans 'PayAttackPublicCost(candidate, activation, plan, player, source, costIds)' 'Attack colon costs must commit before stack entry.'
Assert-Contains $attackPlans 'PublicLegions(player).Select(card => card.InstanceId), requiredChoice: required)' 'Menes must be allowed to declare itself as the legion discard cost.'
Assert-Contains $attackPlans '"discard-own-legion" => PublicLegions(player).Any(),' 'Menes must remain activatable when it is the only friendly legion.'
if ($attackPlans.IndexOf('sacrifice.InstanceId == candidate.SourceInstanceId', [StringComparison]::Ordinal) -ge 0) {
    throw 'Menes self-discard cost must not be rejected during declaration commit.'
}
Assert-Contains $attackPlans '["attackPlan"] = "gawain-buff"' 'Gawain rune consumption and buff must remain independent stack segments.'
Assert-Contains $attackPlans 'Candidate("richard-defense"' 'Richard defense tax must be its own same-time candidate.'
Assert-Contains $attackPlans 'Candidate("richard-squires"' 'Richard squire cost/buff must be its own same-time candidate.'
Assert-Contains $attackPlans 'Candidate("robin-rune"' 'Robin rune gain must be its own required candidate.'
Assert-Contains $attackPlans 'Candidate("robin-draw"' 'Robin draw must be its own optional candidate.'

Assert-Contains $plans 'HasPublicTriggerDeclarationPlan' 'Public trigger plans need a shared route predicate.'
Assert-Contains $plans 'QueueOrPushTriggeredEffect' 'Direct trigger entry points must join the TriggerBatch declaration route.'
Assert-Contains $actions 'QueueOrPushTriggeredEffect' 'Normal hand entry and attack triggers must not bypass public declarations.'
Assert-Contains $gm 'QueueOrPushTriggeredEffect' 'GM effect entry must not bypass public declarations.'
Assert-Contains $authority 'QueueOrPushTriggeredEffect' 'Non-hand effect entry must not bypass public declarations.'
Assert-Contains $kernel 'TryBeginPublicTriggerDeclaration(candidate, source)' 'Trigger candidates must enter the public declaration planner.'
Assert-Contains $kernel 'TryCompletePublicTriggerDeclaration(candidate, activation)' 'Trigger declarations need a shared atomic completion hook.'
Assert-Contains $plans 'HasTrialAdvanceTriggerDeclarationPlan(cardId, trigger, data)' 'Trial advances must enter the shared public trigger route.'
Assert-Contains $plans 'TryBeginTrialAdvanceTriggerDeclaration(candidate, source)' 'Trial trigger choices must be declared before stack entry.'
Assert-Contains $plans 'TryCompleteTrialAdvanceTriggerDeclaration(candidate, activation)' 'Trial trigger costs must commit before stack entry.'
foreach ($cardId in @('S02-0602', 'S02-0604', 'S02-0610', 'S02-0614', 'S02-06M2', 'S02-06D1')) {
    Assert-Contains $trialAdvancePlans $cardId "Trial advance event plan is missing card $cardId."
}
foreach ($contract in @(
    'BeginTrialAdvanceActivation', 'TryCommitTrialAdvanceActivation', 'TryResolveTrialAdvanceEffect',
    '["trialAdvanceEvent"] = "true"', 'QueueFinnReadyAfterTrial', 'QueueAvalonTurnStart',
    'L12S2ZoneOps.SpendRunes(player, 1)', 'source.Tapped = true', 'FinishStackItem(item)'
)) {
    Assert-Contains $trialAdvancePlans $contract "Trial advance event contract is missing: $contract"
}

$directRuneDecrements = [regex]::Matches($allRuntime, 'SpecialZones\.Runes\s*--').Count
if ($directRuneDecrements -ne 0) {
    throw "Rune spend contract regressed: found $directRuneDecrements direct decrement(s); all rune spending must use L12S2ZoneOps.SpendRunes."
}
$directRuneSubtractions = [regex]::Matches($allRuntime, 'SpecialZones\.Runes\s*-=').Count
if ($directRuneSubtractions -ne 1) {
    throw "Rune spend contract regressed: expected the single subtraction inside L12S2ZoneOps.SpendRunes, found $directRuneSubtractions."
}
if ($trialAdvancePlans.IndexOf('TrialCompleted = true', [StringComparison]::Ordinal) -ge 0 -or
    $trialAdvancePlans.IndexOf('QueueCompletedTrialTriggerBatch', [StringComparison]::Ordinal) -ge 0) {
    throw 'Advancing a trial to 8 must not automatically complete it or publish the Batch 6B completion event.'
}
Assert-Contains $plans 'TryConsumeSelectedResources(player, 1' 'Tsukuyomi must commit its declared resource before stack entry.'
Assert-Contains $plans 'ReturnSelectedMoraleById(player, [costId], 1)' 'Liu Bei must return the declared morale before stack entry.'
Assert-Contains $plans 'DamageMaster(candidate.Controller, 1,' 'Brynhild must pay the known master-damage cost before stack entry.'
Assert-Contains $plans 'candidate.Data["preserveIndependentStack"] = "true"' 'Immortal Gift must preserve its independent draw segment when summon declaration is absent.'
Assert-Contains $plans 'activation.DeclaredValues["entryCard"] = ["mode:none"]' 'Immortal Gift invalid summon segment must cancel independently.'
Assert-Contains $plans 'Batch6GAPublicTriggerPlan' 'Batch 6G-A triggers need one shared data-driven declaration route.'
Assert-Contains $plans 'VerifiedAtomicOptionalTriggerPlan' 'Verified atomic Optional atoms need one shared public declaration route.'
Assert-Contains $plans 'PrepareVerifiedAtomicOptionalCandidate' 'Verified atomic Optional conditions must be checked before candidate admission.'
Assert-Contains $plans 'TakeWhile(atom => atom.Kind != L12AtomKinds.Optional)' 'Only public conditions before Optional may gate candidate creation.'
Assert-Contains $plans 'candidate.Data["verifiedAtomicConditionLocked"] = "true"' 'Verified atomic trigger-time conditions must be immutable after candidate creation.'
Assert-Contains $kernel '.Where(PrepareVerifiedAtomicOptionalCandidate)' 'Every TriggerBatch entry must filter verified Optional candidates through the common condition gate.'
Assert-Contains $atomicRuntime 'PublicTriggerDeclared(item, "mode") != "mode:use"' 'Verified atomic resolution must consume only the immutable declared mode.'
Assert-Contains $atomicRuntime 'item.Data.GetValueOrDefault("verifiedAtomicConditionLocked") != "true"' 'Verified atomic resolution must not re-evaluate a trigger-time condition locked before stack entry.'
foreach ($verifiedOptionalProgram in @(
    'Program("S01-0413", "enter"', 'Program("S01-0405", "attack"',
    'Program("S01-0409", "after-attack"', 'Program("S01-0115", "enter"',
    'Program("S01-0301", "death"', 'Program("S01-0304", "enter"',
    'Program("S01-0309", "death"', 'Program("S02-0104", "enter"',
    'Program("S02-0203", "death"', 'Program("S02-0402", "death"',
    'Program("S02-0512", "death"', 'Program("S02-0507", "enter"',
    'Program("S02-0507", "promotion-enter"', 'Program("S02-0616", "enter"'
)) {
    Assert-Contains $atomicPrograms $verifiedOptionalProgram "Verified atomic Optional inventory is missing: $verifiedOptionalProgram"
}
if ($atomicRuntime.IndexOf('CreatePrompt(', [StringComparison]::Ordinal) -ge 0) {
    throw 'Verified atomic runtime must not create any resolution-time Optional prompt.'
}
if ($allRuntime.IndexOf('verified-atomic-optional', [StringComparison]::Ordinal) -ge 0) {
    throw 'Legacy verified-atomic-optional continuation returned to production runtime.'
}
Assert-Contains $plans '("S02-0102", "enter", _, _) => "limu-enter"' 'Li Mu enter must use the public trigger declaration route.'
Assert-Contains $plans 'PublicTriggerStep("option", "revealMode"' 'Li Mu reveal opt-in must be declared before stack entry.'
Assert-Contains $plans 'PublicTriggerStep("option", "drawMode"' 'Li Mu independent draw opt-in must be declared before stack entry.'
Assert-Contains $plans 'CompositeFirstSegmentData("trigger:S02-0102:enter"' 'Li Mu must skip disabled segments before creating its first real stack item.'
Assert-Contains $composite '["trigger:S02-0102:enter"]' 'Li Mu enter needs one shared composite trigger plan.'
Assert-Contains $composite 'RequiredDeclarationKey: "revealMode"' 'Li Mu reveal segment must consume its immutable declaration key.'
Assert-Contains $composite 'RequiredDeclarationKey: "drawMode"' 'Li Mu draw segment must consume its immutable declaration key.'
Assert-Contains $composite 'First(index => CompositeSegmentEnabled(segments[index], declared))' 'Composite triggers must start at their first declared segment without an empty stack item.'
Assert-Contains $faction 'private void RevealS2LiMuTop(L12StackItem item)' 'Li Mu hidden reveal must remain in the legal resolution handler.'
Assert-Contains $faction 'var top = player.Library[0];' 'Li Mu must read the library top only after its reveal segment starts resolving.'
Assert-Contains $faction '["action"] = "s2-limu-tactic"' 'Li Mu must delay the free-play-or-bottom choice until after the legal reveal.'
Assert-Contains $plans 'margaretMasterDamage' 'Margaret damage trigger must predeclare and prepay its rest cost.'
Assert-Contains $allRuntime 'margaret-heal-lock' 'Margaret heal and heal-lock sentences must remain independent stack segments.'
Assert-Contains $plans 'cleanupReservation' 'Optional once-per-turn triggers must reserve pending state before player declaration.'
Assert-Contains $plans 'player.UsedAbilities.Add(onceKey)' 'Committed optional triggers must consume their once before stack entry.'
Assert-Contains $plans 'card.Tapped && !card.IsGodPower' 'Artemis must declare an exact rested ordinary morale target.'
Assert-Contains $kernel 'SourceSnapshot = CaptureLastKnownSourceSnapshot(sourceSnapshot ?? card)' 'Every generated trigger candidate must carry a last-known source snapshot.'
Assert-Contains $kernel 'FindAuthoritativeCard(candidate.SourceInstanceId)' 'Trigger declarations must resolve sources through the internal authoritative lookup.'
Assert-Contains $plans 'owner-unused-slot' 'Tomb Construct must declare owner battlefield slots before stack entry.'
Assert-Contains $plans 'declaredGuardOwners' 'Tomb Construct must retain each attached guard owner with its declared slot.'
Assert-Contains $zones 'TryMoveAuthoritativeCardToOwnerLibraryTop' 'Return-to-library effects need one owner-and-instance zone transaction.'
foreach ($zoneName in @('field', 'relic', 'extra', 'god-power', 'trial', 'canopic', 'resolving', 'hand', 'library', 'graveyard', 'removed')) {
    Assert-Contains $zones ('"' + $zoneName + '"') "Authoritative source lookup is missing zone $zoneName."
}
Assert-Contains $zones 'locations.Count != 1' 'Authority-zone transfer must reject missing or duplicate real instances.'
Assert-Contains $zones 'QueueReturnedToLibraryTopTrigger' 'Returning Katsura must create an independent trigger candidate.'
Assert-Contains $cardEffects 'case "return-library-top"' 'Returned-to-library trigger needs its own stack dispatch.'
if ($prompts.IndexOf('FindAuthoritativeCard', [StringComparison]::Ordinal) -ge 0) {
    throw 'Client prompt lookup must not expose the internal authoritative source query.'
}
if ($zones.IndexOf('Library.Insert(0, sourceSnapshot)', [StringComparison]::Ordinal) -ge 0) {
    throw 'A last-known snapshot must never be inserted as a real zone card.'
}

Assert-Contains $counter 'var revealed = player.Library[0];' 'Cosmos Yin must inspect the hidden library top only during legal resolution.'
Assert-Contains $counter 'CreateDelayedPublicResolutionPrompt(item' 'Cosmos Yin must declare its public target only after the hidden reveal.'
Assert-Contains $plans 'data["declarationTiming"] = "post-hidden-reveal"' 'Delayed public resolution prompts need an explicit post-reveal timing marker.'
Assert-Contains $prompts 'or "S02-0106")' 'Cosmos Yin responses must route to their own effect instead of generic negate handling.'
if ($plans.IndexOf('"S02-0106"', [StringComparison]::Ordinal) -ge 0) {
    throw 'Cosmos Yin must not be placed in the pre-reveal public trigger planner.'
}
foreach ($hiddenCardId in @('S01-0103', 'S02-0401', 'S02-0403')) {
    if ($hiddenCardId -eq 'S02-0403' -and $plans.IndexOf('"' + $hiddenCardId + '"', [StringComparison]::Ordinal) -ge 0) {
        throw "Hidden-information effect $hiddenCardId must remain outside the pre-reveal public trigger planner."
    }
}

foreach ($cardId in @(
    'S01-0101','S01-0102','S01-0103','S01-0108','S01-0110','S01-0111','S01-0112',
    'S01-0201','S01-0202','S01-0205','S01-0210','S01-0215','S01-0217','S01-0220',
    'S01-0313','S01-0316','S01-0317','S01-0402','S01-0403','S01-0406','S01-0408',
    'S01-0411','S01-0412','S01-0416','S01-0417','S02-0003','S02-0008','S02-0204',
    'S02-0303','S02-0401','S02-0402','S02-0404','S02-0501','S02-0502','S02-0505',
    'S02-0506','S02-0513','S02-0518','S02-0520','S02-0601','S02-0608','S02-0613',
    'S02-0617','S02-0619'
)) {
    Assert-Contains $entryPlans ('["' + $cardId + '|') "Batch 6J-A entry declaration inventory is missing card $cardId."
}
foreach ($contract in @(
    'PrepareBatch6JAEnterCandidate', 'TryBeginBatch6JAEnterDeclaration',
    'TryCompleteBatch6JAEnterDeclaration', 'TryResolveBatch6JAEnterEffect',
    'ReturnSelectedMoraleById', 'L12S2ZoneOps.SpendRunes', 'MoveHandToGrave',
    'BeginTakedaFollowupWithinStack'
)) {
    Assert-Contains $entryPlans $contract "Batch 6J-A public entry contract is missing: $contract"
}
Assert-Contains $kernel '.Where(PrepareBatch6JAEnterCandidate)' 'Every TriggerBatch entry must filter Batch 6J-A enter candidates.'
foreach ($obsoleteTakedaSplit in @('batch6JAFollowup', 'takeda-followup', 'case "enter-followup"')) {
    if ($allRuntime.IndexOf($obsoleteTakedaSplit, [StringComparison]::Ordinal) -ge 0) {
        throw "Takeda must resolve as one stack item; obsolete split route returned: $obsoleteTakedaSplit"
    }
}
Assert-Contains $composite '["trigger:S01-0111:enter"]' 'Zhuge reveal and disaster adjustment must remain independent stack segments.'
Assert-Contains $composite '["trigger:S01-0217:enter"]' 'Canopic Jar One target and discard must remain independent stack segments.'
Assert-Contains $composite '["trigger:S01-0220:enter"]' 'Canopic Jar Four target and discard must remain independent stack segments.'
foreach ($legalHiddenPrompt in @('s2-ring-search','s2-magatama-search','s2-takeda-search','s2-robin-summon-squire')) {
    Assert-Contains $entryPlans $legalHiddenPrompt "Legal post-reveal hidden prompt is missing: $legalHiddenPrompt"
}

foreach ($legacy in @(
    's2-tsukuyomi-pay', 's2-tsukuyomi-target', 's2-tsukuyomi-slot', 's2-tsukuyomi-ready',
    's2-trojan-confirm', 's2-trojan-slot', 'medjed-damage-response',
    's2-nephthys-own-death', 's2-nephthys-scarab-slot',
    's2-xiaotian-morale', 's2-xiaotian-slot', 'liubei-card', 'liubei-summon', 'liubei-slot',
    'brynhild-sigurd', 'regency-card', 'regency-slot', 'kaba-summon', 'kaba-slot',
    'saladin-move', 'saladin-slot', 'ryoma-pick', 'ryoma-slot', 's2-scarab-enter-slot',
    'katsura-return', 'katsura-ready-morale', 'kusanagi-return-top'
)) {
    if ($allRuntime.IndexOf($legacy, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack public trigger continuation returned: $legacy"
    }
}

foreach ($legacyAttackPrompt in @(
    'hanxin-attack', 'guanyu-attack', 'menes-sacrifice', 'ay-pay', 'ay-buff', 'beowulf-buff',
    'olaf-strong', 'gustav-attack-choice', 'gustav-attack-return', 'nobunaga-attack-pay',
    'nobunaga-debuff', 'hijikata-attack-pay', 'hijikata-attack-kill', 'takasugi-attack-pay',
    'hiromasa-disable', 's2-odysseus-show-tactic', 's2-parrot-god-power',
    's2-penthesilea-god-power', 's2-achilles-god-power', 's2-bors-strong',
    's2-percival-attack-discard', 's2-gawain-runes', 's2-richard-attack-squires',
    's2-scathach-rune'
)) {
    if ($allRuntime.IndexOf($legacyAttackPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack attack declaration continuation returned: $legacyAttackPrompt"
    }
}

foreach ($legacyTrialPrompt in @(
    's2-lancelot-entry-charge', 's2-lancelot-kill', 's2-galahad-entry-trial',
    's2-finn-entry-trial', 's2-finn-entry-ready', 's2-constance-entry', 's2-angus-trial'
)) {
    if ($allRuntime.IndexOf($legacyTrialPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack trial declaration continuation returned: $legacyTrialPrompt"
    }
}

foreach ($legacyBatch6GAPrompt in @(
    's2-margaret-entry-mill', 's2-margaret-master-damage', 's2-ring-draw',
    's2-morrigan-enemy-death', 's2-limu-morale', 's2-grail-round-table-rune'
)) {
    if ($allRuntime.IndexOf($legacyBatch6GAPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6G-A trigger continuation returned: $legacyBatch6GAPrompt"
    }
}

foreach ($legacyBatch6GBPrompt in @('s2-limu-reveal', 's2-limu-draw')) {
    if ($allRuntime.IndexOf($legacyBatch6GBPrompt, [StringComparison]::Ordinal) -ge 0) {
        throw "Legacy post-stack Batch 6G-B Li Mu continuation returned: $legacyBatch6GBPrompt"
    }
}

Write-Host 'Public trigger declaration guard passed.'
