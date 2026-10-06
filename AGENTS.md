# Sufler

WPF-телесуфлёр: скроллер текста с окном редактора настроек. UI-строки — русские, код и комментарии — английские.

## Commands
- Build: `dotnet build Sufler.sln -c Release`
- Test: `dotnet test tests/Sufler.Tests/Sufler.Tests.csproj -c Release` (92 кейса, только Core)
- Run: `dotnet run --project src/Sufler.App`
- Lint/форматирование: нет (`.editorconfig`, dotnet-format отсутствуют) — стиль держится на ревью
- SDK: .NET 10 (TFM `net10.0` / `net10.0-windows`)

## Architecture
- `src/Sufler.Core` — чистая логика без WPF: Settings, Scrolling, Script, HotKeys, Capture, Shell-интерфейсы. Не содержит `System.Windows`/`System.Drawing` — не добавлять.
- `src/Sufler.App` — WPF-оболочка, зависит только от Core (`Sufler.App.csproj:4`).
- Диск только из `App.xaml.cs` (composition root, `App.xaml.cs:16-19`): окна не пишут сами, только поднимают события (`MainWindow.xaml.cs:17-18`, `EditorWindow.xaml.cs:20-21`).
- «Интерфейс в Core, реализация в App»: `ISettingsStore`/`IScriptStore`/`IHotKeyService`/`ITrayIcon`/`ICaptureGuard`.
- Поток настроек: `EditorWindow.SettingsEdited` → `App.OnEditorSettingsEdited` (`App.xaml.cs:480`) → `AdoptLiveValues` → `_prompter.ApplySettings` + debounce-сохранение 400 мс. Размер окна намеренно не тянется из live-состояния (`App.xaml.cs:493-500`).

## Conventions
- `double?` в `SuflerSettings` = «default / ничего не сохранено»; `SanitizeSize` отбрасывает ≤0 и не-finite (`SuflerSettings.cs:87-88`).
- `Normalize()` обязан быть идемпотентным (`SuflerSettings.cs:43-46`, тест `Normalize_IsIdempotent`).
- `EditorWindow`: каждый обработчик начинается с `if (!_isReady || _suppressEvents)` — события приходят из BAML внутри `InitializeComponent()` (`EditorWindow.xaml.cs:46-52`).
- Диапазоны слайдера размера окна задаются только из кода (`EditorWindow.xaml.cs:27-31, 70-77`), не литералами в XAML.
- Doc-комментарии `///` — по-английски, с объяснением «почему».

## Tests
- `tests/Sufler.Tests/{Settings,Scrolling,Script,HotKeys}`, xunit.
- Обязательно покрыто и не ломать: границы и идемпотентность `Normalize`, HotKeys-нормализация, `ScriptText.Normalize`/`SplitLogicalLines`, поведение `ScrollEngine` (loop, clamp, NaN/≤0).
- `src/Sufler.App` не покрыт тестами вовсе (нет ссылки Tests→App) — правки в App проверяются сборкой и ревью.

## Forbidden
- `MainWindow.xaml:49-62` — ScrollViewer `Vertical="Hidden"` / `Horizontal="Disabled"`: смена ломает либо прокрутку, либо перенос строк.
- `ApplyNativeStyles` и capture affinity вызывать повторно по тем же точкам (`MainWindow.xaml.cs:221-227`, `App.xaml.cs:116-129`): WPF перетирает нативные стили.
- Stores не удаляют файлы пользователя: `File.Delete` только для собственного `.tmp`.
- `.github/workflows/release.yml` не менять без нужды (concurrency и `fail_on_unmatched_files` намеренные).
- `bin/`, `obj/`, `.vs/` не коммитить.

## Context
- Настройки: `%AppData%\Sufler\settings.json` — атомарная запись через `.tmp`, битый JSON отодвигается в `settings.corrupt.json`; рядом `scripts\*.txt|*.md`, лог `error.log`. Hot-reload с диска нет — только живое применение в памяти и чтение при старте.
- `ShutdownMode=OnExplicitShutdown`: окно скрывается, Alt+F4 сворачивает (`README.md:74`).
- CI один: push в main → self-contained релиз; PR-CI нет.
- Коммиты — conventional commits с scope, по-английски.
