[CmdletBinding()]
param([string]$BadgeEntry = '')
. "$PSScriptRoot\Common.ps1"
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$config = Get-Content -LiteralPath (Join-Path $script:Root 'data\build-assets.json') -Raw | ConvertFrom-Json
$cache = Join-Path $script:Root '.cache'
$artifacts = Join-Path $script:Root 'artifacts'
New-Item -ItemType Directory -Force -Path $cache, $artifacts | Out-Null

# Только HTTPS, без редиректов и с ограничением размера. Ничего загруженное не исполняется.
function Get-HttpsFile {
    param([string]$Url, [string]$Destination, [long]$MaximumBytes)
    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https') { throw 'Разрешён только HTTPS.' }
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(15)
    $deadline = New-Object System.Threading.CancellationTokenSource
    $deadline.CancelAfter([TimeSpan]::FromMinutes(15))
    $partial = "$Destination.partial"
    $response = $null; $inputStream = $null; $output = $null
    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) { throw "HTTP $([int]$response.StatusCode): $Url" }
        if ($response.Content.Headers.ContentLength -gt $MaximumBytes) { throw 'Слишком большой ответ сервера.' }
        $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [IO.File]::Create($partial)
        $buffer = New-Object byte[] 65536
        [long]$total = 0
        while (($read = $inputStream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
            $total += $read
            if ($total -gt $MaximumBytes) { throw 'Превышен допустимый размер файла.' }
            $output.Write($buffer, 0, $read)
        }
        $output.Dispose(); $output = $null
        if ($total -eq 0) { throw 'Получен пустой файл.' }
        Move-Item -LiteralPath $partial -Destination $Destination -Force
    } finally {
        if ($output) { $output.Dispose() }
        if ($inputStream) { $inputStream.Dispose() }
        if ($response) { $response.Dispose() }
        $client.Dispose(); $handler.Dispose(); $deadline.Dispose()
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}

$aar = Join-Path $cache "vosk-android-$($config.VoskVersion).aar"
$expected = [string]$config.VoskSha256
$hashSource = 'data/build-assets.json'
if ([string]::IsNullOrWhiteSpace($expected)) {
    $sidecar = "$aar.sha256"
    Get-HttpsFile "$($config.VoskAarUrl).sha256" $sidecar 4096
    $sidecarText = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($sidecarText -notmatch '^([0-9a-fA-F]{64})(\s|$)') { throw 'Сервер не вернул SHA-256; проверку обходить нельзя.' }
    $expected = $Matches[1]
    $hashSource = "$($config.VoskAarUrl).sha256 (same-origin; ещё не независимое закрепление)"
}
if ($expected -notmatch '^[0-9a-fA-F]{64}$') { throw 'Некорректный закреплённый SHA-256 Vosk.' }
if (Test-Path -LiteralPath $aar) {
    if ((Get-FileHash -LiteralPath $aar -Algorithm SHA256).Hash -ine $expected) {
        Remove-Item -LiteralPath $aar -Force
    }
}
if (!(Test-Path -LiteralPath $aar)) {
    Write-Host 'Загрузка нативной библиотеки Vosk…'
    Get-HttpsFile $config.VoskAarUrl $aar (128MB)
}
$actual = (Get-FileHash -LiteralPath $aar -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ine $expected) { throw 'SHA-256 AAR Vosk не совпал. Сборка остановлена.' }

$nativeParent = Join-Path $script:Root 'src\Mostik.Android\NativeLibs'
New-Item -ItemType Directory -Force -Path $nativeParent | Out-Null
$stage = Join-Path $nativeParent ('.stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($aar)
try {
    $entries = @($zip.Entries | Where-Object { $_.FullName -match '^jni/arm64-v8a/[A-Za-z0-9_.+-]+\.so$' })
    if ($entries.Count -eq 0) { throw 'В AAR нет ARM64 native libraries.' }
    [long]$total = 0
    foreach ($entry in $entries) {
        $total += $entry.Length
        if ($total -gt 256MB) { throw 'Слишком большой набор нативных библиотек.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage $entry.Name), $false)
    }
    if (!(Test-Path (Join-Path $stage 'libvosk.so'))) { throw 'В AAR отсутствует libvosk.so.' }
    $native = Join-Path $nativeParent 'arm64-v8a'
    if (Test-Path -LiteralPath $native) { Remove-Item -LiteralPath $native -Recurse -Force }
    Move-Item -LiteralPath $stage -Destination $native
} finally {
    $zip.Dispose()
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

# Берём оригинальную картинку из официального пакета; не перерисовываем логотип.
$branding = Join-Path $cache 'google-translate-attribution.zip'
if (!(Test-Path -LiteralPath $branding)) {
    Write-Host 'Загрузка официальной атрибуции Google Translate…'
    Get-HttpsFile $config.AttributionZipUrl $branding (16MB)
}
$badge = Join-Path $script:Root 'src\Mostik.Android\Resources\drawable-nodpi\google_translate_badge.png'
New-Item -ItemType Directory -Force -Path (Split-Path $badge -Parent) | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($branding)
try {
    $candidates = @($zip.Entries | Where-Object { $_.FullName -match '\.png$' -and $_.FullName -notmatch '(^|/)__MACOSX/|(^|/)\._' })
    if ($BadgeEntry) {
        $selected = @($candidates | Where-Object { $_.FullName -ceq $BadgeEntry })
    } else {
        $selected = @($candidates | Where-Object { $_.Name -match '(?i)powered' -and $_.Name -notmatch '(?i)white|grey|gray|mono|dark|negative' } | Sort-Object FullName)
        if ($selected.Count -eq 0) { $selected = @($candidates | Where-Object { $_.Name -match '(?i)color|colour' } | Sort-Object FullName) }
        if ($selected.Count -eq 0 -and $candidates.Count -eq 1) { $selected = $candidates }
        if ($selected.Count -gt 1) { $selected = @($selected[0]) }
    }
    if ($selected.Count -ne 1) {
        $names = ($candidates | ForEach-Object { $_.FullName }) -join "`n"
        throw "Укажите оригинальный цветной powered-by бейдж: -BadgeEntry 'путь/в/zip.png'. Доступно:`n$names"
    }
    $entry = $selected[0]
    if ($entry.Length -gt 2MB) { throw 'Неожиданно большой бейдж.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $badge, $true)
    $signature = [IO.File]::ReadAllBytes($badge)
    if ($signature.Length -lt 8 -or [BitConverter]::ToString($signature[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') {
        Remove-Item -LiteralPath $badge -Force
        throw 'Атрибуция не является PNG.'
    }
    $selectedName = $entry.FullName
} finally { $zip.Dispose() }

[ordered]@{
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    VoskVersion = $config.VoskVersion
    VoskUrl = $config.VoskAarUrl
    VoskSha256 = $actual
    VoskHashSource = $hashSource
    BadgeUrl = $config.AttributionZipUrl
    BadgeEntry = $selectedName
    BadgeZipSha256 = (Get-FileHash -LiteralPath $branding -Algorithm SHA256).Hash.ToLowerInvariant()
    BadgeSha256 = (Get-FileHash -LiteralPath $badge -Algorithm SHA256).Hash.ToLowerInvariant()
    BadgeVisualReview = 'NOT_TESTED: требуется проверить оригинальный powered-by бейдж на экране'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'build-assets-report.json') -Encoding UTF8
Write-Host 'Нативные библиотеки и оригинальный бейдж подготовлены. Компиляция ещё не выполнялась.'
