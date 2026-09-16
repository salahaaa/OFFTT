# Read-only integrity check. No database access, installation, or execution-policy changes.
$ErrorActionPreference = 'Stop'
try {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') + '\'
    $manifest = Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.algorithm -ne 'SHA256' -or @($manifest.files).Count -eq 0) { throw 'Invalid release manifest.' }
    $seen = @{}
    foreach ($entry in $manifest.files) {
        $relative = [string]$entry.path
        $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe manifest path: $relative" }
        if ($seen.ContainsKey($relative)) { throw "Duplicate manifest path: $relative" }
        $seen[$relative] = $true
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing file: $relative" }
        if ((Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes) { throw "Size mismatch: $relative" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Hash mismatch: $relative" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relative = $file.FullName.Substring($root.Length).Replace('\', '/')
        if ($file.Extension -in @('.exe', '.dll', '.ps1', '.cmd', '.bat') -and -not $seen.ContainsKey($relative)) {
            throw "Unlisted executable/script: $relative"
        }
    }
    Write-Host ("PASS: {0} files; DateERP {1}; SHA256 verified." -f $seen.Count, $manifest.version) -ForegroundColor Green
    Write-Host 'Integrity does not establish publisher identity. Windows UI and database acceptance remain separate checks.'
    exit 0
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
