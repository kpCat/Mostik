[CmdletBinding()]
param([string]$Serial = '', [string]$Apk = '')
. "$PSScriptRoot\Common.ps1"
if (!$Apk) { $Apk = Join-Path $script:Root 'artifacts\Mostik-0.1.0-debug.apk' }
if (!(Test-Path -LiteralPath $Apk)) { throw "APK не найден: $Apk" }
$adb = Find-Adb
$lines = @(& $adb devices)
if ($LASTEXITCODE -ne 0) { throw 'Не удалось получить список устройств ADB.' }
$devices = @($lines | ForEach-Object { if ($_ -match '^([^\s]+)\s+device$') { $Matches[1] } })
if (!$Serial) {
    if ($devices.Count -ne 1) { throw 'Нужно ровно одно разрешённое устройство ADB либо параметр -Serial.' }
    $Serial = $devices[0]
}
if ($devices -notcontains $Serial) { throw 'Устройство не подключено либо не подтверждена USB-отладка на телефоне.' }
& $adb -s $Serial install -r $Apk
if ($LASTEXITCODE -ne 0) { throw 'APK не установлен. Не удаляйте прежнее приложение автоматически: это уничтожит его языковые пакеты.' }
Write-Host 'APK установлен. Откройте Мостик на телефоне; настройка голосов выполняется там.'
