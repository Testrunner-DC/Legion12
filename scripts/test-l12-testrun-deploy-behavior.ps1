[CmdletBinding()]
param([string]$FixtureBase = "")

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-Utf8NoBom {
    param([string]$Path, [AllowEmptyString()][string]$Content)
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertTo-MsysPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path).Replace('\', '/')
    if ($full -match '^([A-Za-z]):/(.*)$') { return "/$($matches[1].ToLowerInvariant())/$($matches[2])" }
    return $full
}

function Invoke-NativeCapture {
    param([string]$Executable, [string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ ExitCode = $process.ExitCode; Output = "$stdout$stderr" }
}

function New-FakeCommand {
    param([string]$Directory, [string]$Name, [string]$Body)
    Write-Utf8NoBom (Join-Path $Directory $Name) "#!/usr/bin/env sh`n$Body"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$windowsDeploy = Join-Path $repoRoot "ops\windows\deploy-l12-testrun.ps1"
$serverDeploy = Join-Path $repoRoot "ops\server\deploy-l12-testrun-release.sh"
$bootstrap = Join-Path $repoRoot "ops\server\bootstrap-l12-testrun.sh"
$service = Join-Path $repoRoot "ops\server\legion12-testrun.service"
$httpNginx = Join-Path $repoRoot "ops\server\legion12-testrun-http.nginx"
$tlsNginx = Join-Path $repoRoot "ops\server\legion12-testrun.nginx"
$pathNginx = Join-Path $repoRoot "ops\server\legion12-testrun-path.nginx"
$envExample = Join-Path $repoRoot "ops\server\legion12-testrun.env.example"
$healthVerifier = Join-Path $repoRoot "ops\server\verify-l12-health.mjs"
$powerShell = Get-Command pwsh -ErrorAction SilentlyContinue
if (-not $powerShell) { $powerShell = Get-Command powershell -ErrorAction Stop }
$git = Get-Command git -ErrorAction Stop
$gitRoot = Split-Path (Split-Path $git.Source -Parent) -Parent
$shell = Join-Path $gitRoot "usr\bin\sh.exe"
$node = (Get-Command node -ErrorAction Stop).Source
$python = (Get-Command python -ErrorAction Stop).Source
$tar = (Get-Command tar -ErrorAction Stop).Source

$base = if ([string]::IsNullOrWhiteSpace($FixtureBase)) { [IO.Path]::GetTempPath() } else { (Resolve-Path -LiteralPath $FixtureBase).Path }
$base = [IO.Path]::GetFullPath($base).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fixture = Join-Path $base "l12-testrun-deploy-behavior-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$suitePassed = $false

try {
    $logicalProbe = Join-Path $fixture "logical-hardlink-probe"
    New-Item -ItemType Directory -Path $logicalProbe -Force | Out-Null
    $logicalSource = Join-Path $logicalProbe "source.bin"
    $logicalLink = Join-Path $logicalProbe "hydrated.bin"
    [IO.File]::WriteAllBytes($logicalSource, [byte[]]::new(4096))
    New-Item -ItemType HardLink -Path $logicalLink -Target $logicalSource | Out-Null
    $logicalBytes = (Get-ChildItem -LiteralPath $logicalProbe -File | Measure-Object -Property Length -Sum).Sum
    Assert-True ($logicalBytes -eq 8192) "Logical accounting did not count a hydrated hard-link pathname again."
    $bootstrapSource = Get-Content -LiteralPath $bootstrap -Raw
    $dailySource = Get-Content -LiteralPath $serverDeploy -Raw
    $windowsSource = Get-Content -LiteralPath $windowsDeploy -Raw
    $serviceSource = Get-Content -LiteralPath $service -Raw
    $httpSource = Get-Content -LiteralPath $httpNginx -Raw
    $tlsSource = Get-Content -LiteralPath $tlsNginx -Raw
    $pathSource = Get-Content -LiteralPath $pathNginx -Raw
    $activatorSource = Get-Content -LiteralPath (Join-Path $repoRoot "ops\server\activate-l12-testrun-path.sh") -Raw
    $envSource = Get-Content -LiteralPath $envExample -Raw

    foreach ($contract in @(
        'failure_stage="post-success-storage-cleanup"',
        'converge_testrun_storage "$release_dir"',
        'rollback_count < 1',
        'program_budget_bytes=$((512 * 1024 * 1024))',
        'data_budget_bytes=$((256 * 1024 * 1024))',
        'temporary_budget_bytes=$((128 * 1024 * 1024))',
        'total_budget_bytes=$((1024 * 1024 * 1024))',
        'storage cleanup retained explicitly PINNED release:',
        'storage cleanup retained failed release evidence:',
        'storage cleanup refused: release may be incomplete or ownership is unknown:',
        '(( uncertain == 0 )) || return 1',
        'newWebTwoPrefixes=${new_web_growth}',
        'accounting=logical',
        'release_logical + hydrate_logical',
        'production_web + testrun_web + hydrate_logical',
        'storage cleanup refused: failure receipt enumeration failed',
        'storage cleanup refused: failure receipt is partial, unsafe, or unowned:',
        'storage cleanup refused: failure receipt is incomplete or cannot be parsed exactly once:',
        'storage cleanup refused: failure receipt read failed during ownership match:',
        'L12_TESTRUN_PRUNE_RUNTIME_BACKUPS:-0',
        'runtime_backup_checksum="${runtime_backup}.sha256"',
        'storage cleanup retained releases:',
        'storage cleanup kept unproven incoming artifact:',
        'card asset cleanup skipped:'
        'find "${static_web_assets_dir}/${public_prefix}" -type d -exec chmod 0755 {} +'
        'chmod 0755 "$prefix_path"'
        'for component in "${prefix_components[@]}"'
    )) {
        Assert-True ($dailySource.Contains($contract)) "Missing minimal testrun retention contract: $contract"
    }

    Assert-True (-not $bootstrapSource.Contains('/etc/legion12-test.env')) "Bootstrap still reads the production environment file."
    Assert-True ($bootstrapSource.Contains('openssl rand -hex 32')) "Bootstrap does not generate an independent admin secret."
    Assert-True ($bootstrapSource.Contains('L12_EMAIL_FEATURE_ENABLED=false')) "Bootstrap does not force email off."
    Assert-True ($bootstrapSource.Contains('L12_PUBLIC_BASE_URL=${public_base}')) "Bootstrap does not bind the testrun public base URL."
    Assert-True ($bootstrapSource.Contains('L12_TESTRUN_MATCH_STORAGE=ephemeral')) "Bootstrap does not enforce ephemeral match storage."
    Assert-True ($dailySource.Contains('location = /testrun/ws') -and $dailySource.Contains('dist-testrun')) "Daily deploy does not require the mounted test path."
    Assert-True (-not $dailySource.Contains('systemctl reload nginx')) "Daily deploy reloads Nginx."
    Assert-True (-not $dailySource.Contains('legion12-testrun-http')) "Daily deploy references the bootstrap HTTP site."
    foreach ($source in @($bootstrapSource, $dailySource, $windowsSource)) {
        Assert-True (-not $source.Contains('127.0.0.1:8083')) "A testrun deploy script references the production port."
        Assert-True (-not $source.Contains('legion12-test.service')) "A testrun deploy script references the production service."
        Assert-True (-not $source.Contains('/opt/legion12-runtime')) "A testrun deploy script references the production runtime."
        Assert-True (-not $source.Contains('/opt/legion12-static')) "A testrun deploy script references the production static cache."
    }
    Assert-True ($serviceSource.Contains('ExecStart=/usr/local/bin/dotnet /opt/legion12-testrun/publish/GrandUMIServer.dll 8084')) "Service does not use the isolated port."
    Assert-True ($serviceSource.Contains('Environment=L12_PRIVATE_DECK_OBJECT_PERSISTENCE=true')) "Testrun must exercise the approved private-deck object path."
    Assert-True ($serviceSource.Contains('InaccessiblePaths=/opt/legion12-test /opt/legion12-runtime /opt/legion12-static')) "Service does not hide production release, runtime, and static paths."
    Assert-True ($serviceSource.Contains('CPUWeight=10') -and $serviceSource.Contains('IOWeight=10') -and $serviceSource.Contains('OOMScoreAdjust=750')) "Service lacks low-priority resource isolation."
    Assert-True (-not $serviceSource.Contains('CPUQuota=')) "Service still imposes a hard CPU quota instead of weight-based priority."
    Assert-True ($serviceSource.Contains('MemoryHigh=768M') -and $serviceSource.Contains('MemoryMax=896M')) "Service memory bounds did not replace the 512M OOM-prone limit."
    Assert-True ($httpSource.Contains("return 503 'testrun TLS bootstrap in progress")) "HTTP bootstrap exposes more than ACME and a 503 guard."
    Assert-True (-not $httpSource.Contains('proxy_pass')) "HTTP bootstrap exposes the application over plaintext."
    Assert-True (-not $tlsSource.Contains('auth_basic')) "Public testrun TLS site enables Basic Auth."
    Assert-True ($tlsSource.Contains('location ^~ /assets/') -and $tlsSource.Contains('try_files $uri =404;') -and $tlsSource.Contains('max-age=31536000, immutable')) "Standalone testrun assets are not immutable strict-404 resources."
    Assert-True (-not $pathSource.Contains('auth_basic') -and $pathSource.Contains('proxy_pass http://127.0.0.1:8084/ws;') -and $pathSource.Contains('dist-testrun')) "Mounted test path is not isolated or public."
    Assert-True ($pathSource.Contains('location ^~ /testrun/assets/') -and $pathSource.Contains('root /opt/legion12-testrun-web-assets;') -and $pathSource.Contains('try_files $uri =404;')) "Mounted testrun assets can still fall back to HTML."
    Assert-True ($pathSource.Contains('location = /testrun/index.html') -and $pathSource.Contains('Cache-Control "no-cache"')) "Mounted testrun HTML is not revalidated."
    $pathNewsRegex = [regex]::Match($pathSource, 'location\s+~\s+\^/testrun/news/\[\^/\]\+/\?\$\s*\{(?<body>.*?)\}', [Text.RegularExpressions.RegexOptions]::Singleline)
    Assert-True ($pathNewsRegex.Success) "Mounted test path lacks the article share route."
    Assert-True ($pathNewsRegex.Groups['body'].Value.Contains('rewrite ^ /_l12/share-page break;') -and $pathNewsRegex.Groups['body'].Value.Contains('proxy_pass http://127.0.0.1:8084;') -and -not $pathNewsRegex.Groups['body'].Value.Contains('proxy_pass http://127.0.0.1:8084/_l12/share-page;')) "Regex article route uses an invalid proxy_pass URI."
    Assert-True ($activatorSource.Contains('server_name legion-12.com;\n') -and -not $activatorSource.Contains('marker = "    location / {"')) "Path activator does not target the unique production HTTPS host block."
    Assert-True ($envSource.Contains('L12_EMAIL_FEATURE_ENABLED=false') -and $envSource.Contains('L12_PUBLIC_BASE_URL=https://legion-12.com/testrun') -and $envSource.Contains('L12_TESTRUN_MATCH_STORAGE=ephemeral')) "Environment example is not fail-closed."
    Assert-True ($windowsSource.Contains('StrictHostKeyChecking=yes') -and $windowsSource.Contains('HostName=$($Endpoint.Address)') -and $windowsSource.Contains('HostKeyAlias=$($Endpoint.HostKeyAlias)')) "Windows entry does not pin strict SSH trust."

    $invalidTarget = Invoke-NativeCapture $powerShell.Source @("-NoProfile", "-File", $windowsDeploy, "-Server", "root@example.com", "-ArtifactManifest", (Join-Path $fixture "missing.json"), "-ValidateArtifactOnly")
    Assert-True ($invalidTarget.ExitCode -ne 0 -and $invalidTarget.Output.Contains('Refusing non-testrun target')) "Windows entry accepted an arbitrary host."

    $commitA = "a" * 40
    $commitB = "b" * 40
    $assetHash = "c" * 64
    $artifactRoot = Join-Path $fixture "artifacts"
    $releaseRoot = Join-Path $artifactRoot "release"
    $assetRoot = Join-Path $artifactRoot "assets"
    New-Item -ItemType Directory -Path (Join-Path $releaseRoot "publish"), (Join-Path $releaseRoot "opcgpro-vue\dist"), (Join-Path $releaseRoot "opcgpro-vue\dist\assets"), (Join-Path $releaseRoot "opcgpro-vue\dist-testrun"), (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets"), (Join-Path $releaseRoot "scripts"), (Join-Path $assetRoot "cards") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") $commitB
    Write-Utf8NoBom (Join-Path $releaseRoot "publish\GrandUMIServer.dll") "binary"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist\index.html") "html"
    New-Item -ItemType Directory -Path (Join-Path $releaseRoot "opcgpro-vue\dist\assets") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist\assets\shared.txt") "shared static asset"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist\assets\Page-new.js") "export const release = 'new'"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist\assets\Page-new.css") ".new{display:block}"
    New-Item -ItemType Directory -Path (Join-Path $releaseRoot "opcgpro-vue\dist\assets\l12\special\round") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist\assets\l12\special\round\Round_S01-0216-卡诺匹斯箱.png") "unicode asset"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\index.html") "testrun html"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\Page-new.js") "export const release = 'testrun-new'"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\Page-new.css") ".testrun-new{display:block}"
    New-Item -ItemType Directory -Path (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\l12\special\round") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\l12\special\round\Round_S01-0216-卡诺匹斯箱.png") "unicode testrun asset"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\testrun-shared-files.txt") "assets/shared.txt`n"
    Write-Utf8NoBom (Join-Path $releaseRoot "scripts\ws-smoke.mjs") "// probe"
    Write-Utf8NoBom (Join-Path $assetRoot "card-assets.manifest.json") "{}"
    Write-Utf8NoBom (Join-Path $assetRoot "card-assets.preload.json") "{}"
    $releaseArchive = Join-Path $artifactRoot "l12-release-$commitB.tar.gz"
    $assetArchive = Join-Path $artifactRoot "l12-card-assets-$assetHash.tar.gz"
    & $tar -czf $releaseArchive -C $releaseRoot .
    Assert-True ($LASTEXITCODE -eq 0) "Release fixture archive creation failed."
    & $tar -czf $assetArchive -C $assetRoot .
    Assert-True ($LASTEXITCODE -eq 0) "Card asset fixture archive creation failed."
    $manifestPath = Join-Path $artifactRoot "l12-release-$commitB.json"
    $manifest = [ordered]@{
        schema = 3; commit = $commitB; releaseArchive = $releaseArchive
        releaseSha256 = (Get-FileHash $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        cardAssetsHash = $assetHash; cardAssetsArchive = $assetArchive
        cardAssetsSha256 = (Get-FileHash $assetArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Write-Utf8NoBom $manifestPath ($manifest | ConvertTo-Json)
    $validArtifact = Invoke-NativeCapture $powerShell.Source @("-NoProfile", "-File", $windowsDeploy, "-ArtifactManifest", $manifestPath, "-ValidateArtifactOnly")
    Assert-True ($validArtifact.ExitCode -eq 0) "Valid target-bound artifact was rejected: $($validArtifact.Output)"
    $manifest.releaseSha256 = "d" * 64
    Write-Utf8NoBom $manifestPath ($manifest | ConvertTo-Json)
    $badHash = Invoke-NativeCapture $powerShell.Source @("-NoProfile", "-File", $windowsDeploy, "-ArtifactManifest", $manifestPath, "-ValidateArtifactOnly")
    Assert-True ($badHash.ExitCode -ne 0 -and $badHash.Output.Contains('Release archive SHA256 differs')) "Tampered release hash was accepted."
    $manifest.releaseSha256 = (Get-FileHash $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom (Join-Path $releaseRoot "unexpected.txt") "forbidden"
    & $tar -czf $releaseArchive -C $releaseRoot .
    $manifest.releaseSha256 = (Get-FileHash $releaseArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom $manifestPath ($manifest | ConvertTo-Json)
    $badMember = Invoke-NativeCapture $powerShell.Source @("-NoProfile", "-File", $windowsDeploy, "-ArtifactManifest", $manifestPath, "-ValidateArtifactOnly")
    Assert-True ($badMember.ExitCode -ne 0 -and $badMember.Output.Contains('unexpected root')) "Archive with an unexpected root was accepted."

    Remove-Item -LiteralPath (Join-Path $releaseRoot "unexpected.txt") -Force
    & $tar -czf $releaseArchive -C $releaseRoot .
    $root = Join-Path $fixture "server"
    $rootPosix = ConvertTo-MsysPath $root
    $oldRelease = Join-Path $root "opt\legion12-testrun-releases\$commitA-old"
    $runtime = Join-Path $root "opt\legion12-testrun-runtime"
    $incoming = Join-Path $root "opt\legion12-testrun-deployment\incoming"
    $fakeBin = Join-Path $root "fake-bin"
    $cachedAssets = Join-Path $root "opt\legion12-testrun-static\card-assets\$assetHash"
    $pathSite = Join-Path $root "etc\nginx\snippets\legion12-testrun-path.conf"
    New-Item -ItemType Directory -Path (Join-Path $oldRelease "publish"), (Join-Path $oldRelease "opcgpro-vue\dist\assets"), (Join-Path $oldRelease "opcgpro-vue\dist-testrun\assets"), (Join-Path $oldRelease "scripts"), $runtime, $incoming, $fakeBin, $cachedAssets, (Split-Path $pathSite -Parent), (Join-Path $root "usr\local\libexec"), (Join-Path $root "run\lock") -Force | Out-Null
    $oldReleasePosix = ConvertTo-MsysPath $oldRelease
    $runtimePosix = ConvertTo-MsysPath $runtime
    Write-Utf8NoBom (Join-Path $oldRelease ".deployment-commit") "$commitA`n"
    Write-Utf8NoBom (Join-Path $oldRelease "publish\runtime") "$runtimePosix`n"
    Write-Utf8NoBom (Join-Path $oldRelease "scripts\ws-smoke.mjs") "// old probe"
    Write-Utf8NoBom (Join-Path $oldRelease "opcgpro-vue\dist\assets\Page-old.js") "export const release = 'old'"
    Write-Utf8NoBom (Join-Path $oldRelease "opcgpro-vue\dist\assets\Page-old.css") ".old{display:block}"
    Write-Utf8NoBom (Join-Path $oldRelease "opcgpro-vue\dist-testrun\assets\Page-old.js") "export const release = 'testrun-old'"
    Write-Utf8NoBom (Join-Path $oldRelease "opcgpro-vue\dist-testrun\assets\Page-old.css") ".testrun-old{display:block}"
    Write-Utf8NoBom (Join-Path $root "opt\legion12-testrun") "$oldReleasePosix`n"
    Write-Utf8NoBom (Join-Path $cachedAssets "card-assets.manifest.json") "{}"
    Write-Utf8NoBom $pathSite "location = /testrun/ws {`nproxy_pass http://127.0.0.1:8084/ws;`n}`nlocation ^~ /testrun/assets/ {`nroot /opt/legion12-testrun-web-assets;`ntry_files `$uri =404;`n}`nlocation /testrun/ {`nalias /opt/legion12-testrun/opcgpro-vue/dist-testrun/;`n}`n"
    Write-Utf8NoBom (Join-Path $root "etc\legion12-testrun.env") ("L12_ADMIN_PASSWORD=" + ("e" * 64) + "`nL12_EMAIL_FEATURE_ENABLED=false`nL12_PUBLIC_BASE_URL=https://legion-12.com/testrun`nL12_TESTRUN_MATCH_STORAGE=ephemeral`nL12_SMTP_HOST=`nL12_SMTP_PORT=`nL12_SMTP_USERNAME=`nL12_SMTP_PASSWORD=`nL12_SMTP_FROM_ADDRESS=`nL12_SMTP_FROM_NAME=`nL12_SMTP_ENABLE_SSL=`nL12_ENABLE_SECOND_APPROVER_BOOTSTRAP=false`nL12_SECOND_APPROVER_BOOTSTRAP_TOKEN=`n")
    Copy-Item $healthVerifier (Join-Path $root "usr\local\libexec\verify-legion12-testrun-health.mjs")
    Copy-Item $releaseArchive (Join-Path $incoming "l12-testrun-release-$commitB.tar.gz")
    Write-Utf8NoBom (Join-Path $root "service.state") "running`n"
    Write-Utf8NoBom (Join-Path $runtime "sentinel.txt") "preserve"

    New-FakeCommand $fakeBin "id" 'if [ "${1:-}" = "-u" ]; then printf "0\n"; fi; exit 0'
    New-FakeCommand $fakeBin "flock" 'exit 0'
    New-FakeCommand $fakeBin "seq" 'exit 0'
    New-FakeCommand $fakeBin "python3" 'exec "$L12_TEST_PYTHON" "$@"'
    New-FakeCommand $fakeBin "sha256sum" @'
