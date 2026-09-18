# VERIFY-SOURCE 1.50.73 FINAL2
# The BAT wrapper deliberately contains no inline PowerShell.  Keep the six
# checks here so CMD metacharacters, Arabic text, and parenthesized blocks
# cannot change the meaning of the verification command.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$checks = @(
    @{ Id = 'F1'; Path = 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml'; Marker = 'SaveActionBtn' },
    @{ Id = 'F2'; Path = 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs'; Marker = 'WithSave' },
    @{ Id = 'F3'; Path = 'src\DatesErp.Desktop\DatesErp.Desktop.csproj'; Marker = '1.50.73' },
    @{ Id = 'F4'; Path = 'src\DatesErp.Desktop\DatesErp.Desktop.csproj'; Marker = 'AssemblyFileVersion>1.50.73' },
    @{ Id = 'F5'; Path = 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml.cs'; Marker = '1.50.73' },
    @{ Id = 'F6'; Path = 'src\DatesErp.Desktop\Views\Screens\PlanningView.xaml'; Marker = '5B7595' }
)

Write-Host '============================================================'
Write-Host '  VERIFY-SOURCE 1.50.73 FINAL2'
Write-Host '============================================================'
$fails = 0
$utf8 = [System.Text.Encoding]::UTF8

foreach ($check in $checks) {
    $path = Join-Path $root $check.Path
    if (-not [System.IO.File]::Exists($path)) {
        Write-Host (('[FAIL] {0}: missing {1}' -f $check.Id, $check.Path)) -ForegroundColor Red
        $fails++
        continue
    }

    $content = [System.IO.File]::ReadAllText($path, $utf8)
    if ($content.Contains($check.Marker)) {
        Write-Host (('[OK]   {0}: {1}' -f $check.Id, $check.Marker)) -ForegroundColor Green
    }
    else {
        Write-Host (('[FAIL] {0}: marker not found: {1}' -f $check.Id, $check.Marker)) -ForegroundColor Red
        $fails++
    }
}

if ($fails -ne 0) {
    Write-Host (('[STOP] {0} FINAL2 source check(s) failed.' -f $fails)) -ForegroundColor Red
    exit 1
}

Write-Host '[PASS] All six FINAL2 source checks passed.' -ForegroundColor Green
exit 0
