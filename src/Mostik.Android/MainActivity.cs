using System.Diagnostics;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Mostik.Core;
using Mostik.Mobile.Services;
using AudioStream = Android.Media.Stream;
using AView = Android.Views.View;

namespace Mostik.Mobile;

[Activity(Label = "Мостик", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Android.App.Activity
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private Task _work = Task.CompletedTask;
    private ModelStore _models = null!;
    private VoskSpeechService _recognition = null!;
    private OfflineTranslator _translator = null!;
    private OfflineSpeechService _speech = null!;
    private Direction _direction = Direction.RuToPl;
    private Direction _translatedDirection = Direction.RuToPl;
    private bool _busy, _listening, _destroyed, _foreground, _readyToSpeak, _preparingDownloads;
    private string _translatedText = "", _lastError = "нет";
    private long _lastTranslationMs;
    private int _resumeVersion;
    private readonly List<AView> _idleControls = new();
    private Button _ru = null!, _pl = null!, _cancel = null!, _repeat = null!;
    private TextView _status = null!, _readiness = null!, _sourceLabel = null!, _targetLabel = null!, _target = null!;
    private EditText _source = null!;
    private CheckBox _autoEnd = null!, _review = null!, _wifiOnly = null!, _autoSpeak = null!;
    private LinearLayout _setup = null!;
    private ProgressBar _level = null!;
    private ImageView _badge = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        VolumeControlStream = AudioStream.Music;
        _models = new ModelStore(ApplicationContext!);
        _recognition = new VoskSpeechService(_models);
        _translator = new OfflineTranslator();
        _speech = new OfflineSpeechService(ApplicationContext!);
        BuildUi();
        _setup.Visibility = _models.Models.All(_models.IsReady) ? ViewStates.Gone : ViewStates.Visible;
        SetStatus("Сначала проверьте языковые пакеты и офлайн-голоса.");
    }

    protected override void OnResume()
    {
        base.OnResume();
        _foreground = true;
        int version = ++_resumeVersion;
        _ = RefreshAfterResumeAsync(version, _work);
    }

    private async Task RefreshAfterResumeAsync(int version, Task previous)
    {
        try
        {
            await previous;
            if (_destroyed || !_foreground || version != _resumeVersion || _busy) return;
            Start(ct => RefreshAsync(ct, resetTts: true), "Проверка локальных компонентов…");
        }
        catch (Exception ex) { _lastError = "resume:" + ex.GetType().Name; }
    }

    protected override void OnPause()
    {
        _foreground = false;
        ++_resumeVersion;
        CancelCurrent();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _destroyed = true;
        _lifetime.Cancel();
        CancelCurrent();
        _ = DisposeAfterWorkAsync(_work);
        base.OnDestroy();
    }

    private async Task DisposeAfterWorkAsync(Task work)
    {
        try { await work; } catch (Exception) { }
        try
        {
            _recognition.Dispose();
            _translator.Dispose();
            _speech.Dispose();
            _models.Dispose();
            _lifetime.Dispose();
        }
        catch (Exception) { /* Не записываем содержимое разговора даже при ошибке завершения. */ }
    }

    private void Start(Func<CancellationToken, Task> operation, string status)
    {
        if (_busy || _destroyed || !_foreground) return;
        _work = RunOperationAsync(operation, status);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action, string status)
    {
        _busy = true;
        _listening = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = cancellation;
        SetStatus(status);
        RenderControls();
        try { await action(cancellation.Token); }
        catch (System.OperationCanceledException)
        {
            SetStatus(_preparingDownloads
                ? "Остановлено. Уже начатая загрузка ML Kit может завершиться в фоне; новые загрузки не запускаются."
                : "Остановлено. Можно начать следующую реплику.");
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name;
            SetStatus(FriendlyError(ex));
        }
        finally
        {
            _operation = null;
            _busy = _listening = _readyToSpeak = _preparingDownloads = false;
            if (!_destroyed)
            {
                _level.Progress = 0;
                Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
                RenderControls();
            }
        }
    }

    private void CancelCurrent()
    {
        _operation?.Cancel();
        _recognition?.FinishRecording();
        _speech?.Stop();
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        DllNotFoundException => "В APK отсутствует Android-библиотека Vosk или её зависимость. Нужна исправленная сборка.",
        EntryPointNotFoundException => "Android-библиотека Vosk несовместима с обёрткой. Нужна исправленная сборка.",
        OutOfMemoryException => "Телефону не хватило памяти. Закройте другие приложения и перезапустите Мостик.",
        HttpRequestException => "Пакет не удалось скачать. Проверьте сеть. Частичная модель не будет использоваться.",
        TimeoutException => "Компонент не ответил вовремя. Остановите операцию и проверьте пакеты и голоса.",
        _ => ex.Message
    };

    private void SetStatus(string message)
    {
        if (_destroyed) return;
        _status.Text = message;
    }

    private void SpeakButton(Direction direction)
    {
        if (_busy)
        {
            if (_listening && _direction == direction)
            {
                _recognition.FinishRecording();
                _listening = false;
                RenderControls();
                SetStatus("Завершаю распознавание…");
            }
            return;
        }
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted)
        {
            RequestPermissions([Manifest.Permission.RecordAudio], 20);
            return;
        }
        _direction = direction;
        Start(async ct =>
        {
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            _sourceLabel.Text = direction.Label + " · распознанный текст";
            _source.Text = "";
            _target.Text = "";
            _translatedText = "";
            _badge.Visibility = ViewStates.Gone;
            bool acceptProgress = true;
            var progress = new Progress<ListeningUpdate>(value =>
            {
                if (!acceptProgress || _destroyed || ct.IsCancellationRequested || !_foreground) return;
                if (value.Stage == "loading") { SetStatus("Загружаю модель речи. Пока не говорите…"); return; }
                if (!_readyToSpeak)
                {
                    _readyToSpeak = _listening = true;
                    RenderControls();
                    SetStatus("Говорите. Повторное нажатие этой же кнопки завершит фразу.");
                }
                _source.Text = value.Text;
                _level.Progress = value.Level;
            });
            string text;
            try { text = await _recognition.ListenAsync(direction.Source, _autoEnd.Checked, progress, ct); }
            finally { acceptProgress = false; }
            ct.ThrowIfCancellationRequested();
            _listening = false;
            _source.Text = text;
            RenderControls();
            if (_review.Checked)
            {
                SetStatus("Проверьте распознанный текст и нажмите кнопку перевода нужного направления ниже.");
                return;
            }
            await TranslateAndSpeakAsync(text, direction, ct);
        }, "Подготовка микрофона…");
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 20) SetStatus(grantResults.Length > 0 && grantResults[0] == Permission.Granted
            ? "Микрофон разрешён. Теперь нажмите «Я говорю» или «Собеседник говорит»."
            : "Нет разрешения на микрофон. Его можно включить в настройках Android; ручной ввод работает и без него.");
    }

    private async Task TranslateAndSpeakAsync(string text, Direction direction, CancellationToken ct)
    {
        _direction = direction;
        _target.Text = "";
        _translatedText = "";
        _badge.Visibility = ViewStates.Gone;
        _sourceLabel.Text = direction.Label + " · исходный текст";
        SetStatus("Перевожу на телефоне…");
        var timer = Stopwatch.StartNew();
        string translated = await _translator.TranslateAsync(text, direction, ct);
        ct.ThrowIfCancellationRequested();
        _lastTranslationMs = timer.ElapsedMilliseconds;
        _translatedDirection = direction;
        _translatedText = translated;
        _targetLabel.Text = direction.Target == "pl" ? "Перевод на польский" : "Перевод на русский";
        _target.Text = translated;
        _badge.Visibility = ViewStates.Visible;
        if (_autoSpeak.Checked)
        {
            SetStatus("Озвучиваю перевод…");
            await _speech.SpeakAsync(translated, direction.Target, ct);
        }
        SetStatus("Готово. Следующая реплика — любая из двух больших кнопок.");
    }

    private async Task RefreshAsync(CancellationToken ct, bool resetTts = false)
    {
        _models.RemoveIncompleteDownloads();
        bool translationReady = false;
        string ttsError = "";
        try
        {
            if (resetTts) await _speech.ReinitializeAsync(ct); else await _speech.InitializeAsync(ct);
        }
        catch (System.OperationCanceledException) { throw; }
        catch (Exception ex) { ttsError = "\nСинтезатор: " + FriendlyError(ex); }
        try { translationReady = await _translator.IsReadyAsync(ct); }
        catch (System.OperationCanceledException) { throw; }
        catch (Exception ex) { _lastError = "MLKit:" + ex.GetType().Name; }
        ct.ThrowIfCancellationRequested();
        _readiness.Text = string.Join("\n", _models.Models.Select(m =>
            $"{m.Title}: {(_models.IsReady(m) ? "установлена" : "нет пакета")}"))
            + $"\nПеревод RU ⇄ PL: {(translationReady ? "пакеты установлены" : "нужна загрузка/проверка")}"
            + $"\nГолос RU: {_speech.Describe("ru")}\nГолос PL: {_speech.Describe("pl")}" + ttsError;
        bool complete = _models.Models.All(_models.IsReady) && translationReady && _speech.HasVoice("ru") && _speech.HasVoice("pl");
        SetStatus(complete
            ? "Компоненты установлены. Для подтверждения офлайн-работы выполните проверку в авиарежиме и обе кнопки разговора."
            : "Ещё не всё готово. Откройте «Пакеты»: загрузите модели и установите оба офлайн-голоса.");
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        _preparingDownloads = true;
        bool wifiOnly = _wifiOnly.Checked;
        if (wifiOnly && !HasWifi()) throw new InvalidOperationException("Включён режим загрузки по Wi-Fi. Подключитесь к Wi-Fi или снимите галочку.");
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        bool acceptProgress = true;
        var progress = new Progress<string>(message => { if (acceptProgress && !ct.IsCancellationRequested) SetStatus(message); });
        try
        {
            foreach (var model in _models.Models)
            {
                await _models.InstallAsync(model, wifiOnly, progress, ct);
                ct.ThrowIfCancellationRequested();
            }
            await _translator.DownloadAsync(wifiOnly, progress, ct);
        }
        finally { acceptProgress = false; }
        await RefreshAsync(ct);
    }

    private bool HasWifi()
    {
        var manager = (global::Android.Net.ConnectivityManager?)GetSystemService(ConnectivityService);
        using var capabilities = manager?.GetNetworkCapabilities(manager.ActiveNetwork);
        return capabilities?.HasTransport(global::Android.Net.TransportType.Wifi) == true;
    }

    private async Task OfflineCheckAsync(CancellationToken ct)
    {
        var manager = (global::Android.Net.ConnectivityManager?)GetSystemService(ConnectivityService);
        if (manager?.ActiveNetwork is not null)
            throw new InvalidOperationException("Включите авиарежим и отдельно выключите Wi-Fi. Приложение не переключает сеть автоматически.");
        await RefreshAsync(ct);
        SetStatus("Без сети: проверяю русский → польский…");
        string pl = await _translator.TranslateAsync("Здравствуйте. Сколько это стоит?", Direction.RuToPl, ct);
        await _speech.SpeakAsync(pl, "pl", ct);
        SetStatus("Без сети: проверяю польский → русский…");
        string ru = await _translator.TranslateAsync("Dzień dobry. Ile to kosztuje?", Direction.PlToRu, ct);
        await _speech.SpeakAsync(ru, "ru", ct);
        SetStatus("Перевод и команды озвучки выполнены без активной сети. Убедитесь, что оба голоса слышны. Затем проверьте обе кнопки микрофона.");
    }

    private void OpenTtsSettings(bool install)
    {
        try { StartActivity(install ? _speech.VoiceInstallIntent() : new Intent("com.android.settings.TTS_SETTINGS")); }
        catch (ActivityNotFoundException)
        {
            try { StartActivity(new Intent(global::Android.Provider.Settings.ActionSettings)); }
            catch (ActivityNotFoundException) { SetStatus("Откройте настройки Android вручную: язык и ввод → синтез речи."); }
        }
    }

    private void ShowInfo()
    {
        using var privacy = new StreamReader(Assets!.Open("privacy.txt"));
        using var thirdParty = new StreamReader(Assets.Open("third_party.txt"));
        new AlertDialog.Builder(this).SetTitle("О приложении и данных")!
            .SetMessage(privacy.ReadToEnd() + "\n\n" + thirdParty.ReadToEnd())!
            .SetPositiveButton("Закрыть", (_, _) => { })!.Show();
    }

    private void CopyDiagnostics()
    {
        string text = $"Mostik 0.1.0\nModel: {Build.Manufacturer} {Build.Model}\nAndroid: {Build.VERSION.Release}; API {(int)Build.VERSION.SdkInt}"
            + $"\nABI: {string.Join(",", Build.SupportedAbis ?? [])}\n{_readiness.Text}"
            + $"\nTTS engine: {_speech.EngineName}\nModel load: {_recognition.LastModelLoadMilliseconds} ms"
            + $"\nTranslation: {_lastTranslationMs} ms\nLast error type: {_lastError}"
            + "\nПроверку телефона и авиарежима подтвердить вручную; текста разговора и аудио здесь нет.";
        var clipboard = (ClipboardManager?)GetSystemService(ClipboardService);
        if (clipboard is not null)
            clipboard.PrimaryClip = ClipData.NewPlainText("Мостик — диагностика", text);
        SetStatus("Техническая диагностика скопирована. Текст разговоров в неё не включён.");
    }

    private int Dp(float value) => (int)(Resources!.DisplayMetrics!.Density * value + 0.5f);
    private TextView Label(string text, float size = 15)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(Color.ParseColor("#24343E"));
        view.SetPadding(Dp(4), Dp(7), Dp(4), Dp(7));
        return view;
    }
    private Button Button(string text, Action click, bool primary = false)
    {
        var button = new Button(this) { Text = text, TextSize = primary ? 20 : 15 };
        button.SetAllCaps(false);
        button.SetMinHeight(Dp(primary ? 104 : 52));
        button.Click += (_, _) => click();
        if (primary) button.SetTextColor(Color.White);
        _idleControls.Add(button);
        return button;
    }
    private void Add(LinearLayout parent, AView child)
    {
        var layout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(3), 0, Dp(3));
        parent.AddView(child, layout);
    }
    private CheckBox Check(string text, string preference, bool fallback)
    {
        var preferences = GetSharedPreferences("settings", FileCreationMode.Private)!;
        var check = new CheckBox(this) { Text = text, TextSize = 15, Checked = preferences.GetBoolean(preference, fallback) };
        check.CheckedChange += (_, _) => preferences.Edit()!.PutBoolean(preference, check.Checked)!.Apply();
        _idleControls.Add(check);
        return check;
    }
    private void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(24));
        root.SetFitsSystemWindows(true);
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(root);
        SetContentView(scroll);
        Add(root, Label("Мостик", 30));
        Add(root, Label("Русский ⇄ польский · разговор по очереди", 16));
        _status = Label("", 16);
        _status.SetTypeface(null, TypefaceStyle.Bold);
        Add(root, _status);
        Add(root, Button("Пакеты и голоса", () => _setup.Visibility = _setup.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible));
        _setup = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical, Visibility = ViewStates.Gone };
        Add(root, _setup);
        Add(_setup, Label("Подготовка дома: модели речи ≈ 95 МБ, затем пакеты перевода и два офлайн-голоса. После этого проверьте работу без сети."));
        _readiness = Label("Пакеты ещё не проверены.", 14);
        Add(_setup, _readiness);
        _wifiOnly = Check("Загружать пакеты только по Wi-Fi", "wifi_only", true);
        Add(_setup, _wifiOnly);
        Add(_setup, Button("Загрузить языковые пакеты", () => Start(DownloadAsync, "Подготовка загрузки…")));
        Add(_setup, Button("Установить офлайн-голоса", () => OpenTtsSettings(true)));
        Add(_setup, Button("Настройки синтеза речи Android", () => OpenTtsSettings(false)));
        Add(_setup, Button("Послушать русский голос", () => Start(ct => _speech.SpeakAsync("Здравствуйте. Русский голос работает.", "ru", ct), "Проверка русского голоса…")));
        Add(_setup, Button("Послушать польский голос", () => Start(ct => _speech.SpeakAsync("Dzień dobry. Polski głos działa.", "pl", ct), "Проверка польского голоса…")));
        Add(_setup, Button("Перепроверить компоненты", () => Start(ct => RefreshAsync(ct, true), "Проверка…")));
        Add(_setup, Button("Проверка перевода и голосов без сети", () => Start(OfflineCheckAsync, "Проверка авиарежима…")));
        Add(_setup, Button("Скопировать диагностику для Codex", CopyDiagnostics));
        Add(_setup, Button("О приложении и данных", ShowInfo));
        _ru = Button("Я говорю\nРусский → польский\nПеревод с Google", () => SpeakButton(Direction.RuToPl), true);
        _ru.BackgroundTintList = ColorStateList.ValueOf(Color.ParseColor("#176B64"));
        _pl = Button("Собеседник говорит\nПольский → русский\nПеревод с Google", () => SpeakButton(Direction.PlToRu), true);
        _pl.BackgroundTintList = ColorStateList.ValueOf(Color.ParseColor("#315B87"));
        Add(root, _ru); Add(root, _pl);
        _cancel = Button("Остановить / отменить", CancelCurrent);
        Add(root, _cancel);
        _level = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
        Add(root, _level);
        _autoEnd = Check("Завершать фразу после паузы", "automatic_end", true);
        _review = Check("Проверять текст перед переводом", "review_before_translation", false);
        _autoSpeak = Check("Озвучивать перевод автоматически", "automatic_speech", true);
        Add(root, _autoEnd); Add(root, _review); Add(root, _autoSpeak);
        _sourceLabel = Label("Исходный текст · можно исправить или ввести вручную", 16);
        Add(root, _sourceLabel);
        _source = new EditText(this) { TextSize = 20, Hint = "Здесь появится распознанная речь", Gravity = GravityFlags.Top,
            InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine };
        _source.SaveEnabled = false;
        _source.SetMinLines(2); _source.SetMaxLines(8);
        _source.SetFilters([new global::Android.Text.InputFilterLengthFilter(TextPolicy.MaxCharacters)]);
        _idleControls.Add(_source); Add(root, _source);
        Add(root, Label("Перевести исправленный текст с Google:", 14));
        Add(root, Button("Текст: русский → польский", () => Start(ct => TranslateAndSpeakAsync(_source.Text ?? "", Direction.RuToPl, ct), "Перевод…")));
        Add(root, Button("Текст: польский → русский", () => Start(ct => TranslateAndSpeakAsync(_source.Text ?? "", Direction.PlToRu, ct), "Перевод…")));
        _targetLabel = Label("Перевод", 16); Add(root, _targetLabel);
        _target = Label("", 26); _target.SaveEnabled = false; _target.SetTextIsSelectable(true); Add(root, _target);
        _badge = new ImageView(this) { ContentDescription = "powered by Google Translate", Visibility = ViewStates.Gone };
        _badge.SetImageResource(Resource.Drawable.google_translate_badge);
        _badge.SetAdjustViewBounds(true); _badge.SetMaxHeight(Dp(40));
        _badge.SetScaleType(ImageView.ScaleType.FitStart);
        Add(root, _badge);
        Add(root, Label("Автоматический перевод может ошибаться. Проверяйте числа, имена и отрицания.", 13));
        _repeat = Button("Повторить озвучку перевода", () => Start(ct => _speech.SpeakAsync(_translatedText, _translatedDirection.Target, ct), "Повторяю перевод…"));
        Add(root, _repeat);
        Add(root, Button("Очистить разговор", () => { _source.Text = _target.Text = _translatedText = ""; _badge.Visibility = ViewStates.Gone; RenderControls(); SetStatus("Разговор очищен."); }));
        RenderControls();
    }

    private void RenderControls()
    {
        if (_destroyed) return;
        foreach (var view in _idleControls) view.Enabled = !_busy;
        _ru.Enabled = !_busy || (_listening && _direction == Direction.RuToPl);
        _pl.Enabled = !_busy || (_listening && _direction == Direction.PlToRu);
        _ru.Text = _listening && _direction == Direction.RuToPl ? "Закончить мою фразу" : "Я говорю\nРусский → польский\nПеревод с Google";
        _pl.Text = _listening && _direction == Direction.PlToRu ? "Закончить фразу собеседника" : "Собеседник говорит\nПольский → русский\nПеревод с Google";
        _ru.Alpha = _ru.Enabled ? 1f : 0.45f;
        _pl.Alpha = _pl.Enabled ? 1f : 0.45f;
        _cancel.Enabled = _busy;
        _repeat.Enabled = !_busy && !string.IsNullOrWhiteSpace(_translatedText);
    }
}
