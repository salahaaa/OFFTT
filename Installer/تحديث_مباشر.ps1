[CmdletBinding()]
param(
    [string]$InstallDir = "",
    [string]$ManifestUrl = "",
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$NoLaunch,
    [switch]$NonInteractive
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

function Get-UpdatePackage($Manifest) {
    if ([string]::IsNullOrWhiteSpace($Manifest.packageUrl)) {
        Fail "ملف manifest لا يحتوي packageUrl صالحاً."
    }
    $headers = @{ "User-Agent" = "MfgSystem-LiveUpdater" }
    Write-UpdateLog "جاري فحص حزمة التحديث المحددة..."
    try {
        $head = Invoke-WebRequest -Uri $Manifest.packageUrl -Headers $headers -Method Head -UseBasicParsing
    } catch {
        Fail "مصدر حزمة التحديث غير متاح (HTTP 404 أو رابط غير صحيح): $($Manifest.packageUrl)"
    }
    if (-not $head -or $head.StatusCode -lt 200 -or $head.StatusCode -ge 400) {
        Fail "مصدر حزمة التحديث لم يُرجع استجابة صالحة: $($Manifest.packageUrl)"
    }
    return [pscustomobject]@{
        Version = [string]$Manifest.packageVersion
        Name = [string]$Manifest.packageName
        Url = [string]$Manifest.packageUrl
        Sha256Url = [string]$Manifest.packageSha256Url
    }
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
    $latest = Get-UpdatePackage $manifest
    $latestText = $latest.Version
    $latestVersion = Get-Version $latestText
    Write-UpdateLog "الإصدار الحالي: $currentText | الحزمة المحددة: $latestText"

    if (-not $Force -and $latestVersion -le $current) {
        Write-Host "لا يوجد تحديث أحدث من الإصدار الحالي."
        exit 0
    }
    if ($CheckOnly) {
        Write-Host "مصدر التحديث متاح. يوجد تحديث: $latestText"
        exit 0
    }

    $answer = if ($NonInteractive) { "Y" } else { Read-Host "سيتم تحديث $target إلى $latestText. هل تريد المتابعة؟ (Y/N)" }
    if ($answer -notmatch '^(Y|y|ن|نعم)$') {
        Write-UpdateLog "ألغى المستخدم التحديث."
        exit 0
    }

    $zipPath = Join-Path $tempRoot $latest.Name
    Write-UpdateLog "تنزيل $($latest.Name) من المصدر المحدد..."
    Invoke-WebRequest -Uri $latest.Url -Headers @{ "User-Agent" = "MfgSystem-LiveUpdater" } -OutFile $zipPath -UseBasicParsing
    if ($latest.Sha256Url) {
        try {
            $expectedZipHash = ((Invoke-WebRequest -Uri $latest.Sha256Url -Headers @{ "User-Agent" = "MfgSystem-LiveUpdater" } -UseBasicParsing).Content -replace '[^0-9a-fA-F]','').ToUpperInvariant()
            $actualZipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
            if ($expectedZipHash.Length -ge 64 -and $expectedZipHash.Substring(0,64) -ne $actualZipHash) {
                Fail "فشل تحقق SHA256 لحزمة التحديث نفسها. تم إيقاف التحديث دون لمس النظام."
            }
            Write-UpdateLog "تم التحقق من SHA256 لحزمة ZIP."
        } catch {
            Fail "تعذر التحقق من SHA256 لحزمة التحديث: $($_.Exception.Message)"
        }
    }
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
