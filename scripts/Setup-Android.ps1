[CmdletBinding()]
param([switch]$InstallWorkload, [switch]$AcceptLicenses)
. "$PSScriptRoot\Common.ps1"
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Сначала установите .NET 10 SDK через Visual Studio Installer.' }
if (!$AcceptLicenses) { throw 'Для установки Android SDK/JDK прочтите их лицензии и явно укажите -AcceptLicenses.' }
Push-Location $script:Root
try {
    if ($InstallWorkload) { Invoke-Dotnet -Arguments @('workload', 'install', 'android') }
    $sdk = Join-Path $script:Root '.tools\android-sdk'
    $jdk = Join-Path $script:Root '.tools\jdk'
    Invoke-Dotnet -Arguments @('build', 'src/Mostik.Android/Mostik.Android.csproj',
        '-t:InstallAndroidDependencies', '-f', 'net10.0-android',
        "-p:AndroidSdkDirectory=$sdk", "-p:JavaSdkDirectory=$jdk", '-p:AcceptAndroidSdkLicenses=True',
        '-p:SkipPreparedAssetsCheck=true')
    Write-Host 'Локальные SDK/JDK подготовлены. Глобальные PATH/JAVA_HOME не изменялись.'
} finally { Pop-Location }
