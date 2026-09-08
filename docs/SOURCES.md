# Источники технических решений

Проверены через веб 8 сентября 2026. Это документация/метаданные, а не результат
компиляции или испытаний. При смене версий проверять актуальность ещё раз.

| Решение | Первичный источник |
|---|---|
| Vosk офлайн, модели RU/PL, лицензии, общая оценка памяти | https://alphacephei.com/vosk/models |
| Vosk C API и формат PCM | https://raw.githubusercontent.com/alphacep/vosk-api/master/src/vosk_api.h |
| Точные SHA и размеры опубликованных ZIP (манифест зеркала) | https://huggingface.co/rhasspy/vosk-models/commit/e7ac2109d134b5f2404ba95389b2fb51916d4cab |
| Нативный Vosk AAR | https://repo.maven.apache.org/maven2/com/alphacephei/vosk-android/0.3.75/vosk-android-0.3.75.aar |
| .NET binding, версия 117.0.3.8 и платформы | https://www.nuget.org/packages/Xamarin.Google.MLKit.Translate/117.0.3.8 |
| Перевод/управление моделями Android | https://developers.google.com/ml-kit/language/translation/android |
| Ограничения качества и английский промежуточный язык | https://developers.google.com/ml-kit/language/translation |
| Данные ML Kit, локальная обработка и технические метрики | https://developers.google.com/ml-kit/terms |
| Требования к атрибуции | https://developers.google.com/ml-kit/language/translation/translation-terms |
| Графика и disclaimer | https://docs.cloud.google.com/translate/attribution?hl=en |
| Официальный архив бейджей | https://docs.cloud.google.com/static/translate/images/google-translate-attribution.zip |
| TTS и окончание/ошибки озвучки | https://developer.android.com/reference/android/speech/tts/TextToSpeech |
| Локальность Voice | https://developer.android.com/reference/android/speech/tts/Voice |
| Признак ещё не установленного голоса | https://developer.android.com/reference/android/speech/tts/TextToSpeech.Engine |
| Установка .NET for Android | https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/net-android |
| InstallAndroidDependencies и JDK 21 | https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies |
| AndroidNativeLibrary / ABI | https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items |
| SignAndroidPackage | https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-targets |
| Поддержка Visual Studio 2026 | https://learn.microsoft.com/en-us/visualstudio/releases/2026/compatibility |

Прямые бинарные URL AAR/бейджей в среде подготовки не скачались: их существование и
содержимое должен повторно подтвердить скрипт на компьютере пользователя. Нет заявления,
что SHA AAR, набор ELF-зависимостей или внешний вид бейджа здесь проверены.
