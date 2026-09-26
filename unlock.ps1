# 번역기 설정 파일 잠금 해제 (설정을 직접 고치거나 패치를 지울 때 사용)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here "common.ps1")
$gamePath = Resolve-GamePath
if (-not $gamePath) { pause; exit 1 }
$me = "$env:USERDOMAIN\$env:USERNAME"
$n = 0
foreach ($cfg in (Get-ConfigPaths $gamePath)) {
    if (Test-Path $cfg) {
        & icacls "$cfg" /remove:d "$me" 2>&1 | Out-Null
        Write-Host ("잠금 해제: " + $cfg) -ForegroundColor Green
        $n++
    }
}
if ($n -eq 0) { Write-Host "잠긴 설정 파일이 없습니다." -ForegroundColor DarkGray }