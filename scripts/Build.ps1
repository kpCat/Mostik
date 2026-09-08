[CmdletBinding()]
param([string]$BadgeEntry = '', [switch]$SkipAssets)
. "$PSScriptRoot\Common.ps1"
Push-Location $script:Root
try {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK отсутствует. Выполните первую задачу Codex по настройке окружения.' }
    if (!$SkipAssets) { & "$PSScriptRoot\Get-BuildAssets.ps1" -BadgeEntry $BadgeEntry }
    $sdkArgs = @(Get-LocalSdkArguments)
    New-Item -ItemType Directory -Force -Path 'artifacts' | Out-Null
    # Удаляем только прежний результат скрипта: не выдаём старый APK за новую сборку.
    $output = Join-Path $script:Root 'artifacts\Mostik-0.1.0-debug.apk'
    if (Test-Path $output) { Remove-Item -LiteralPath $output -Force }
    if (Test-Path "$output.sha256") { Remove-Item -LiteralPath "$output.sha256" -Force }
    Invoke-Dotnet -Arguments (@('restore', 'Mostik.sln') + $sdkArgs)
    Invoke-Dotnet -Arguments @('run', '--project', 'tests/Mostik.Core.Smoke/Mostik.Core.Smoke.csproj', '-c', 'Release')
    Invoke-Dotnet -Arguments (@('clean', 'src/Mostik.Android/Mostik.Android.csproj', '-c', 'Debug') + $sdkArgs)
    Invoke-Dotnet -Arguments (@('build', 'src/Mostik.Android/Mostik.Android.csproj', '-c', 'Debug',
        '-t:SignAndroidPackage', '-p:AndroidPackageFormats=apk', '-p:EmbedAssembliesIntoApk=true',
        '-bl:artifacts/build.binlog') + $sdkArgs)
    $apk = @(Get-ChildItem -LiteralPath 'src\Mostik.Android\bin\Debug' -Recurse -File -Filter '*-Signed.apk' |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($apk.Count -ne 1) { throw "Ожидался один подписанный APK, найдено $($apk.Count). Проверьте результаты сборки, не выбирайте файл наугад." }
    Copy-Item -LiteralPath $apk[0].FullName -Destination $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  Mostik-0.1.0-debug.apk" | Set-Content -LiteralPath "$output.sha256" -Encoding ASCII
    Write-Host "APK для личного тестирования: $output"
    Write-Host 'Использован отладочный ключ. Проверки Redmi/авиарежима скрипт не выполнял.'
} finally { Pop-Location }
