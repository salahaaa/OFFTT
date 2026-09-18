# =====================================================================
#  CHECK-EXE-VERSION 1.50.73 FINAL — فحص آلي لسمات الإصدار في MfgSystem.exe
#  usage:  powershell -File CHECK-EXE-VERSION-1.50.73.ps1 <مسار MfgSystem.exe>
#  يمر فقط إذا:  FileVersion = 1.50.73.0  و  ProductVersion = 1.50.73
#  الملف ببداية BOM عمدًا (PowerShell).
# =====================================================================
$ErrorActionPreference = 'Stop'
if ($args.Count -lt 1) { Write-Host '[FAIL] لم يُمرر مسار MfgSystem.exe' -ForegroundColor Red; exit 1 }
$exe = $args[0]
if (-not (Test-Path $exe)) { Write-Host ('[FAIL] الملف غير موجود: ' + $exe) -ForegroundColor Red; exit 1 }
$vi = (Get-Item $exe).VersionInfo
Write-Host ('  FileVersion    : ' + $vi.FileVersion)
Write-Host ('  ProductVersion : ' + $vi.ProductVersion)
$okFile = ($vi.FileVersion -eq '1.50.73.0')
$okProd = ($vi.ProductVersion -eq '1.50.73')
if ($okFile -and $okProd) {
    Write-Host '[OK] سمات الإصدار صحيحة: 1.50.73' -ForegroundColor Green
    exit 0
}
Write-Host '[FAIL] الإصدار الفعلي ليس 1.50.73 — لا تُشغَّل هذه النسخة.' -ForegroundColor Red
exit 1
