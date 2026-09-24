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

try {
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
        'rollback_count < 2',
        'L12_TESTRUN_PRUNE_RUNTIME_BACKUPS:-0',
        'runtime_backup_checksum="${runtime_backup}.sha256"',
        'storage cleanup retained releases:',
        'storage cleanup kept unproven incoming artifact:',
        'card asset cleanup skipped:'
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
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\index.html") "testrun html"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\Page-new.js") "export const release = 'testrun-new'"
    Write-Utf8NoBom (Join-Path $releaseRoot "opcgpro-vue\dist-testrun\assets\Page-new.css") ".testrun-new{display:block}"
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
    }
    $saved = @{}
    foreach ($entry in $environment.GetEnumerator()) { $saved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process"); [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process") }
    try {
        $archiveServerPath = "$rootPosix/opt/legion12-testrun-deployment/incoming/l12-testrun-release-$commitB.tar.gz"
        $launcher = 'export PATH="$L12_TEST_FAKE_BIN:/usr/bin:/bin:$L12_TEST_NODE_DIR"; /usr/bin/sh "$1" "${@:2}"'
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
    Assert-True (@(Get-ChildItem -LiteralPath $releaseBase -Directory).Count -eq 3) "Release retention did not converge to current plus two rollback releases."
    Assert-True (-not (Test-Path -LiteralPath $expiredRelease)) "Release outside the current + two rollback boundary was retained."
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
    Assert-True (@(Get-ChildItem -LiteralPath $releaseBase -Directory).Count -eq 3) "Repeated cleanup did not preserve the same three-release boundary."
    Assert-True ($null -ne (Get-ChildItem -LiteralPath $releaseBase -Directory | Where-Object Name -Like "$commitC-*" | Select-Object -First 1)) "Repeated cleanup removed the immediately previous release."
    Assert-True (Test-Path -LiteralPath $unprovenIncoming -PathType Leaf) "Repeated cleanup deleted an unproven incoming artifact."

    Write-Host "[L12 testrun deploy behavior] isolation, target pinning, rollback safety, and post-success storage convergence passed."
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fixture).Path)
        if (-not $resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path $resolved -Leaf).StartsWith('l12-testrun-deploy-behavior-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove an unmanaged fixture: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
