# 설치/폰트/잠금해제 스크립트가 함께 쓰는 함수 모음 (직접 실행하지 않음)

function Get-SteamLibraries {
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($rk in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $v = Get-ItemProperty -Path $rk -ErrorAction Stop
            foreach ($name in @("SteamPath", "InstallPath")) {
                $sp = $v.$name
                if ($sp) { $roots.Add(($sp -replace '/', '\')) }
            }
        } catch {}
    }
    $roots.Add("C:\Program Files (x86)\Steam")
    foreach ($d in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Z]:\\$' })) {
        foreach ($sub in @("Steam", "SteamLibrary", "Program Files (x86)\Steam", "Program Files\Steam", "Games\Steam")) {
            $roots.Add((Join-Path $d.Root $sub))
        }
    }
    $libs = New-Object System.Collections.Generic.List[string]
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $libs.Add($r)
        $vdf = Join-Path $r "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs.Add($m.Groups[1].Value.Replace('\\', '\'))
            }
        }
    }
    return @($libs | Select-Object -Unique)
}

# 게임 폴더(Schedule I.exe 가 있는 곳)를 찾는다. 못 찾으면 직접 입력받고, 그래도 없으면 $null.
function Resolve-GamePath([string]$GamePath = "") {
    if ($GamePath) {
        $GamePath = $GamePath.Trim('"').Trim()
        if (Test-Path (Join-Path $GamePath "Schedule I.exe")) { return $GamePath }
    }
    foreach ($lib in (Get-SteamLibraries)) {
        $c = Join-Path $lib "steamapps\common\Schedule I"
        if (Test-Path (Join-Path $c "Schedule I.exe")) { return $c }
    }
    Write-Host "게임 폴더를 자동으로 찾지 못했습니다." -ForegroundColor Yellow
    Write-Host "Steam 라이브러리에서 Schedule I 우클릭 > 관리 > 로컬 파일 보기 로 열리는 폴더의 경로를 복사해 붙여넣으세요."
    $inp = Read-Host "Schedule I 게임 폴더 경로"
    if ($inp) { $inp = $inp.Trim('"').Trim() }
    if ($inp -and (Test-Path (Join-Path $inp "Schedule I.exe"))) { return $inp }
    Write-Host "해당 경로에서 게임을 찾을 수 없습니다." -ForegroundColor Red
    return $null
}

# 어떤 모드 로더가 깔려 있는지 확인한다.
#   MelonLoader: version.dll + MelonLoader 폴더 (넥서스 모드 대부분이 사용)
#   BepInEx    : winhttp.dll + BepInEx\core (이 패치의 기본 방식)
function Get-LoaderState([string]$gamePath) {
    return [pscustomobject]@{
        Melon     = (Test-Path (Join-Path $gamePath "version.dll")) -and (Test-Path (Join-Path $gamePath "MelonLoader"))
        BepActive = (Test-Path (Join-Path $gamePath "winhttp.dll")) -and (Test-Path (Join-Path $gamePath "BepInEx\core"))
        BepFiles  = (Test-Path (Join-Path $gamePath "BepInEx\core\BepInEx.Core.dll"))
    }
}

# 번역기 설정 파일 위치 (로더마다 다름)
function Get-ConfigPaths([string]$gamePath) {
    return @(
        (Join-Path $gamePath "BepInEx\config\AutoTranslatorConfig.ini"),
        (Join-Path $gamePath "AutoTranslator\Config.ini")
    )
}
