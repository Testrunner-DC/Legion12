[CmdletBinding()]
param(
    [string]$CacheRoot = 'D:/GPT/Legion12/cache/primary/storage-native-symbol-policy-20261022',
    [string]$ArtifactRoot = 'D:/GPT/Legion12/artifacts/storage-output-routing-20261016/audit-20261022',
    [string]$DotnetPath = 'dotnet',
    # Optional run-specific hot files, not a permanent dependency on one QA host.
    [string[]]$ProtectedFilePaths = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $repo 'ops/windows/L12GeneratedStorage.ps1')
$CacheRoot = Assert-L12GeneratedPath -Path $CacheRoot -Purpose 'native-symbol policy probes'
$ArtifactRoot = Assert-L12GeneratedPath -Path $ArtifactRoot -Purpose 'native-symbol policy evidence'
$null = & (Join-Path $repo 'ops/windows/Initialize-L12BuildEnvironment.ps1') -CacheRoot $CacheRoot
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Assert-L12GeneratedPath -Path (Join-Path $CacheRoot $runId) -Purpose 'isolated policy run'
$evidenceRoot = Assert-L12GeneratedPath -Path (Join-Path $ArtifactRoot "native-symbol-policy-$runId") -Purpose 'policy raw results'
$null = New-Item -ItemType Directory -Path $runRoot, $evidenceRoot
$policyPath = Join-Path $repo 'Directory.Build.targets'
$assertions = 0
$results = [Collections.Generic.List[object]]::new()

