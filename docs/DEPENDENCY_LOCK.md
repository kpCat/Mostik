# Закрепление зависимостей

Состояние исходной поставки: версия SDK-политики и прямой NuGet заданы в исходниках.
После реального restore 8 сентября 2026 сохранены автоматически созданные
`packages.lock.json` для `Mostik.Core`, `Mostik.Android` и `Mostik.Core.Smoke`.

## Vosk Android AAR 0.3.75

Проверено 8 сентября 2026.

- URL: `https://repo.maven.apache.org/maven2/com/alphacephei/vosk-android/0.3.75/vosk-android-0.3.75.aar`.
- Размер AAR: 13 472 638 байт.
- SHA-256: `ab2f8b91ac8051561aa325546b35fed9a68b36b8121bac5c6fb927525c4adfad`.
- Источник SHA: официальный same-origin sidecar Maven Central `.aar.sha256`; это
  проверка целостности канала и файла, а не независимый trust anchor.
- `Get-BuildAssets.ps1` извлёк оригинальный `jni/arm64-v8a/libvosk.so` размером
  10 042 800 байт; его SHA-256:
  `06965ebb4e5eb3a9e4a815755e178d9553392e3fe8d3d368ac46952ad3694173`.
- В собранном APK присутствует ровно один `lib/arm64-v8a/libvosk.so`; иных ABI для
  Vosk в APK нет. Обнаруженные имена динамических зависимостей — только системные
  Android `libc.so`, `libdl.so`, `liblog.so`, `libm.so`.

Официальная атрибуция загружена из URL, закреплённого в `data/build-assets.json`.
Выбран оригинальный `png/color-regular.png`; визуально подтверждён цветной badge
Google Translate. SHA-256 ZIP:
`1cf5975466881127a227d4c0510518a83542eacff57d0ac5240430263035ee1b`, SHA-256 PNG:
`1ce4cf9db4cd6c8ae6f6d49542264f5334f40ac79139d51c145a4e039d26323d`.

SHA и размеры двух моделей распознавания уже закреплены по опубликованному манифесту;
локальное подтверждение официально скачанных ZIP ещё не выполнялось. См. MODELS.md.

ML Kit модели управляются SDK, у приложения нет собственной версии/контрольной суммы
этих внутренних моделей. Не выдавать их за воспроизводимо закреплённые пакеты.