node -e "const fs=require('node:fs'),crypto=require('node:crypto');const p=process.argv[1];process.stdout.write(crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex')+'  '+p+'\n')" "$1"
'@
    New-FakeCommand $fakeBin "tar" 'exec "$L12_TEST_TAR" "$@"'
    New-FakeCommand $fakeBin "install" 'if [ "${1:-}" = "-m" ]; then shift 2; fi; if [ "$#" -eq 2 ]; then cp "$1" "$2"; fi; exit 0'
    New-FakeCommand $fakeBin "find" @'
if [ "${L12_TEST_FAILURE_FIND_STATUS:-0}" != "0" ] && [ "${1:-}" = "$L12_TEST_FAILURE_DIR" ] && [ "${2:-}" = "-mindepth" ]; then exit "$L12_TEST_FAILURE_FIND_STATUS"; fi
exec /usr/bin/find "$@"
'@
    New-FakeCommand $fakeBin "grep" @'
if [ "${L12_TEST_FAILURE_GREP_STATUS:-0}" != "0" ] && [ "${1:-}" = "-Fqx" ]; then exit "$L12_TEST_FAILURE_GREP_STATUS"; fi
exec /usr/bin/grep "$@"
'@
    New-FakeCommand $fakeBin "systemctl" @'
