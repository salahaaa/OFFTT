[CmdletBinding()]
param(
    [string]$InstallDir = "",
    [string]$ManifestUrl = "",
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $scriptRoot "update-manifest.json"
$logRoot = Join-Path $env:LOCALAPPDATA "MfgSystem\updates"
$logPath = Join-Path $logRoot "updater.log"
$tempRoot = Join-Path $env:TEMP ("MfgSystemUpdate-" + [guid]::NewGuid().ToString("N"))

function Write-UpdateLog([string]$Message) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Write-Host $line
    try {
        New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch { }
}

function Fail([string]$Message) {
    Write-UpdateLog "ERROR: $Message"
    throw $Message
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { Fail "ملف إعداد التحديث غير موجود: $Path" }
    return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-Version([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return [version]"0.0.0" }
    try { return [version]($Path.Trim().TrimStart("v")) } catch { return [version]"0.0.0" }
}

function Get-InstalledVersion([string]$Folder, [string]$ExeName) {
    $versionFile = Join-Path $Folder "VERSION.txt"
    if (Test-Path -LiteralPath $versionFile) {
        $value = (Get-Content -LiteralPath $versionFile -First 1).Trim()
        if ($value) { return $value }
    }
    $exe = Join-Path $Folder $ExeName
    if (Test-Path -LiteralPath $exe) {
        $value = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
        if ($value) { return ($value -replace '[^0-9.]','') }
    }
    return "0.0.0"
}

function Find-InstallDir([string]$ExeName) {
    $candidates = @()
    if ($InstallDir) { $candidates += $InstallDir }
    if ($env:MFGSYSTEM_INSTALL_DIR) { $candidates += $env:MFGSYSTEM_INSTALL_DIR }
    $candidates += (Join-Path ${env:ProgramFiles} "MfgSystem")
    $candidates += (Join-Path ${env:LOCALAPPDATA} "Programs\MfgSystem")
    if (Test-Path -LiteralPath (Join-Path $scriptRoot $ExeName)) { $candidates += $scriptRoot }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate $ExeName))) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    return ""
}

function Get-LatestRelease($Manifest) {
    $headers = @{ "User-Agent" = "MfgSystem-LiveUpdater"; "Accept" = "application/vnd.github+json" }
    Write-UpdateLog "جاري فحص الإصدار الأخير من GitHub..."
    try {
        $release = Invoke-RestMethod -Uri $Manifest.releaseApi -Headers $headers -Method Get
    } catch {
        Fail "تعذر الاتصال بمصدر التحديث: $($_.Exception.Message)"
    }
    if (-not $release -or $release.draft -or $release.prerelease) {
        Fail "لا يوجد إصدار مستقر منشور للتحديث حتى الآن."
    }
    $asset = @($release.assets) | Where-Object { $_.name -like $Manifest.assetPattern } | Select-Object -First 1
    if (-not $asset) {
        Fail "الإصدار $($release.tag_name) لا يحتوي حزمة $($Manifest.assetPattern)."
    }
    return [pscustomobject]@{ Release = $release; Asset = $asset }
}

function Stop-InstalledApp([string]$Folder, [string]$ExeName) {
    $exePath = [IO.Path]::GetFullPath((Join-Path $Folder $ExeName))
    $running = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq $exePath) })
    if ($running.Count -gt 0) {
        Write-UpdateLog "إغلاق البرنامج قبل استبدال الملفات..."
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 800
    }
}

function Backup-Install([string]$Folder) {
    $backupRoot = Join-Path $env:LOCALAPPDATA "MfgSystem\updates\backups"
    $backup = Join-Path $backupRoot (Get-Date -Format "yyyyMMdd-HHmmss")
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Get-ChildItem -LiteralPath $Folder -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin @("updates", "logs") } |
        Copy-Item -Destination $backup -Recurse -Force
    Write-UpdateLog "تم إنشاء نسخة رجوع: $backup"
    return $backup
}

