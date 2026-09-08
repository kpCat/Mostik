# GOAL-001 — результат первой сборки

Дата: 2026-09-08. Рабочий каталог: `C:\Users\ZBook\Mostik`.
Репозиторий: `https://github.com/kpCat/Mostik.git`, ветка `master`.

## Статусы приёмки

| Проверка | Статус | Основание |
|---|---|---|
| SOURCE_BUILD | PASS | Финальный `scripts/Build.ps1`, exit 0; C# и Android package собраны |
| CORE_SMOKE | PASS | 38 PASS, 0 FAIL; отдельный запуск exit 0 |
| APK_PACKAGED | PASS | Подписанный standalone ARM64 debug APK создан и проверен |
| DEVICE_LAUNCH | NOT_TESTED | ADB запущен, список разрешённых устройств пуст |
| OFFLINE_RU_TO_PL | NOT_TESTED | Нет подключённого Redmi и установленных на нём runtime-пакетов |
| OFFLINE_PL_TO_RU | NOT_TESTED | Нет подключённого Redmi и установленных на нём runtime-пакетов |
| OFFLINE_TTS_RU_PL | NOT_TESTED | Оба голоса нужно услышать на реальном телефоне в авиарежиме |
| LATENCY_MEMORY | NOT_MEASURED | Без реального устройства P50/P95 и память не измерялись |

Финальных статусов FAIL нет. Отрицательный запуск `Install.ps1` с exit 1 ожидаемо
подтвердил отказ от установки, когда разрешённого ADB-устройства нет.

## Окружение

- Visual Studio Enterprise 2026: 18.0.11205.157.
- .NET SDK: 10.0.100.
- Android workload: 36.1.2/10.0.100, источник установки VS 18.0.11205.157.
- Android SDK Platform: API 36, revision 2; установлен также API 35.
- Android build-tools: 36.0.0; ADB 1.0.41, версия 36.0.0-13206524.
- JDK, реально выбранный Android build: Microsoft OpenJDK 21.0.8+9-LTS
  (`javasdkpath` подтверждён в сгенерированном `build.props`).
- Глобальный `JAVA_HOME` указывал на Temurin 25.0.4.1, но сборка его не использовала.
- Дополнительная установка SDK/JDK/workload не потребовалась: нужные версии уже были.

## Изменения

- Исправлены реальные конфликты типов .NET/Android: `Activity`,
  `CancellationToken`, `OperationCanceledException`, `Orientation`.
- Использованы подтверждённые API binding: `AudioRecordReadOptions.NonBlocking` и
  свойство `ClipboardManager.PrimaryClip`.
- TTS listener обрабатывает и обязательный старый, и актуальный error callback.
- Удалено двойное явное включение `libvosk.so`: .NET for Android уже добавляет его
  стандартным `AndroidNativeLibrary` item по пути `NativeLibs/arm64-v8a`.
- Windows-структурный validator теперь исключает только generated/ignored build output
  и читает исходники как UTF-8, поэтому работает и после реальной сборки с кириллицей.
- Реальный SHA-256 Vosk AAR закреплён в `data/build-assets.json` и описан в
  `docs/DEPENDENCY_LOCK.md`; сохранены созданные restore-файлы `packages.lock.json`.
- README и VALIDATION дополнены фактическим Windows-результатом без изменения истории
  исходной поставки.

Два направления RU ⇄ PL, Vosk C API, локальный ML Kit, запрет скрытой загрузки из
`TranslateAsync`, фильтр сетевых/notInstalled TTS-голосов и одна Vosk-модель в RAM
сохранены. Заглушки, облачный ASR/перевод и платные API не добавлялись.

## Native assets и APK

- Vosk AAR 0.3.75: 13 472 638 байт, SHA-256
  `ab2f8b91ac8051561aa325546b35fed9a68b36b8121bac5c6fb927525c4adfad`.
- Извлечённый ARM64 `libvosk.so`: 10 042 800 байт, SHA-256
  `06965ebb4e5eb3a9e4a815755e178d9553392e3fe8d3d368ac46952ad3694173`.
- Его обнаруженные системные зависимости: `libc.so`, `libdl.so`, `liblog.so`, `libm.so`.
- Официальный badge: `png/color-regular.png`; исходный и упакованный варианты
  визуально проверены, цветной знак Google Translate читаем.
- APK: `artifacts/Mostik-0.1.0-debug.apk`.
- Размер APK: 45 437 847 байт.
- SHA-256 APK:
  `736b80818607b327bea2cd132f5cb2d83aefd5c2ac6ab481c4ec9a09b3237ad7`.
