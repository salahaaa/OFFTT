# Creates a NEW verified application folder. Does not modify the original, start DateERP, or access a DB.
param(
    [Parameter(Mandatory=$true)][string]$SourceFolder,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$staging = $null
$created = $false
function Root([string]$p) { return [IO.Path]::GetFullPath($p).TrimEnd('\') + '\' }
function SafePath([string]$root, [string]$relative) {
    $p = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $p.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe relative path: $relative" }
    return $p
}
function CheckFile([string]$p, $entry) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing file: $p" }
    if ((Get-Item -LiteralPath $p).Length -ne [long]$entry.bytes) { throw "Size mismatch: $p" }
    if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Hash mismatch: $p" }
}
try {
    $patch = Root $PSScriptRoot
    $source = Root $SourceFolder
    $dest = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if (Test-Path -LiteralPath $dest) { throw 'Destination must be a NEW, nonexistent folder.' }
    if ((Root $dest).StartsWith($source, [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination must be outside the original application folder.' }
    if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($dest)) -PathType Container)) { throw 'Destination parent folder must exist.' }
    $base = Get-Content -LiteralPath (Join-Path $patch 'base-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $target = Get-Content -LiteralPath (Join-Path $patch 'target-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $delta = Get-Content -LiteralPath (Join-Path $patch 'update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($base.version -ne '1.50.4' -or $target.version -ne '1.50.5') { throw 'Wrong base or target version.' }
    if (@($base.files).Count -eq 0 -or @($target.files).Count -eq 0) { throw 'Empty manifest.' }
    $baseFiles = @{}
    foreach ($entry in $base.files) {
        if ($baseFiles.ContainsKey($entry.path)) { throw 'Duplicate base path.' }
        $baseFiles[$entry.path] = $entry
        CheckFile (SafePath $source $entry.path) $entry
    }
    $updates = @{}
    foreach ($entry in $delta.files) {
        if ($updates.ContainsKey($entry.path)) { throw 'Duplicate update path.' }
        $updates[$entry.path] = $entry
        CheckFile (SafePath (Root (Join-Path $patch 'payload')) $entry.path) $entry
    }
    $staging = $dest + '.staging-' + [Guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path $staging | Out-Null
    $created = $true
    $stageRoot = Root $staging
    $seen = @{}
    foreach ($entry in $target.files) {
        if ($seen.ContainsKey($entry.path)) { throw 'Duplicate target path.' }
        $seen[$entry.path] = $true
        if ($updates.ContainsKey($entry.path)) { $from = SafePath (Root (Join-Path $patch 'payload')) $entry.path }
        elseif ($baseFiles.ContainsKey($entry.path)) { $from = SafePath $source $entry.path }
        else { throw "No verified source for: $($entry.path)" }
        $to = SafePath $stageRoot $entry.path
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($to)) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to
        CheckFile $to $entry
    }
    Copy-Item -LiteralPath (Join-Path $patch 'target-manifest.json') -Destination (Join-Path $stageRoot 'release-manifest.json')
    [IO.Directory]::Move($staging, $dest)
    $staging = $null
    Write-Host "PASS: New DateERP 1.50.5 folder: $dest" -ForegroundColor Green
    Write-Host 'Original folder was not changed. No application was started and no database was accessed.'
    Write-Host 'Run Check-Print-WPF.cmd and review its PDFs/PNGs before using a BACKED-UP TEST database.'
    Write-Host 'A new application folder does NOT isolate the database: LocalAppData configuration is reused.'
    exit 0
} catch {
    if ($created -and $staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force }
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