printf 'systemctl %s\n' "$*" >> "$L12_TEST_COMMAND_LOG"
case "${1:-}" in
  cat|is-enabled) exit 0 ;;
  is-active) [ "$(tr -d '\r\n' < "$L12_TEST_SERVICE_STATE")" = "running" ] && exit 0; exit 3 ;;
  stop) printf 'stopped\n' > "$L12_TEST_SERVICE_STATE"; exit 0 ;;
  start) printf 'running\n' > "$L12_TEST_SERVICE_STATE"; exit 0 ;;
esac
exit 0
'@
    New-FakeCommand $fakeBin "nginx" 'printf "nginx %s\n" "$*" >> "$L12_TEST_COMMAND_LOG"; exit 0'
    New-FakeCommand $fakeBin "runuser" 'exit 0'
    New-FakeCommand $fakeBin "chmod" 'exit 0'
    New-FakeCommand $fakeBin "chown" 'exit 0'
    New-FakeCommand $fakeBin "stat" 'if [ "${1:-}" = "-c" ] && [ "${2:-}" = "%s" ]; then wc -c < "$3" | tr -d " "; exit 0; fi; exit 2'
    New-FakeCommand $fakeBin "ln" 'if [ "${1:-}" = "-s" ]; then target="$2"; link="$3"; printf "%s\n" "$target" > "$link"; else cp "$1" "$2"; fi'
    New-FakeCommand $fakeBin "readlink" @'
