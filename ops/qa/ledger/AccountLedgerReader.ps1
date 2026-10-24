# Four original immutable reader functions, extracted without the historical test runner.
# Source SHA256: C991435C5E18FC8A26D9F57264E25BC38F4D1B4D4CC79DAF395D1C0EFCC6BECE
# Callers verify this file and load exactly these function ASTs; no top-level runner executes.

function Throw-L12RealTest([string]$Code) {
    throw [InvalidOperationException]::new($Code)
}

function Assert-L12RegularFile([string]$Path, [int64]$MaxBytes, [string]$Code = 'SOURCE_REJECTED') {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not ($item -is [IO.FileInfo]) -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $item.Length -lt 1 -or $item.Length -gt $MaxBytes) {
        Throw-L12RealTest $Code
    }
    $item.FullName
}

function Test-L12ContainsBytes([byte[]]$Haystack, [byte[]]$Needle) {
    if ($Needle.Length -eq 0 -or $Needle.Length -gt $Haystack.Length) { return $false }
    for ($offset = 0; $offset -le $Haystack.Length - $Needle.Length; $offset += 1) {
        $match = $true
        for ($index = 0; $index -lt $Needle.Length; $index += 1) {
            if ($Haystack[$offset + $index] -ne $Needle[$index]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    $false
}

function Read-L12ProtectedSnapshot([string]$Path, [string]$RunId, [string[]]$ForbiddenPlaintext) {
    $full = Assert-L12RegularFile $Path 1048576 'SNAPSHOT_REJECTED'
    $cipher = [IO.File]::ReadAllBytes($full)
    $entropyInput = $script:Utf8NoBom.GetBytes("l12-cohort-ledger-r5|$RunId")
    $entropy = $null
    $plain = $null
    try {
        foreach ($value in $ForbiddenPlaintext) {
            $needle = $script:Utf8NoBom.GetBytes($value)
            try {
                if (Test-L12ContainsBytes $cipher $needle) { Throw-L12RealTest 'CIPHERTEXT_REJECTED' }
            }
            finally { [Array]::Clear($needle, 0, $needle.Length) }
        }
        $cipherSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cipher)).ToUpperInvariant()
        $entropy = [Security.Cryptography.SHA256]::HashData($entropyInput)
        $plain = [Security.Cryptography.ProtectedData]::Unprotect(
            $cipher,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::CurrentUser
        )
        [pscustomobject]@{
            Snapshot = ($script:Utf8NoBom.GetString($plain) | ConvertFrom-Json -Depth 16 -DateKind String)
            CipherSha256 = $cipherSha
        }
    }
    catch {
        if ($_.Exception.Message -eq 'CIPHERTEXT_REJECTED') { throw }
        Throw-L12RealTest 'SNAPSHOT_REJECTED'
    }
    finally {
        [Array]::Clear($cipher, 0, $cipher.Length)
        [Array]::Clear($entropyInput, 0, $entropyInput.Length)
        if ($null -ne $entropy) { [Array]::Clear($entropy, 0, $entropy.Length) }
        if ($null -ne $plain) { [Array]::Clear($plain, 0, $plain.Length) }
    }
}