function Verify-Package([string]$Folder, $Manifest) {
    $exe = Join-Path $Folder $Manifest.executable
    if (-not (Test-Path -LiteralPath $exe)) { Fail "الحزمة لا تحتوي $($Manifest.executable)." }
    $hashFile = Join-Path $Folder "SHA256.txt"
    if (Test-Path -LiteralPath $hashFile) {
        $expected = ((Get-Content -LiteralPath $hashFile -Raw) -replace '[^0-9a-fA-F]','').ToUpperInvariant()
        $actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($expected.Length -ge 64 -and $expected.Substring(0,64) -ne $actual) {
            Fail "فشل تحقق SHA256 للحزمة. تم إيقاف التحديث دون لمس النظام."
        }
        Write-UpdateLog "تم التحقق من SHA256 للملف التنفيذي."
    } else {
        Write-UpdateLog "تحذير: لا يوجد SHA256.txt؛ لن يتم رفض الحزمة بسبب غيابه."
    }
}

try {
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    $manifest = if ($ManifestUrl) {
        Write-UpdateLog "تحميل إعداد التحديث من $ManifestUrl"
        Invoke-RestMethod -Uri $ManifestUrl -Headers @{ "User-Agent" = "MfgSystem-LiveUpdater" }
    } else { Read-JsonFile $manifestPath }

    $target = Find-InstallDir $manifest.executable
    if (-not $target) {
        $target = Read-Host "أدخل مسار مجلد تثبيت MfgSystem الذي يحتوي MfgSystem.exe"
        if (-not (Test-Path -LiteralPath (Join-Path $target $manifest.executable))) {
            Fail "المجلد المحدد لا يحتوي $($manifest.executable)."
        }
        $target = [IO.Path]::GetFullPath($target)
    }

    $currentText = Get-InstalledVersion $target $manifest.executable
    $current = Get-Version $currentText
    $latest = Get-LatestRelease $manifest
    $latestText = ($latest.Release.tag_name -replace '^v','')
    $latestVersion = Get-Version $latestText
    Write-UpdateLog "الإصدار الحالي: $currentText | الإصدار المتاح: $latestText"

    if (-not $Force -and $latestVersion -le $current) {
        Write-Host "لا يوجد تحديث أحدث من الإصدار الحالي."
        exit 0
    }
    if ($CheckOnly) {
        Write-Host "يوجد تحديث متاح: $latestText"
        exit 0
    }

    $answer = Read-Host "سيتم تحديث $target إلى $latestText. هل تريد المتابعة؟ (Y/N)"
    if ($answer -notmatch '^(Y|y|ن|نعم)$') {
        Write-UpdateLog "ألغى المستخدم التحديث."
        exit 0
    }

    $zipPath = Join-Path $tempRoot $latest.Asset.name
    Write-UpdateLog "تنزيل $($latest.Asset.name)..."
    Invoke-WebRequest -Uri $latest.Asset.browser_download_url -Headers @{ "User-Agent" = "MfgSystem-LiveUpdater" } -OutFile $zipPath
    $extract = Join-Path $tempRoot "package"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extract -Force
    Verify-Package $extract $manifest

    Stop-InstalledApp $target $manifest.executable
    $backup = Backup-Install $target
    Write-UpdateLog "نسخ ملفات الإصدار الجديد..."
    Get-ChildItem -LiteralPath $extract -Force |
        Where-Object { $_.Name -notin @("SHA256.txt", "VERSION.txt") -or $_.Name -in @("SHA256.txt", "VERSION.txt") } |
        Copy-Item -Destination $target -Recurse -Force

    # لا يكتب المحدث إلى قاعدة البيانات ولا يغير config.json أو مجلد البيانات.
    Write-UpdateLog "اكتمل تحديث ملفات البرنامج. النسخة الاحتياطية: $backup"
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $target $manifest.executable) -WorkingDirectory $target
        Write-UpdateLog "تم تشغيل البرنامج بعد التحديث."
    }
    Write-Host "تم تحديث النظام بنجاح إلى الإصدار $latestText."
    exit 0
} catch {
    Write-Host ""
    Write-Host "فشل التحديث: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "سجل التحديث: $logPath"
    exit 1
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
