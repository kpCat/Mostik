# Сборка на Windows / Visual Studio 2026

## Требования

.NET 10 SDK, .NET for Android workload, Android SDK, Microsoft OpenJDK 21 и доступ
к NuGet/Maven/официальным ресурсам. Microsoft рекомендует цель InstallAndroidDependencies,
подбирающую необходимые компоненты под target framework. Источники: SOURCES.md.
В этом проекте `net10.0-android`, ABI arm64-v8a, минимум API 26. Эмулятор x86 не поддержан.

`global.json` запрашивает 10.0.100 с rollForward latestFeature внутри .NET 10.
Не откатывайте уже установленный совместимый .NET 10. Не используйте неподдержанную
превью-версию лишь ради обхода ошибки. Workload должен соответствовать выбранному SDK.

## Диагностика

Откройте PowerShell в корне репозитория:

```powershell
.\scripts\Doctor.ps1
```

Для IDE-компонентов используйте Visual Studio Installer → Изменить → разработка .NET MAUI
с Android-компонентами либо отдельный .NET for Android, если доступен в вашей редакции.
MAUI workload устанавливает инструменты, но сам проект не использует MAUI UI.
Наличие VS само по себе не означает наличие Android workload.

## Изолированная установка SDK/JDK

Когда .NET 10 уже установлен, Android workload отсутствует и вы согласны с лицензиями:

```powershell
.\scripts\Setup-Android.ps1 -InstallWorkload -AcceptLicenses
```

При уже установленном workload опустите `-InstallWorkload`. SDK/JDK помещаются в `.tools`
данного репозитория; PATH и JAVA_HOME глобально не меняются. Если инструменты управляются
Visual Studio и dotnet сообщает об этом, используйте Installer, не ломайте запись MSI.
Если лицензии или UAC требуют действий пользователя, Codex обязан сообщить об этом.
Не отключайте ExecutionPolicy системно: при блокировке скачанных файлов изучите их
и разблокируйте только эти скрипты стандартным способом Windows.

## Нативные зависимости и атрибуция

```powershell
.\scripts\Get-BuildAssets.ps1
```

Скрипт получает Vosk Android AAR из Maven Central, проверяет SHA-256 и извлекает только
ARM64 `.so`. Java-часть AAR не добавляется. SHA берётся из закреплённого значения
`data/build-assets.json`; пока оно null, используется официальный `.sha256` того же сервера.
Это проверка целостности канала и файла, а не независимый криптографический trust anchor.
Первый Codex должен проверить артефакт, зафиксировать SHA и оформить DEPENDENCY_LOCK.md.
Скачанные файлы и `.so` в исходный ZIP не включены и при подготовке не проверялись.

Официальный пакет атрибуции Google скачивается отдельно. Если выбор PNG неоднозначен,
скрипт выводит имена: укажите оригинальный цветной `powered by Google Translate` через
`-BadgeEntry 'имя/файла.png'`. Изображение нельзя заменять самодельным логотипом или
пустой картинкой. Проверить выбранный файл визуально при первой сборке обязательно.
Отчёт о URL и контрольных суммах: `artifacts/build-assets-report.json`.

## Сборка

```powershell
.\scripts\Build.ps1
```

После подготовки ресурсов допускается `-SkipAssets`, когда кеш уже проверен.
Скрипт делает restore, выполняет Core.Smoke и `SignAndroidPackage` в Debug.
Выход: `artifacts\Mostik-0.1.0-debug.apk` и его SHA-256. Это полноценный отладочный APK
с включёнными assemblies, а не сборка, требующая Visual Studio Fast Deployment.
Результаты получатся только после реально успешной компиляции и упаковки.

Для IDE после подготовки откройте `Mostik.sln`, стартовый проект `Mostik.Android`.
Если используете `.tools`, задайте AndroidSdkDirectory/JavaSdkDirectory в настройках
IDE либо собирайте скриптом — IDE не обязана автоматически находить эти каталоги.

Отдельно Core:

```powershell
dotnet run --project tests/Mostik.Core.Smoke/Mostik.Core.Smoke.csproj -c Release
```

## Установка

```powershell
.\scripts\Install.ps1
# Для нескольких устройств:
.\scripts\Install.ps1 -Serial 'серийный_номер'
```

USB-отладку и доступ этого компьютера нужно подтвердить на телефоне. Скрипт делает
`adb install -r`; не выполняет uninstall и не удаляет модели. Если Android отклоняет
другую подпись, сохраните прежний ключ или запросите согласие на переустановку с потерей данных.
Подпись здесь debug: для публичного распространения нужен отдельный релизный процесс.
Ключи не хранить в Git/архивах задач. Play Store, 16 KB pages и релизный trimming
в первую аппаратную приёмку не входят и не объявляются проверенными.