- Подпись: Android Debug, RSA 3072; certificate SHA-256
  `03a71927ed16c3c6e02ca534eb3c86aaab9211117ab75b7b840d1f5fb1490a1b`.
  `apksigner`: v2=true, v3=true; exit 0. `zipalign -P 16`: exit 0.
- Package: `org.mostik.offline`, version 0.1.0 (code 1), min API 26, target API 36.
- APK содержит только ABI `arm64-v8a`, ровно по одному `libvosk.so` и
  `libtranslate_jni.so`, без повторяющихся ZIP entries; registrar-классы MainActivity,
  ML Kit completion listener и TTS listeners найдены в DEX.

## Выполненные команды и exit codes

- `git status --short --branch`, `git branch --show-current`, `git remote -v`: 0.
- `scripts/Doctor.ps1`: 0; обязательные SDK/workload обнаружены. Первый sandbox-запуск
  ADB внутри Doctor не поднял daemon; отдельный разрешённый запуск ниже успешен.
- `scripts/Get-BuildAssets.ps1`: финальный запуск внутри `Build.ps1` — 0. Первая попытка
  без сетевого разрешения получила socket access denied; проверка SHA не обходилась.
- `scripts/Build.ps1 -SkipAssets`: первый restore без сетевого разрешения — 1, NU1301.
- Первая разрешённая Android-компиляция: 1, выявила 2 конфликта типов; следующий pass:
  1, выявил ещё 7 binding-ошибок; после исправлений C# compile прошёл.
- Первый signing-pass в sandbox: 1, доступ к стандартному debug keystore запрещён;
  разрешённый повторный pass — 0.
- Финальный `scripts/Build.ps1`: 0; assets, restore, 38 Core smoke и APK выполнены.
- `dotnet restore Mostik.sln --locked-mode -v:minimal`: 0.
- `dotnet run --project tests/Mostik.Core.Smoke/Mostik.Core.Smoke.csproj -c Release --no-restore`: 0, 38/38.
- `scripts/validate_source.py --json artifacts/source-validation.json`: 0,
  STRUCTURE_PASS_ONLY, 35 checks.
- `apksigner verify --verbose --print-certs`: 0.
- `zipalign -c -P 16 4`: 0.
- `adb start-server`: 0; `adb devices -l`: 0, устройств нет.
- `scripts/Install.ps1`: 1, ожидаемый отказ из-за отсутствия ровно одного устройства.

## NOT_TESTED и известные риски

- Vosk-модели RU/PL не скачивались на телефон; опубликованные ZIP SHA/size не были
  повторно подтверждены реальной телефонной загрузкой.
- ML Kit download/inference, микрофон, поздние callback, отмена, оба TTS-голоса,
  авиарежим и 10+10 реплик требуют Redmi и остаются NOT_TESTED.
- В Xamarin.Google.MLKit.Translate 117.0.3.8 bundled AAR и downloaded AAR идентичны
  (оба SHA-256 `b6194f7b42034309cf8299784b2d5d70a82a2e9287fbe1650dea4d1f3ad1fe55`),
  но package targets дважды объявляет downloaded AAR. Сборка выдаёт два XA4301 warning;
  итоговый APK проверен и содержит одну копию `libtranslate_jni.so`. Runtime всё равно
  должен быть подтверждён на телефоне.
- Debug APK предназначен для личной установки, не для публикации в магазине.

## Git / публикация

- Исходный `origin` был `git@github.com:kpCat/Mostik.git`. Первая push-попытка, exit 1:
  `git@github.com: Permission denied (publickey). fatal: Could not read from remote repository.`
- `origin` изменён на явно указанный пользователем
  `https://github.com/kpCat/Mostik.git`; пользовательские файлы и история не менялись.
- IMPLEMENTATION_COMMIT_SHA:
  `8d4af7a5738975072eab92d8e7c28765d3aed145`.
- PUSH_IMPLEMENTATION: PASS, `git push -u origin master`, exit 0; без force.
- REMOTE_MASTER_VERIFICATION: PASS. После push `git rev-parse HEAD` и
  `git ls-remote origin refs/heads/master` вернули один SHA:
  `8d4af7a5738975072eab92d8e7c28765d3aed145`.

Этот post-push раздел публикуется отдельным коротким attestation-коммитом: commit не
может содержать собственный SHA или заранее доказать результат будущего push без
самореференции. Force-push и переписывание истории не использовались.
