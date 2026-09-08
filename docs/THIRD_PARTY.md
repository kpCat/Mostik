# Сторонние компоненты и уведомления

## Собственный код

Исходный код Мостика распространяется по MIT (LICENSE). Это не меняет условия
сторонних SDK, моделей, голосов и торговых знаков. Приложение не связано с Lingofonix
и не является продуктом или одобрением Google/Alpha Cephei/Microsoft.

## Vosk

Vosk API: Alpha Cephei, Apache License 2.0.
https://github.com/alphacep/vosk-api
Русская small-ru-0.22 и польская small-pl-0.22: Apache-2.0 по официальному каталогу.
https://alphacephei.com/vosk/models
Текст Apache-2.0 приложен в licenses/Apache-2.0.txt. Бинарные библиотеки и модели
в исходный архив не включены. При публичном распространении собранного APK необходимо
сохранить относящиеся к конкретному AAR notices и лицензии транзитивных native компонентов
(Kaldi/OpenFst и т. п.), а не считать этот краткий перечень завершённым юридическим аудитом.

## .NET и Microsoft bindings

.NET и используемые Microsoft bindings имеют собственные лицензии MIT и сопутствующие
уведомления. Пакет Xamarin.Google.MLKit.Translate предоставляет привязки; лицензия
привязки не распространяется автоматически на исходную Java-библиотеку Google.
https://www.nuget.org/packages/Xamarin.Google.MLKit.Translate/117.0.3.8

## Google ML Kit / Google Translate

Перевод в этом приложении выполняется Google ML Kit на устройстве. Автоматический
перевод может быть неточным, неполным или неуместным; проверяйте смысл перед использованием.
Google не предоставляет гарантий точности/надёжности перевода и связанных подразумеваемых
гарантий пригодности для конкретной цели. Приложение не связано с Google и не одобрено им.

Условия ML Kit: https://developers.google.com/ml-kit/terms
Правила перевода: https://developers.google.com/ml-kit/language/translation/translation-terms
Атрибуция: https://docs.cloud.google.com/translate/attribution?hl=en
Сервис Google Translate: https://translate.google.com/

В интерфейсе действия обозначены «Перевод с Google». Рядом с результатом используется
оригинальный powered-by бейдж из официального пакета; его нельзя перерисовывать.
Графика загружается скриптом сборки; после первой сборки её выбор и размещение требуется
проверить визуально. В этой исходной поставке графика не включена.

Обязательное уведомление Google (юридический оригинал):
THIS SERVICE MAY CONTAIN TRANSLATIONS POWERED BY GOOGLE. GOOGLE DISCLAIMS ALL WARRANTIES
RELATED TO THE TRANSLATIONS, EXPRESS OR IMPLIED, INCLUDING ANY WARRANTIES OF ACCURACY,
RELIABILITY, AND ANY IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT.

## Системные голоса

Голоса Android TTS поставляются выбранным движком. Мостик их не перепаковывает и не
распространяет. Наличие бесплатного офлайн-голоса конкретного языка зависит от движка;
приложение не обещает, что любой установленный TTS поддерживает польский офлайн.
Перед публикацией в магазине приложений проверить действующие условия всех компонентов.