for last; do :; done
if [ -d "$last" ]; then printf '%s\n' "$last"; elif [ -f "$last" ]; then tr -d '\r\n' < "$last"; else exit 1; fi
'@
    New-FakeCommand $fakeBin "timeout" 'printf "timeout %s\n" "$*" >> "$L12_TEST_COMMAND_LOG"; exit 0'
    New-FakeCommand $fakeBin "curl" @'
for url; do :; done
printf 'curl %s\n' "$url" >> "$L12_TEST_COMMAND_LOG"
case "$url" in
  */health)
    target="$(tr -d '\r\n' < "$L12_TEST_ACTIVE")"
    served="$(tr -d '\r\n' < "$target/.deployment-commit")"
    status=ok
    if [ "$served" = "$L12_TEST_NEW_COMMIT" ] && [ "${L12_TEST_FORCE_NEW_HEALTH_OK:-0}" != "1" ]; then status=degraded; fi
    printf '{"status":"%s","maintenance":false,"service":"twelve-legions","serverVersion":"%s","engineVersion":"l12-engine/%s"}\n' "$status" "$served" "$served" ;;
esac
exit 0
'@
    $commandLog = Join-Path $root "commands.log"
    $environment = @{
        L12_TESTRUN_DEPLOY_TEST_MODE = "1"; L12_TESTRUN_DEPLOY_TEST_ROOT = $rootPosix
        L12_TESTRUN_DEPLOY_HEALTH_VERIFIER = "$rootPosix/usr/local/libexec/verify-legion12-testrun-health.mjs"
        L12_TESTRUN_DEPLOY_HEALTH_ATTEMPTS = "1"; L12_TESTRUN_DEPLOY_HEALTH_DELAY_SECONDS = "0"
        L12_TESTRUN_DEPLOY_LOCKED = "1"; L12_TESTRUN_DEPLOY_SKIP_CARD_AUDIT = "1"
        L12_TEST_FAKE_BIN = (ConvertTo-MsysPath $fakeBin); L12_TEST_NODE_DIR = (ConvertTo-MsysPath (Split-Path $node -Parent))
        L12_TEST_PYTHON = (ConvertTo-MsysPath $python)
        L12_TEST_TAR = (ConvertTo-MsysPath $tar)
        L12_TEST_COMMAND_LOG = (ConvertTo-MsysPath $commandLog); L12_TEST_SERVICE_STATE = (ConvertTo-MsysPath (Join-Path $root "service.state"))
        L12_TEST_ACTIVE = "$rootPosix/opt/legion12-testrun"; L12_TEST_NEW_COMMIT = $commitB
        L12_TEST_FORCE_NEW_HEALTH_OK = "0"
        L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_DATA_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_TEMP_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_RELEASE_LOGICAL_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_PREVIOUS_WEB_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_NEW_WEB_BYTES = ""
        L12_TESTRUN_DEPLOY_TEST_RUNTIME_BACKUP_BYTES = ""
        L12_TEST_FAILURE_DIR = "$rootPosix/opt/legion12-testrun-deployment/failures"
        L12_TEST_FAILURE_FIND_STATUS = "0"
        L12_TEST_FAILURE_GREP_STATUS = "0"
    }
    $saved = @{}
    foreach ($entry in $environment.GetEnumerator()) { $saved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $archiveServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$commitB.tar.gz"
        $launcher = 'export PATH="$L12_TEST_FAKE_BIN:/usr/bin:/bin:$L12_TEST_NODE_DIR"; /usr/bin/sh "$1" "${@:2}"'
        foreach ($budgetFault in @(
            @{ Name = "program-over-512mib"; Values = @{ L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES = [string](512MB + 1) }; Reason = "program budget exceeds 512 MiB" },
            @{ Name = "data-over-256mib"; Values = @{ L12_TESTRUN_DEPLOY_TEST_DATA_BYTES = [string](256MB + 1) }; Reason = "data budget exceeds 256 MiB" },
            @{ Name = "temporary-over-128mib"; Values = @{ L12_TESTRUN_DEPLOY_TEST_TEMP_BYTES = [string](128MB + 1) }; Reason = "temporary budget exceeds 128 MiB" },
            @{ Name = "near-512mib-new-two-prefix-web"; Values = @{
                L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES = [string](512MB - 1); L12_TESTRUN_DEPLOY_TEST_RELEASE_LOGICAL_BYTES = "0"
                L12_TESTRUN_DEPLOY_TEST_PREVIOUS_WEB_BYTES = "0"; L12_TESTRUN_DEPLOY_TEST_NEW_WEB_BYTES = "2"; L12_TESTRUN_DEPLOY_TEST_RUNTIME_BACKUP_BYTES = "0"
            }; Reason = "program budget exceeds 512 MiB" },
            @{ Name = "previous-web-cache-missing"; Values = @{
                L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES = [string](512MB - 1); L12_TESTRUN_DEPLOY_TEST_RELEASE_LOGICAL_BYTES = "0"
                L12_TESTRUN_DEPLOY_TEST_PREVIOUS_WEB_BYTES = "2"; L12_TESTRUN_DEPLOY_TEST_NEW_WEB_BYTES = "0"; L12_TESTRUN_DEPLOY_TEST_RUNTIME_BACKUP_BYTES = "0"
            }; Reason = "program budget exceeds 512 MiB" },
            @{ Name = "hydrated-hardlink-logical-byte"; Values = @{
                L12_TESTRUN_DEPLOY_TEST_PROGRAM_BYTES = [string](512MB - 1); L12_TESTRUN_DEPLOY_TEST_RELEASE_LOGICAL_BYTES = "2"
                L12_TESTRUN_DEPLOY_TEST_PREVIOUS_WEB_BYTES = "0"; L12_TESTRUN_DEPLOY_TEST_NEW_WEB_BYTES = "0"; L12_TESTRUN_DEPLOY_TEST_RUNTIME_BACKUP_BYTES = "0"
            }; Reason = "program budget exceeds 512 MiB" }
        )) {
            foreach ($entry in $budgetFault.Values.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
            try {
                $fault = Invoke-NativeCapture $shell @("-c", $launcher, $budgetFault.Name, (ConvertTo-MsysPath $serverDeploy), "deploy", $commitB, (Get-FileHash (Join-Path $incoming "l12-testrun-release-$commitB.tar.gz") -Algorithm SHA256).Hash.ToLowerInvariant(), $archiveServerPath, $assetHash, "-", "-")
            }
            finally { foreach ($entry in $budgetFault.Values.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, "", "Process") } }
            Assert-True ($fault.ExitCode -ne 0 -and $fault.Output.Contains($budgetFault.Reason)) "$($budgetFault.Name) did not fail closed: $($fault.Output)"
            $faultCommands = if (Test-Path -LiteralPath $commandLog) { Get-Content -LiteralPath $commandLog -Raw } else { "" }
            Assert-True (-not $faultCommands.Contains("systemctl stop")) "$($budgetFault.Name) reached service stop."
            Assert-True ((Get-Content -LiteralPath (Join-Path $runtime "sentinel.txt") -Raw) -eq "preserve") "$($budgetFault.Name) changed testrun runtime."
            Assert-True (Test-Path -LiteralPath (Join-Path $incoming "l12-testrun-release-$commitB.tar.gz") -PathType Leaf) "$($budgetFault.Name) deleted incoming evidence."
            if (Test-Path -LiteralPath $commandLog) { Remove-Item -LiteralPath $commandLog -Force }
        }
        $rollback = Invoke-NativeCapture $shell @("-c", $launcher, "testrun-test", (ConvertTo-MsysPath $serverDeploy), "deploy", $commitB, (Get-FileHash (Join-Path $incoming "l12-testrun-release-$commitB.tar.gz") -Algorithm SHA256).Hash.ToLowerInvariant(), $archiveServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($rollback.ExitCode -ne 0) "Injected new-release health failure unexpectedly succeeded."
    Assert-True ((Get-Content -LiteralPath (Join-Path $root "opt\legion12-testrun") -Raw).Trim() -eq $oldReleasePosix) "Failure did not restore the old testrun release."
    Assert-True ((Get-Content -LiteralPath (Join-Path $root "service.state") -Raw).Trim() -eq "running") "Verified old testrun release was not restarted."
    Assert-True ((Get-Content -LiteralPath (Join-Path $runtime "sentinel.txt") -Raw) -eq "preserve") "Failure changed existing testrun runtime data."
    $sharedWebAssets = Join-Path $root "opt\legion12-testrun-static\web-assets"
    Assert-True (Test-Path -LiteralPath (Join-Path $sharedWebAssets "assets\Page-old.js") -PathType Leaf) "Rollback path did not stage the previous standalone JS asset: $($rollback.Output)"
    Assert-True ((Get-Content -LiteralPath (Join-Path $sharedWebAssets "assets\Page-old.js") -Raw) -eq "export const release = 'old'") "Rollback path did not preserve the previous standalone JS asset."
    Assert-True ((Get-Content -LiteralPath (Join-Path $sharedWebAssets "testrun\assets\Page-old.css") -Raw) -eq ".testrun-old{display:block}") "Rollback path did not preserve the previous mounted CSS asset."
    Assert-True ((Get-Content -LiteralPath (Join-Path $sharedWebAssets "testrun\assets\Page-new.js") -Raw) -eq "export const release = 'testrun-new'") "New mounted JS asset was not atomically staged before the switch."
    Assert-True (Test-Path -LiteralPath (Join-Path $sharedWebAssets "testrun\assets\l12\special\round\Round_S01-0216-卡诺匹斯箱.png") -PathType Leaf) "Unicode web asset path was not installed safely."
    Assert-True (Test-Path -LiteralPath $commandLog -PathType Leaf) "Server rollback fixture did not reach command execution: $($rollback.Output)"
    $commands = Get-Content -LiteralPath $commandLog -Raw
    Assert-True (-not $commands.Contains('reload nginx')) "Daily rollback touched Nginx state."
    Assert-True (-not $commands.Contains('8083') -and -not $commands.Contains('legion12-test.service')) "Daily rollback touched a production port or service."

    $commitC = "c" * 40
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") "$commitC`n"
    $successArchive = Join-Path $incoming "l12-testrun-release-$commitC.tar.gz"
    & $tar -czf $successArchive -C $releaseRoot .
    $releaseBase = Join-Path $root "opt\legion12-testrun-releases"
    $expiredCommit = "d" * 40
    $expiredRelease = Join-Path $releaseBase "$expiredCommit-expired"
    New-Item -ItemType Directory -Path (Join-Path $expiredRelease "opcgpro-vue\dist\assets"), (Join-Path $expiredRelease "opcgpro-vue\dist-testrun\assets") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $expiredRelease ".deployment-commit") "$expiredCommit`n"
    Write-Utf8NoBom (Join-Path $expiredRelease "opcgpro-vue\dist\assets\expired.js") "expired"
    Write-Utf8NoBom (Join-Path $expiredRelease "opcgpro-vue\dist-testrun\assets\expired.js") "expired"
    (Get-Item -LiteralPath $expiredRelease).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-10)
    $pinnedCommit = "5" * 40
    $pinnedRelease = Join-Path $releaseBase "$pinnedCommit-pinned"
    New-Item -ItemType Directory -Path (Join-Path $pinnedRelease "opcgpro-vue\dist\assets"), (Join-Path $pinnedRelease "opcgpro-vue\dist-testrun\assets") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $pinnedRelease ".deployment-commit") "$pinnedCommit`n"
    Write-Utf8NoBom (Join-Path $pinnedRelease ".PINNED") "operator evidence hold`n"
    Write-Utf8NoBom (Join-Path $pinnedRelease "opcgpro-vue\dist\assets\pinned.js") "pinned"
    Write-Utf8NoBom (Join-Path $pinnedRelease "opcgpro-vue\dist-testrun\assets\pinned.js") "pinned"
    (Get-Item -LiteralPath $pinnedRelease).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-20)
    $runtimeBackups = Join-Path $root "opt\legion12-testrun-deployment\runtime-backups"
    $seededBackup = Join-Path $runtimeBackups "runtime-before-seeded-old.tar.gz"
    Write-Utf8NoBom $seededBackup "snapshot"
    Write-Utf8NoBom "$seededBackup.sha256" "fixture checksum sidecar"
    $consumedIncoming = Join-Path $incoming "l12-testrun-release-$expiredCommit.tar.gz"
    $unprovenIncoming = Join-Path $incoming ("l12-testrun-release-" + ("e" * 40) + ".tar.gz")
    Write-Utf8NoBom $consumedIncoming "consumed"
    Write-Utf8NoBom $unprovenIncoming "unproven"
    $staleStaging = Join-Path $root "opt\legion12-testrun-staging-seeded-expired"
    New-Item -ItemType Directory -Path $staleStaging -Force | Out-Null
    Write-Utf8NoBom (Join-Path $staleStaging "stale.txt") "stale"
    $unverifiableCardAssets = Join-Path $root ("opt\legion12-testrun-static\card-assets\" + ("f" * 64))
    New-Item -ItemType Directory -Path $unverifiableCardAssets -Force | Out-Null
    Write-Utf8NoBom (Join-Path $unverifiableCardAssets "orphan.txt") "orphan"

    $successEnvironment = $environment.Clone()
    $successEnvironment["L12_TEST_NEW_COMMIT"] = $commitC
    $successEnvironment["L12_TEST_FORCE_NEW_HEALTH_OK"] = "1"
    $successSaved = @{}
    foreach ($entry in $successEnvironment.GetEnumerator()) { $successSaved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $successArchiveServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$commitC.tar.gz"
        $successDeploy = Invoke-NativeCapture $shell @("-c", $launcher, "testrun-success", (ConvertTo-MsysPath $serverDeploy), "deploy", $commitC, (Get-FileHash $successArchive -Algorithm SHA256).Hash.ToLowerInvariant(), $successArchiveServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $successSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($successDeploy.ExitCode -eq 0) "Successful storage-convergence fixture failed: $($successDeploy.Output)"
    Assert-True ((Get-Content -LiteralPath (Join-Path $root "opt\legion12-testrun") -Raw).Contains($commitC)) "Successful deploy did not activate the expected commit."
    Assert-True (@(Get-ChildItem -LiteralPath $releaseBase -Directory).Count -eq 4) "Release retention did not converge to current + one rollback + failed/PINNED evidence."
    Assert-True (-not (Test-Path -LiteralPath $expiredRelease)) "Ordinary release outside current + one rollback was retained."
    Assert-True (Test-Path -LiteralPath $pinnedRelease -PathType Container) "Explicitly PINNED release evidence was deleted."
    Assert-True ($successDeploy.Output.Contains("retained failed release evidence") -and $successDeploy.Output.Contains("retained explicitly PINNED release")) `
        "Protected release reasons were not reported."
    Assert-True (Test-Path -LiteralPath $seededBackup -PathType Leaf) "Default-off runtime snapshot policy deleted a snapshot."
    Assert-True (Test-Path -LiteralPath "$seededBackup.sha256" -PathType Leaf) "Default-off runtime snapshot policy deleted a checksum sidecar."
    Assert-True (-not (Test-Path -LiteralPath $consumedIncoming)) "Proven consumed incoming artifact was retained."
    Assert-True (Test-Path -LiteralPath $unprovenIncoming -PathType Leaf) "Unproven incoming artifact was deleted."
    Assert-True (-not (Test-Path -LiteralPath $staleStaging)) "Managed stale staging directory was retained."
    Assert-True (Test-Path -LiteralPath $unverifiableCardAssets -PathType Container) "Card assets were deleted despite an unverifiable retained reference."
    Assert-True ($successDeploy.Output.Contains("runtime snapshot deletion skipped by default")) "Default-off runtime snapshot policy was not reported."
    Assert-True ($successDeploy.Output.Contains("card asset cleanup skipped")) "Conservative card-asset skip reason was not reported."
    Assert-True ($successDeploy.Output.Contains("post-deploy storage cleanup: before=") -and $successDeploy.Output.Contains("released=")) "Storage before/after/released metrics were not reported."
    foreach ($snapshot in Get-ChildItem -LiteralPath $runtimeBackups -Filter "runtime-before-*.tar.gz" -File) {
        Assert-True (Test-Path -LiteralPath "$($snapshot.FullName).sha256" -PathType Leaf) "Runtime snapshot is missing its SHA256 sidecar: $($snapshot.FullName)"
    }

    $commitD = "6" * 40
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") "$commitD`n"
    $repeatArchive = Join-Path $incoming "l12-testrun-release-$commitD.tar.gz"
    & $tar -czf $repeatArchive -C $releaseRoot .
    $repeatEnvironment = $environment.Clone()
    $repeatEnvironment["L12_TEST_NEW_COMMIT"] = $commitD
    $repeatEnvironment["L12_TEST_FORCE_NEW_HEALTH_OK"] = "1"
    $repeatSaved = @{}
    foreach ($entry in $repeatEnvironment.GetEnumerator()) { $repeatSaved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $repeatArchiveServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$commitD.tar.gz"
        $repeatDeploy = Invoke-NativeCapture $shell @("-c", $launcher, "testrun-repeat", (ConvertTo-MsysPath $serverDeploy), "deploy", $commitD, (Get-FileHash $repeatArchive -Algorithm SHA256).Hash.ToLowerInvariant(), $repeatArchiveServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $repeatSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($repeatDeploy.ExitCode -eq 0) "Repeated storage-convergence run was not idempotent: $($repeatDeploy.Output)"
    Assert-True (@(Get-ChildItem -LiteralPath $releaseBase -Directory).Count -eq 4) "Repeated cleanup did not preserve current + one rollback + protected evidence."
    Assert-True ($null -ne (Get-ChildItem -LiteralPath $releaseBase -Directory | Where-Object Name -Like "$commitC-*" | Select-Object -First 1)) "Repeated cleanup removed the immediately previous release."
    Assert-True (Test-Path -LiteralPath $pinnedRelease -PathType Container) "Repeated cleanup deleted PINNED evidence."
    Assert-True (Test-Path -LiteralPath $unprovenIncoming -PathType Leaf) "Repeated cleanup deleted an unproven incoming artifact."

    $failureDirectory = Join-Path $root "opt\legion12-testrun-deployment\failures"
    $cleanupProbeCommit = "6" * 40
    $cleanupProbeRelease = Join-Path $releaseBase "$cleanupProbeCommit-cleanup-probe"
    New-Item -ItemType Directory -Path (Join-Path $cleanupProbeRelease "opcgpro-vue\dist\assets"), (Join-Path $cleanupProbeRelease "opcgpro-vue\dist-testrun\assets") -Force | Out-Null
    Write-Utf8NoBom (Join-Path $cleanupProbeRelease ".deployment-commit") "$cleanupProbeCommit`n"
    (Get-Item -LiteralPath $cleanupProbeRelease).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-30)

    $findFailureCommit = "7" * 40
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") "$findFailureCommit`n"
    $findFailureArchive = Join-Path $incoming "l12-testrun-release-$findFailureCommit.tar.gz"
    & $tar -czf $findFailureArchive -C $releaseRoot .
    $findFailureEnvironment = $environment.Clone()
    $findFailureEnvironment["L12_TEST_NEW_COMMIT"] = $findFailureCommit
    $findFailureEnvironment["L12_TEST_FORCE_NEW_HEALTH_OK"] = "1"
    $findFailureEnvironment["L12_TEST_FAILURE_FIND_STATUS"] = "2"
    $findFailureSaved = @{}
    foreach ($entry in $findFailureEnvironment.GetEnumerator()) { $findFailureSaved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $findFailureServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$findFailureCommit.tar.gz"
        $findFailureDeploy = Invoke-NativeCapture $shell @("-c", $launcher, "failure-receipt-find-error", (ConvertTo-MsysPath $serverDeploy), "deploy", $findFailureCommit, (Get-FileHash $findFailureArchive -Algorithm SHA256).Hash.ToLowerInvariant(), $findFailureServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $findFailureSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($findFailureDeploy.ExitCode -eq 0) "Failure-receipt find error should skip cleanup after verified activation: $($findFailureDeploy.Output)"
    Assert-True ($findFailureDeploy.Output.Contains("failure receipt enumeration failed") -and $findFailureDeploy.Output.Contains("post-deploy storage cleanup skipped")) "find status was not propagated to whole-pass cleanup refusal."
    Assert-True (Test-Path -LiteralPath $cleanupProbeRelease -PathType Container) "find failure pruned ordinary release evidence."

    $grepFailureCommit = "8" * 40
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") "$grepFailureCommit`n"
    $grepFailureArchive = Join-Path $incoming "l12-testrun-release-$grepFailureCommit.tar.gz"
    & $tar -czf $grepFailureArchive -C $releaseRoot .
    $grepFailureEnvironment = $environment.Clone()
    $grepFailureEnvironment["L12_TEST_NEW_COMMIT"] = $grepFailureCommit
    $grepFailureEnvironment["L12_TEST_FORCE_NEW_HEALTH_OK"] = "1"
    $grepFailureEnvironment["L12_TEST_FAILURE_GREP_STATUS"] = "2"
    $grepFailureSaved = @{}
    foreach ($entry in $grepFailureEnvironment.GetEnumerator()) { $grepFailureSaved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $grepFailureServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$grepFailureCommit.tar.gz"
        $grepFailureDeploy = Invoke-NativeCapture $shell @("-c", $launcher, "failure-receipt-grep-error", (ConvertTo-MsysPath $serverDeploy), "deploy", $grepFailureCommit, (Get-FileHash $grepFailureArchive -Algorithm SHA256).Hash.ToLowerInvariant(), $grepFailureServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $grepFailureSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($grepFailureDeploy.ExitCode -eq 0) "Failure-receipt grep error should skip cleanup after verified activation: $($grepFailureDeploy.Output)"
    Assert-True ($grepFailureDeploy.Output.Contains("failure receipt read failed during ownership match") -and $grepFailureDeploy.Output.Contains("post-deploy storage cleanup skipped")) "grep exit 2 was treated as an ordinary nonmatch."
    Assert-True (Test-Path -LiteralPath $cleanupProbeRelease -PathType Container) "grep error pruned ordinary release evidence."

    $unknownRelease = Join-Path $releaseBase "unknown-release-in-progress"
    $unsafeFailureEntry = Join-Path $failureDirectory "deploy-$('a' * 12)-20261006T000000Z.txt"
    New-Item -ItemType Directory -Path $unknownRelease -Force | Out-Null
    New-Item -ItemType Directory -Path $unsafeFailureEntry -Force | Out-Null
    Write-Utf8NoBom (Join-Path $unknownRelease "partial.txt") "writer ownership is not proved"
    $commitE = "9" * 40
    Write-Utf8NoBom (Join-Path $releaseRoot ".deployment-commit") "$commitE`n"
    $unknownArchive = Join-Path $incoming "l12-testrun-release-$commitE.tar.gz"
    & $tar -czf $unknownArchive -C $releaseRoot .
    $unknownEnvironment = $environment.Clone()
    $unknownEnvironment["L12_TEST_NEW_COMMIT"] = $commitE
    $unknownEnvironment["L12_TEST_FORCE_NEW_HEALTH_OK"] = "1"
    $unknownSaved = @{}
    foreach ($entry in $unknownEnvironment.GetEnumerator()) { $unknownSaved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $unknownArchiveServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$commitE.tar.gz"
        $unknownDeploy = Invoke-NativeCapture $shell @("-c", $launcher, "unknown-release-cleanup-refusal", (ConvertTo-MsysPath $serverDeploy), "deploy", $commitE, (Get-FileHash $unknownArchive -Algorithm SHA256).Hash.ToLowerInvariant(), $unknownArchiveServerPath, $assetHash, "-", "-")
    }
    finally { foreach ($entry in $unknownSaved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process") } }
    Assert-True ($unknownDeploy.ExitCode -eq 0) "Unknown-release evidence should skip cleanup without invalidating the verified release: $($unknownDeploy.Output)"
    Assert-True ($unknownDeploy.Output.Contains("post-deploy storage cleanup skipped: release retention cannot be proven")) "Unknown release did not refuse the whole cleanup pass."
    Assert-True ($unknownDeploy.Output.Contains("failure receipt is partial, unsafe, or unowned")) "Unsafe failure receipt was treated as an absent failure."
    Assert-True (Test-Path -LiteralPath $unsafeFailureEntry -PathType Container) "Unsafe failure receipt evidence was deleted."
    Assert-True (Test-Path -LiteralPath $unknownRelease -PathType Container) "Unknown/in-progress release evidence was deleted."
    Assert-True (Test-Path -LiteralPath $pinnedRelease -PathType Container) "Unknown-release refusal deleted PINNED evidence."
    Assert-True ($null -ne (Get-ChildItem -LiteralPath $releaseBase -Directory | Where-Object Name -Like "$commitC-*" | Select-Object -First 1)) "Unknown-release refusal pruned an older release despite uncertain ownership."

    Write-Host "[L12 testrun deploy behavior] isolation, target pinning, rollback safety, and post-success storage convergence passed."
    $suitePassed = $true
}
finally {
    if ($suitePassed -and (Test-Path -LiteralPath $fixture)) {
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fixture).Path)
        if (-not $resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path $resolved -Leaf).StartsWith('l12-testrun-deploy-behavior-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove an unmanaged fixture: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $fixture) { Write-Host "[L12 testrun deploy behavior] Failed synthetic evidence retained: $fixture" }
}