function Assert-Policy([bool]$Passed, [string]$Message) {
    if (-not $Passed) { throw "NATIVE_SYMBOL_POLICY: $Message" }
    $script:assertions++
}
function Write-Fixture([string]$Path, [string]$Content) {
    $null = Assert-L12GeneratedPath -Path $Path -Purpose 'synthetic policy fixture'
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
function Get-Metadata($Item, [string]$Name) {
    $property = $Item.PSObject.Properties[$Name]
    if ($null -eq $property) { return '' }
    return [string]$property.Value
}
function Test-ExcludedSymbol($Item) {
    return (Get-Metadata $Item 'NuGetPackageId') -ieq 'SkiaSharp.NativeAssets.Win32' -and
        (Get-Metadata $Item 'AssetType') -ieq 'native' -and
        [IO.Path]::GetFileName((Get-Metadata $Item 'Identity')) -ieq 'libSkiaSharp.pdb'
}
function Get-AssetKeys($Items) {
    @($Items | ForEach-Object {
        $item = $_
        (@('Identity', 'NuGetPackageId', 'NuGetPackageVersion', 'AssetType', 'DestinationSubPath', 'DestinationSubDirectory', 'CopyLocal') |
            ForEach-Object { Get-Metadata $item $_ }) -join '|'
    } | Sort-Object)
}
function Invoke-Probe([string]$Name, [string[]]$Arguments) {
    $output = @(& $DotnetPath msbuild @Arguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    $rawPath = Join-Path $evidenceRoot "$Name.log"
    Write-Fixture $rawPath ($output -join [Environment]::NewLine)
    Assert-Policy ($exitCode -eq 0) "$Name failed with exit $exitCode; see $rawPath"
    return $output -join [Environment]::NewLine
}
function Get-PreservedProof {
    $dependencyRoot = $env:L12_DEPENDENCY_CACHE
    $packageRoot = Join-Path $dependencyRoot 'nuget/packages/skiasharp.nativeassets.win32/3.119.4'
    $paths = @('win-x64', 'win-x86', 'win-arm64') | ForEach-Object { Join-Path $packageRoot "runtimes/$_/native/libSkiaSharp.pdb" }
    $paths += Join-Path $packageRoot 'skiasharp.nativeassets.win32.3.119.4.nupkg'
    $paths += $ProtectedFilePaths
    @($paths | ForEach-Object {
        $item = Get-Item -LiteralPath $_ -Force
        [pscustomobject]@{ path = $item.FullName; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
    })
}

$record = [ordered]@{
    schema = 1; status = 'running'; runId = $runId
    scope = 'Synthetic MSBuild copies and real SDK asset resolution only; no compilation, restore, deployment or existing-output deletion'
    policySha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
    scriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    sdkVersion = (& $DotnetPath --version | Out-String).Trim()
    runRoot = $runRoot; evidenceRoot = $evidenceRoot; results = @(); assertions = 0; error = $null
}
try {
    $preservedBefore = Get-PreservedProof
    # Three independent SDK-like item lists exercise both early Build filtering
    # and late Publish filtering. All fixture contents are tiny, not real PDBs.
    $fixtureItems = @{}
    foreach ($collection in @('NativeCopyLocalItems', 'RuntimeTargetsCopyLocalItems', 'PublishFixture')) {
        $xml = [Text.StringBuilder]::new()
        foreach ($arch in @('win-x64', 'win-x86', 'win-arm64')) {
            foreach ($extension in @('dll', 'pdb')) {
                $relative = "$collection/$arch/libSkiaSharp.$extension"
                $source = Join-Path $runRoot "input/$relative"
                Write-Fixture $source "synthetic-$relative"
                $null = $xml.AppendLine("<$collection Include=`"$(Escape-Xml $source)`"><NuGetPackageId>SkiaSharp.NativeAssets.Win32</NuGetPackageId><AssetType>native</AssetType><CopyLocal>true</CopyLocal><RelativeOutput>$relative</RelativeOutput></$collection>")
            }
        }
        foreach ($control in @(
            @{ name = 'other-package/libSkiaSharp.pdb'; package = 'Other.Native.Package'; type = 'native' },
            @{ name = 'own-native/libSkiaSharp.pdb'; package = ''; type = 'native' },
            @{ name = 'own/Legion12.pdb'; package = ''; type = 'runtime' },
            @{ name = 'managed/libSkiaSharp.pdb'; package = 'SkiaSharp.NativeAssets.Win32'; type = 'runtime' }
        )) {
            $relative = "$collection/$($control.name)"
            $source = Join-Path $runRoot "input/$relative"
            Write-Fixture $source "control-$relative"
            $null = $xml.AppendLine("<$collection Include=`"$(Escape-Xml $source)`"><NuGetPackageId>$($control.package)</NuGetPackageId><AssetType>$($control.type)</AssetType><CopyLocal>true</CopyLocal><RelativeOutput>$relative</RelativeOutput></$collection>")
        }
        $fixtureItems[$collection] = $xml.ToString()
    }
    $fixturePath = Join-Path $runRoot 'policy-fixture.proj'
    $fixture = @'
<Project>
  <Import Project="__POLICY__" />
  <PropertyGroup>
    <_FixturePublishBuildDepends Condition="'$(NoBuild)' != 'true'">Build</_FixturePublishBuildDepends>
  </PropertyGroup>
  <Target Name="ResolvePackageAssets">
    <ItemGroup>__NATIVE____RUNTIME__</ItemGroup>
  </Target>
  <Target Name="ResolveLockFileCopyLocalFiles" DependsOnTargets="ResolvePackageAssets">
    <ItemGroup><FixtureBuild Include="@(NativeCopyLocalItems);@(RuntimeTargetsCopyLocalItems)" /></ItemGroup>
  </Target>
  <Target Name="_ResolveCopyLocalAssetsForPublish" DependsOnTargets="ResolvePackageAssets">
    <ItemGroup>__PUBLISH__<_ResolvedCopyLocalPublishAssets Include="@(PublishFixture)" /></ItemGroup>
  </Target>
  <Target Name="Build" DependsOnTargets="ResolveLockFileCopyLocalFiles">
    <Error Condition="'$(NoBuild)' == 'true'" Text="NoBuild publish unexpectedly invoked Build" />
    <Copy SourceFiles="@(FixtureBuild)" DestinationFiles="@(FixtureBuild->'$(FixtureOutput)/build/%(RelativeOutput)')" />
  </Target>
  <Target Name="Publish" DependsOnTargets="$(_FixturePublishBuildDepends);_ResolveCopyLocalAssetsForPublish">
    <Copy SourceFiles="@(_ResolvedCopyLocalPublishAssets)" DestinationFiles="@(_ResolvedCopyLocalPublishAssets->'$(FixtureOutput)/publish/%(RelativeOutput)')" />
  </Target>
</Project>
'@
    $fixture = $fixture.Replace('__POLICY__', (Escape-Xml $policyPath)).Replace('__NATIVE__', $fixtureItems.NativeCopyLocalItems).
        Replace('__RUNTIME__', $fixtureItems.RuntimeTargetsCopyLocalItems).Replace('__PUBLISH__', $fixtureItems.PublishFixture)
    Write-Fixture $fixturePath $fixture
    foreach ($mode in @(
        @{ name = 'build-default'; target = 'Build'; include = $false; noBuild = $false },
        @{ name = 'build-optin'; target = 'Build'; include = $true; noBuild = $false },
        @{ name = 'publish-default'; target = 'Publish'; include = $false; noBuild = $false },
        @{ name = 'publish-optin'; target = 'Publish'; include = $true; noBuild = $false },
        @{ name = 'publish-nobuild-default'; target = 'Publish'; include = $false; noBuild = $true },
        @{ name = 'publish-nobuild-optin'; target = 'Publish'; include = $true; noBuild = $true }
    )) {
        $output = Join-Path $runRoot "synthetic/$($mode.name)"
        $arguments = @($fixturePath, '-nologo', '-verbosity:quiet', "-target:$($mode.target)", "-property:FixtureOutput=$output", "-property:NoBuild=$($mode.noBuild.ToString().ToLowerInvariant())")
        if ($mode.include) { $arguments += '-property:L12IncludeSkiaSharpNativeSymbols=true' }
        $null = Invoke-Probe "synthetic-$($mode.name)" $arguments
        $stage = if ($mode.target -eq 'Build') { 'build' } else { 'publish' }
        $copied = @(Get-ChildItem -LiteralPath (Join-Path $output $stage) -Recurse -File)
        $expectedCount = if ($stage -eq 'build') { if ($mode.include) { 20 } else { 14 } } else { if ($mode.include) { 10 } else { 7 } }
        Assert-Policy ($copied.Count -eq $expectedCount) "synthetic $($mode.name) exact copy count"
        $collections = if ($stage -eq 'build') { @('NativeCopyLocalItems', 'RuntimeTargetsCopyLocalItems') } else { @('PublishFixture') }
        foreach ($collection in $collections) {
            foreach ($arch in @('win-x64', 'win-x86', 'win-arm64')) {
                Assert-Policy (Test-Path -LiteralPath (Join-Path $output "$stage/$collection/$arch/libSkiaSharp.dll")) "synthetic $($mode.name) retains $collection $arch DLL"
                Assert-Policy ((Test-Path -LiteralPath (Join-Path $output "$stage/$collection/$arch/libSkiaSharp.pdb")) -eq $mode.include) "synthetic $($mode.name) symbol opt-in"
            }
            foreach ($control in @('other-package/libSkiaSharp.pdb', 'own-native/libSkiaSharp.pdb', 'own/Legion12.pdb', 'managed/libSkiaSharp.pdb')) {
                Assert-Policy (Test-Path -LiteralPath (Join-Path $output "$stage/$collection/$control")) "synthetic $($mode.name) preserves $control"
            }
        }
        if ($mode.noBuild) { Assert-Policy (-not (Test-Path -LiteralPath (Join-Path $output 'build'))) 'NoBuild did not call Build' }
        $results.Add([pscustomobject]@{ kind = 'synthetic-copy'; mode = $mode.name; copiedFiles = $copied.Count; passed = $true })
    }

    foreach ($relativeProject in @('服务端WebSocket/GrandUMIServer.csproj', 'TwelveLegions.Tests/TwelveLegions.Tests.csproj', 'TwelveLegions.Platform.Tests/TwelveLegions.Platform.Tests.csproj')) {
        $project = Join-Path $repo $relativeProject
        $projectName = [IO.Path]::GetFileNameWithoutExtension($project)
        $assets = Join-Path ([IO.Path]::GetDirectoryName($project)) 'obj/project.assets.json'
        Assert-Policy (Test-Path -LiteralPath $assets) "$projectName has existing assets; this check never restores"
        $assetsBefore = (Get-FileHash -LiteralPath $assets -Algorithm SHA256).Hash
        $resolved = @{}
        foreach ($mode in @(
            @{ name = 'build-optin'; target = 'ResolveLockFileCopyLocalFiles'; item = '_ResolvedCopyLocalBuildAssets'; include = $true; noBuild = $false; store = $false },
            @{ name = 'build-default'; target = 'ResolveLockFileCopyLocalFiles'; item = '_ResolvedCopyLocalBuildAssets'; include = $false; noBuild = $false; store = $false },
            @{ name = 'publish-optin'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $true; noBuild = $false; store = $false },
            @{ name = 'publish-default'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $false; noBuild = $false; store = $false },
            @{ name = 'publish-nobuild-default'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $false; noBuild = $true; store = $false },
            @{ name = 'publish-nobuild-optin'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $true; noBuild = $true; store = $false },
            @{ name = 'publish-store-optin'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $true; noBuild = $true; store = $true },
            @{ name = 'publish-store-default'; target = '_ResolveCopyLocalAssetsForPublish'; item = '_ResolvedCopyLocalPublishAssets'; include = $false; noBuild = $true; store = $true }
        )) {
            $probeRoot = Join-Path $runRoot "$projectName/$($mode.name)"
            $null = Assert-L12GeneratedPath -Path $probeRoot -Purpose 'real SDK assets/cache'
            $arguments = @($project, '-nologo', '-verbosity:quiet', "-target:$($mode.target)", "-getItem:$($mode.item)",
                "-property:ProjectAssetsFile=$assets", "-property:ProjectAssetsCacheFile=$probeRoot/project.assets.cache",
                "-property:BaseIntermediateOutputPath=$probeRoot/obj/", "-property:MSBuildProjectExtensionsPath=$probeRoot/obj/",
                "-property:OutputPath=$probeRoot/bin/", "-property:PublishDir=$probeRoot/publish/", '-property:BuildProjectReferences=false',
                "-property:NoBuild=$($mode.noBuild.ToString().ToLowerInvariant())", "-property:PreserveStoreLayout=$($mode.store.ToString().ToLowerInvariant())")
            if ($mode.include) { $arguments += '-property:L12IncludeSkiaSharpNativeSymbols=true' }
            $raw = Invoke-Probe "real-$projectName-$($mode.name)" $arguments
            $parsed = $raw | ConvertFrom-Json
            $items = @($parsed.Items.PSObject.Properties[$mode.item].Value)
            $resolved[$mode.name] = $items
            $symbols = @($items | Where-Object { Test-ExcludedSymbol $_ })
            Assert-Policy ($symbols.Count -eq $(if ($mode.include) { 3 } else { 0 })) "$projectName $($mode.name) has expected symbol count"
            $nativeDlls = @($items | Where-Object {
                (Get-Metadata $_ 'NuGetPackageId') -ieq 'SkiaSharp.NativeAssets.Win32' -and
                [IO.Path]::GetFileName((Get-Metadata $_ 'Identity')) -ieq 'libSkiaSharp.dll'
            })
            Assert-Policy ($nativeDlls.Count -eq 3) "$projectName $($mode.name) retains all three native DLLs"
            $results.Add([pscustomobject]@{ kind = 'real-sdk-resolution'; project = $relativeProject; assetsSha256 = $assetsBefore; mode = $mode.name; items = $items.Count; symbols = $symbols.Count; nativeDlls = $nativeDlls.Count; passed = $true })
        }
        foreach ($pair in @(
            @('build-optin', 'build-default'), @('publish-optin', 'publish-default'),
            @('publish-nobuild-optin', 'publish-nobuild-default'), @('publish-store-optin', 'publish-store-default')
        )) {
            $kept = Get-AssetKeys @($resolved[$pair[0]] | Where-Object { -not (Test-ExcludedSymbol $_) })
            $actual = Get-AssetKeys $resolved[$pair[1]]
            Assert-Policy (($kept -join "`n") -ceq ($actual -join "`n")) "$projectName $($pair[1]) changes no non-target asset/metadata"
        }
        Assert-Policy ((Get-FileHash -LiteralPath $assets -Algorithm SHA256).Hash -eq $assetsBefore) "$projectName existing assets file unchanged"
    }
    $preservedAfter = Get-PreservedProof
    Assert-Policy (($preservedBefore | ConvertTo-Json -Depth 4 -Compress) -ceq ($preservedAfter | ConvertTo-Json -Depth 4 -Compress)) 'NuGet originals and optional pinned files unchanged'
    $record.preserved = $preservedAfter
    $record.status = 'passed'
} catch {
    $record.status = 'failed'
    $record.error = $_.Exception.Message
    throw
} finally {
    $record.results = @($results)
    $record.assertions = $assertions
    $record.completedUtc = [DateTime]::UtcNow.ToString('o')
    Write-Fixture (Join-Path $evidenceRoot 'result.json') ($record | ConvertTo-Json -Depth 8)
}
[pscustomobject]@{ status = $record.status; assertions = $assertions; checks = $results.Count; result = (Join-Path $evidenceRoot 'result.json') } | ConvertTo-Json -Compress
