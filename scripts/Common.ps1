Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Root = Split-Path $PSScriptRoot -Parent
function Invoke-Dotnet {
    param([Parameter(Mandatory=$true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet завершился с кодом $LASTEXITCODE : $($Arguments -join ' ')" }
}
function Find-Adb {
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $roots = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT,
        (Join-Path $script:Root '.tools\android-sdk'),
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
        (Join-Path ${env:ProgramFiles(x86)} 'Android\android-sdk'))
    foreach ($root in $roots) {
        if ($root) {
            $candidate = Join-Path $root 'platform-tools\adb.exe'
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    throw 'ADB не найден. Укажите Android SDK в ANDROID_HOME или установите зависимости.'
}
function Get-LocalSdkArguments {
    $result = @()
    $sdk = Join-Path $script:Root '.tools\android-sdk'
    $jdk = Join-Path $script:Root '.tools\jdk'
    if (Test-Path (Join-Path $sdk 'platform-tools')) { $result += "-p:AndroidSdkDirectory=$sdk" }
    if (Test-Path (Join-Path $jdk 'bin\java.exe')) { $result += "-p:JavaSdkDirectory=$jdk" }
    return $result
}
