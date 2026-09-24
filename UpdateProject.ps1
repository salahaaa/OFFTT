param(
    [string]$ArchiveUrl = 'https://github.com/salahaaa/OFFTT/archive/refs/heads/arena/01a0ac34-offtt.zip',
    [string]$LocalPackageRoot = '',
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Stop'
$transcriptStarted = $false
if ($LogPath) {
    try { Start-Transcript -Path $LogPath -Force | Out-Null; $transcriptStarted = $true } catch { }
}
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()

function Show-Message([string]$Text, [string]$Title, [System.Windows.Forms.MessageBoxButtons]$Buttons = [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]$Icon = [System.Windows.Forms.MessageBoxIcon]::Information) {
    return [System.Windows.Forms.MessageBox]::Show($Text, $Title, $Buttons, $Icon)
}

$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = 'اختر مجلد Source الذي يحتوي على DateERP.sln أو مجلد src — لا تختر مجلد publish'
$dialog.ShowNewFolderButton = $false
if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }

$target = [System.IO.Path]::GetFullPath($dialog.SelectedPath).TrimEnd('\')
$hasSource = (Test-Path (Join-Path $target 'src')) -or (Test-Path (Join-Path $target 'DateERP.sln')) -or (Test-Path (Join-Path $target 'DatesErp.sln'))
$hasPublishedExe = Test-Path (Join-Path $target 'MfgSystem.exe')
if ($hasPublishedExe -and -not $hasSource) {
    Show-Message 'هذا مجلد تشغيل publish ويحتوي على MfgSystem.exe. اختر مجلد Source الذي يحتوي على ملف الحل، وليس مجلد publish.' 'المجلد غير صحيح' | Out-Null
    exit 0
}
if (-not $hasSource) {
    $answer = Show-Message 'المجلد المختار لا يبدو مجلد مصدر؛ يجب اختيار مجلد Source الذي يحتوي على DateERP.sln أو src. هل تريد المتابعة؟' 'تأكيد مجلد المشروع' ([System.Windows.Forms.MessageBoxButtons]::YesNo) ([System.Windows.Forms.MessageBoxIcon]::Warning)
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }
}

if ($LocalPackageRoot -and (Test-Path $LocalPackageRoot)) {
    $localRoot = [System.IO.Path]::GetFullPath($LocalPackageRoot).TrimEnd('\')
    if ($localRoot -eq $target) {
        Show-Message 'اختر مجلد Source القديم المراد تحديثه، وليس مجلد الحزمة التي شغّلت منها الأداة.' 'المجلد غير صحيح' | Out-Null
        exit 0
    }
}

$answer = Show-Message "سيتم تحديث ملفات المصدر في:`n$target`n`nسيتم حذف ملفات الأرشيف والتحديثات القديمة المعروفة فقط. هل تريد المتابعة؟" 'تحديث المشروع' ([System.Windows.Forms.MessageBoxButtons]::YesNo) ([System.Windows.Forms.MessageBoxIcon]::Question)
if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }

$temp = $null
$sourceRoot = $null
try {
    if ($LocalPackageRoot -and (Test-Path (Join-Path $LocalPackageRoot 'DateERP.sln'))) {
        $sourceRoot = Get-Item -LiteralPath $LocalPackageRoot
        Show-Message 'سيتم استخدام ملفات الحزمة المحلية الموجودة بجانب أداة التحديث.' 'بدء التحديث' | Out-Null
    }
    else {
        Show-Message 'سيتم تنزيل النسخة الحالية ثم تحديث المشروع. اضغط موافق للبدء.' 'بدء التحديث' | Out-Null
        $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('MfgSystemUpdate_' + [guid]::NewGuid().ToString('N'))
        $zip = Join-Path $temp 'current.zip'
        $extract = Join-Path $temp 'extract'
        New-Item -ItemType Directory -Path $temp, $extract -Force | Out-Null
        Invoke-WebRequest -Uri $ArchiveUrl -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $extract -Force
        $sourceRoot = Get-ChildItem -Path $extract -Directory | Select-Object -First 1
    }

    if ($null -eq $sourceRoot -or -not (Test-Path (Join-Path $sourceRoot.FullName 'DateERP.sln'))) {
        throw 'لم يتم العثور على مصدر المشروع الحالي داخل الحزمة.'
    }

    $skip = '(^|[\\/])(\.git|bin|obj|\.vs|artifacts|publish|MfgSystem_Publish)([\\/]|$)'
    Get-ChildItem -Path $sourceRoot.FullName -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.FullName.Length).TrimStart('\','/')
        if ($relative -match $skip) { return }
        $destination = Join-Path $target ($relative -replace '/', '\')
        $parent = Split-Path -Parent $destination
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

    # إزالة البقايا المعروفة من إصدارات التحديث السابقة، مع إبقاء مرجع الثبات المطلوب.
    $stableArchive = 'UPDATE_1.50.73_FULL_SOURCE_BUILDABLE.zip'
    $legacyPatterns = @(
        'BASELINE_*.zip', 'DateERP_*.zip', 'DateERP_Source_*.zip', 'UPDATE_*.zip',
        'BUILD-PUBLISH-*.bat', 'CHECK-EXE-VERSION-*.ps1', 'VERIFY-SOURCE-*',
        'README_1.50.*', 'README_FULL_SOURCE_BUILD_*', 'README_UPDATE_*',
        'FIXES_*.md', 'FIXLOG_*.md', 'MERGE_*.md', 'UPDATES_*.md',
        'AUDIT_*.md', 'B83_*.md', 'COMPARE_*.md', 'INSPECTION_*.md',
        'PLAN_ROLLOVER*', 'PRODUCTION_*_*.md', 'PROFESSIONALIZATION*',
        'PlanningView_Save*', 'READINESS*', 'SCREENS_REVIEW*', 'UX_POLISH*',
        'WORKFLOW_DESIGN*', 'تقرير_*.md', 'تحسينات_*.md', 'خارطة_*.md'
    )
    Get-ChildItem -Path $target -File -Force | ForEach-Object {
        if ($_.Name -eq $stableArchive) { return }
        foreach ($pattern in $legacyPatterns) {
            if ($_.Name -like $pattern) {
                Remove-Item -LiteralPath $_.FullName -Force
                break
            }
        }
    }

    @(
        'DatesErp.sln',
        'UPDATE_*_ONECLICK',
        'Installer\OneClick',
        'Documentation\تقارير_المشروع',
        'tools\release',
        'tools\SqlFixes'
    ) | ForEach-Object {
        $path = Join-Path $target $_
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }

    Show-Message "تم تحديث مصدر المشروع بنجاح.`n`nالمجلد:`n$target`n`nلم يتم تغيير مجلد publish أو MfgSystem.exe.`n`nافتح DateERP.sln للبناء على Windows." 'اكتمل التحديث' | Out-Null
}
catch {
    Show-Message ("فشل التحديث:`n" + $_.Exception.Message) 'خطأ في التحديث' ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
finally {
    if ($temp -and (Test-Path $temp)) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
}
