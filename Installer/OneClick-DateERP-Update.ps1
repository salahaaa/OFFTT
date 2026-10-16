[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$targetDir = "D:\DateERP_1.50.20_Full\DateERP_1.50.20"
$updater = Join-Path $PSScriptRoot "تحديث_مباشر.ps1"
$exe = Join-Path $targetDir "MfgSystem.exe"

function Stop-WithMessage([string]$message, [int]$code = 1) {
    Write-Host ""
    Write-Host $message -ForegroundColor Red
    Write-Host "المسار المطلوب: $targetDir"
    exit $code
}

if (-not (Test-Path -LiteralPath $updater)) {
    Stop-WithMessage "ملف التحديث الأساسي غير موجود بجانب هذا الملف."
}

if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
    Stop-WithMessage "مجلد النظام المستهدف غير موجود. لا تم تغيير أي ملف."
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    Stop-WithMessage "لم يتم العثور على MfgSystem.exe داخل مجلد النظام المستهدف. لا تم تغيير أي ملف."
}

Write-Host "تحديث DateERP بضغطة واحدة..." -ForegroundColor Cyan
Write-Host "المجلد المستهدف: $targetDir"
Write-Host "سيتم إغلاق البرنامج مؤقتاً وإنشاء نسخة احتياطية قبل الاستبدال."

# NonInteractive + Force = لا أسئلة ولا أوامر إضافية للمستخدم.
& $updater -InstallDir $targetDir -Force -NonInteractive
$code = $LASTEXITCODE
if ($code -ne 0) {
    Stop-WithMessage "فشل التحديث. راجع سجل التحديث داخل %LOCALAPPDATA%\MfgSystem\updates\updater.log" $code
}

Write-Host "تم تحديث النظام وتشغيله بنجاح." -ForegroundColor Green
exit 0
