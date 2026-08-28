# AGENTS.md — 目标地图 (Goal Map / "ME")

Personal goal-management & health-tracking WPF desktop app. 100% local data, no server. UI text and README are in Chinese — keep new UI strings Chinese.

## Project layout

- `Fangan.slnx` solution; single project `ME/` (root namespace `ME`).
- `ME/Models/` — POCO data models (one file per entity: Goal, TaskItem, HealthRecord, MedicationRecord, …).
- `ME/Data/` — static repository classes per entity + `DatabaseHelper.cs` (contains static `JsonStore`).
- `ME/Services/` — app services (tray notifications, reminders, timers, backup, LLM, theme, sync, Xiaomi import, DPAPI `SecureStore`).
- `ME/Core/` — MVVM infra (`ViewModelBase`, `RelayCommand`, `EventAggregator`, `NavigationService`).
- `ME/ViewModels/`, `ME/Views/`, `ME/Converters/`, `ME/Resources/Styles.xaml`.
- Code-behind is deliberately heavy (e.g. `HealthView.xaml.cs` ~3000 lines) — this is a MVVM + code-behind hybrid, match the existing style of the file you edit rather than forcing pure MVVM.

## Build / run

```
dotnet build ME/ME.csproj        # no tests, no lint config in repo
dotnet run --project ME          # launches the WPF app (Windows only)
```

- Target `net8.0-windows`, `UseWPF` **and** `UseWindowsForms` (WinForms is used for the tray `NotifyIcon`).
- `<Nullable>disable</Nullable>` and `<ImplicitUsings>disable</ImplicitUsings>` — files need explicit `using` directives; don't add nullable annotations that don't match surrounding code.
- `DisableHardwareAcceleration=true` is intentional; don't remove.
- App version lives in `ME/ME.csproj` `<Version>`; bump it for user-facing changes. Commit style: `vX.Y.Z: <chinese summary>`.
- bin/obj/.vs are untracked (cleanup in v1.9.26) — never commit build artifacts.

## Storage architecture (important)

- No database. All data is JSON files via static `JsonStore` in `ME/Data/DatabaseHelper.cs`, stored under `%LocalAppData%\ME\JsonData\`.
- Repositories are static classes; reads go through `JsonStore.LoadWithCache<T>(fileName)` which has a **2-second in-memory cache**. `JsonStore.Save` invalidates the cache for that file — always save through `JsonStore.Save`, and if you read-modify-write, re-load after invalidation or read fresh to avoid stale cache data.
- Adding a new entity = new model in `ME/Models/`, new static repository in `ME/Data/` using `JsonStore`, then wire into views/services.
- Settings persist via `SettingsRepository` (also JSON); API keys are encrypted with DPAPI via `SecureStore` — never store keys in plain JSON.

## Known gotchas

- **Single tray icon**: `AppNotifier` borrows MainWindow's `NotifyIcon` for balloon tips instead of creating its own (v1.9.27). A temporary fallback icon auto-hides after 12s. Don't create additional persistent `NotifyIcon`s.
- Backup/restore (`BackupService`) writes to `%LocalAppData%\ME\` — health/medication data is included in backups; visions/reviews are not (as of v1.9.24).
- AI analysis (`LlmService`) targets any OpenAI-compatible provider configured in Settings; the default prompt is editable and persisted, with a restore-default option.
- Theme switching is runtime via DynamicResource from `Resources/Styles.xaml` (light/dark/system) — new styles must use DynamicResource references to stay theme-aware; ModernWpf 0.9.6 is the only UI package.
- Xiaomi health import expects the official privacy-center export zip and only parses sleep + weight.

## Before sensitive edits

Read `README.md` (feature map, Chinese) first when touching the Health module, backup scope, or tray/notification behavior — these areas changed repeatedly in recent versions and the README documents the intended behavior.
