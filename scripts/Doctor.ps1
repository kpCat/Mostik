[CmdletBinding()]
param()
. "$PSScriptRoot\Common.ps1"
Push-Location $script:Root
try {
    Write-Host "Репозиторий: $script:Root"
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        & dotnet --list-sdks
        & dotnet --version
        & dotnet workload list
    } else { Write-Warning '.NET SDK не найден. Установите .NET 10 через Visual Studio Installer.' }
    Write-Host "JAVA_HOME=$env:JAVA_HOME"
    Write-Host "ANDROID_HOME=$env:ANDROID_HOME"
    try { $adb = Find-Adb; Write-Host "ADB: $adb"; & $adb devices -l }
    catch { Write-Warning $_.Exception.Message }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) { & $vswhere -latest -products '*' -format json }
    Write-Host 'Это диагностика, а не подтверждение успешной сборки.'
} finally { Pop-Location }
