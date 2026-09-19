<div align="center">

# ME — Personal Management System (Goals · Tasks · Time · Health)

[简体中文](README_CN.md) | **English**

A fully local personal management system for Windows: plan goals, track time, manage tasks and record health data — your data never leaves your device.

[![Release](https://img.shields.io/github/v/release/nailao946/ME)](https://github.com/nailao946/ME/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/nailao946/ME/total)](https://github.com/nailao946/ME/releases/latest)
![.NET 8](https://img.shields.io/badge/.NET-8-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%20WPF-purple)
![License](https://img.shields.io/badge/License-MIT-green)
[![Stars](https://img.shields.io/github/stars/nailao946/ME?style=social)](https://github.com/nailao946/ME/stargazers)

**⬇️ [Download the latest release](https://github.com/nailao946/ME/releases/latest)** · 📱 [Android version (ME-PE)](https://github.com/nailao946/ME-PE)

</div>

> **Note:** the app's UI is currently Chinese-only. The project is fully usable if you can read basic Chinese; issues and PRs in English are always welcome.

---

## Feature Overview

| Module | Features |
|--------|----------|
| 📋 Tasks | Multiple task types, date picker, tag filters, progress tracking, Pomodoro timer |
| 🎯 Goals | Goal categories, tag system, parent-child hierarchy, automatic progress |
| 📅 Calendar | Monthly view, color coding, detail sidebar, task stats (completion rate / days left / streak) |
| 🗺️ Goal Map | Tree visualization, progress rings, global overview |
| 📝 Review | Weekly / monthly / yearly stats, completion-rate trends, goal progress list |
| ⏱️ Time Tracking | Tag timers, Gantt chart, pie charts, Pomodoro, right-click management, floating timer window |
| 💚 Health | Sleep / body measurements / water / mood / uric acid / exercise / sedentary / medication / compare / AI analysis |
| ⚙️ Settings | Theme switching, border color, auto-start, system tray, backup/restore, cloud sync, AI providers |

---

## Health Module (💚)

Modeled after Apple's Health app, all data stored locally:

| Category | Features |
|----------|----------|
| 🏠 Overview | Body figure (tap a body part to see its data), detailed summary (height / weight / BMI / average sleep / daily water / uric acid / medication), export health report |
| 😴 Sleep | Log bedtime / wake time with automatic duration; today / 7-day / 30-day stats and charts |
| ⚖️ Body | Log weight + height, automatic BMI and body assessment (underweight / normal / overweight / obese), weight trend chart |
| 💧 Water | Custom water containers (name + ml, capacity auto-computed from diameter / height), +1/-1 per container, daily / weekly / monthly stats and goal rate |
| 😊 Mood | One-tap daily mood (😊😐😔😢), 7-day distribution, 30-day timeline |
| 🩸 Uric acid | Log uric acid values (with measurement time & sex), automatic normal-range check (male 149–416 / female 89–357) with color coding, 30-day trend |
| 🏃 Exercise | Custom exercise items (name / target / unit), daily / every-other-day / specific-weekdays frequency; today's achievement, 7-day chart, history |
| 🚶 Sedentary | Tap once each time you get up and move — daily count + 7-day stats; one-tap +1 button in the floating window |
| 💊 Medication | Medication records (name / type / dosage / frequency / time slots / duration), optional system notifications at scheduled times |
| 📊 Compare | Overlay multiple health parameters (water / sleep / weight / uric acid / mood) on one trend chart to spot correlations |
| 🤖 AI Analysis | Send selected parameters to an AI provider for correlation analysis (DeepSeek and any OpenAI-compatible service — see Settings → AI Analysis) |

### Importing health data (Xiaomi Mi Fitness)

Xiaomi offers no public real-time API; the only official data channel is the **privacy-center export**:

1. Sign in at [account.xiaomi.com](https://account.xiaomi.com) → Privacy Center → Manage your data → Mi Fitness → Request export
2. Download the zip archive
3. In "Settings → Cloud Sync → Import Xiaomi health data", pick the zip — sleep and weight are parsed automatically

---

## Core Features in Detail

### 📋 Task Management

- **Multiple task types**: one-time, recurring (daily / weekdays / weekends / weekly / monthly / interval / custom), quantitative
- **Custom recurrence**: custom tasks support "N times per week" and "M times per day" targets
- **Date picker**: horizontal date bar at the top, swipe left / right to switch days
- **Tag filters**: click a tag button to filter task categories (selected state highlighted)
- **Progress tracking**: quantitative tasks compute completion percentage automatically; recurring tasks show today's completion count
- **Subtasks**: create subtasks under a main task, displayed hierarchically

### ⏱️ Time Tracking

- **Tag timers**: click a tag to start timing, click again to stop; automatic "idle" records fill the gaps
- **Pomodoro**: configurable work / short break / long break durations with automatic phase switching
- **Floating timer window**: always-on-top floating window showing the current timer — collapsible / draggable / edge-snapping, expandable to show today's tasks
- **Gantt chart**: daily time allocation grouped by tag; click for details (time range, duration, share, record list)
- **Pie chart**: weekly / monthly time distribution per tag; click a slice for that tag's detailed records
- **Time statistics**: day / week / month / year dimensions showing accumulated time per tag
- **Right-click management**: right-click a tag to edit or delete it

### 🎯 Goal Management

- **Goal categories**: short-term, long-term and idea goals (three time frames)
- **Tag system**: custom tag names and colors for classification
- **Parent-child hierarchy**: nested goals; completing a sub-goal updates the parent's progress automatically
- **Quantitative goals**: numeric goal tracking with automatic progress percentage

### 📅 Calendar View

- **Monthly calendar**: intuitive view of each day's tasks
- **Color coding**: different goals / tasks get different colors
- **Task statistics**: completion rate, days left, streak (the former "Dashboard" was merged here)
- **Detail sidebar**: click a date to see that day's task details

### 📝 Review

- **Dimensions**: weekly / monthly / yearly statistics
- **Completion rate**: overall task completion percentage
- **Trend charts**: daily completion-count line chart
- **Goal progress**: progress list per goal

### ⚙️ Settings

- **Theme mode**: light / dark / follow system (instant global switching)
- **Window border color**: 5 presets + system color picker
- **Auto-start**: launch with Windows
- **System tray**: minimize to tray, tray balloon notifications
- **Sound effects**: task-completed / focus-ended notification sounds
- **Backup**: selective backup (choose data types, incl. health / medication / containers / exercise) or full JSON backup
- **Restore**: restore data from a backup file
- **Cloud sync**: GitHub / Gitee / WebDAV with anti-overwrite protection — same data as the Android version
- **AI analysis**: multi-provider management (DeepSeek / Tongyi / Zhipu / any OpenAI-compatible service); API keys encrypted with DPAPI

---

## Tech Stack

| Category | Technology |
|----------|------------|
| Framework | .NET 8 WPF |
| UI library | ModernWpf 0.9.6 (Fluent Design controls) |
| Architecture | MVVM (ViewModel + code-behind hybrid) |
| Storage | Local JSON files (no database) |
| Theming | DynamicResource runtime theme switching (light / dark) |
| Window | Custom WindowChrome (borderless rounded window) |
| Tray | Windows Forms NotifyIcon |
| Color picker | Windows Forms ColorDialog |
| AI | OpenAI-compatible Chat Completions API (DeepSeek etc.) |

### Dependency

```xml
<PackageReference Include="ModernWpfUI" Version="0.9.6" />
```

ModernWpfUI is the only NuGet package; everything else ships with .NET 8.

---

## Data Storage

Data lives under `%LOCALAPPDATA%/ME/JsonData/` (plain JSON — easy to copy and back up, and directly readable by the Android app):

| File | Contents |
|------|----------|
| `goals.json` | Goals |
| `tasks.json` | Tasks |
| `tags.json` | Goal tags |
| `time_tags.json` | Time tags |
| `time_records.json` | Time records |
| `focus_sessions.json` | Focus (Pomodoro) sessions |
| `task_completions.json` | Task check-in records |
| `health_records.json` | Health records (sleep / weight / water / mood / uric acid / exercise / sedentary) |
| `medications.json` | Medication records |
| `water_containers.json` | Water containers |
| `exercise_items.json` | Exercise items |
| `ai_providers.json` | AI provider config (keys encrypted with DPAPI) |
| `settings.json` | App settings |

---

## Install & Run

### Requirements

- Windows 10/11
- .NET 8.0 Runtime

### Download

Grab the latest prebuilt zip from [Releases](https://github.com/nailao946/ME/releases/latest), unzip and run `ME.exe`.

### Build from source

```bash
# clone
git clone https://github.com/nailao946/ME.git
cd ME

# build
dotnet build "ME\ME.csproj"

# run
dotnet run --project "ME\ME.csproj"
```

### Release build

```bash
dotnet publish "ME\ME.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## Design Style

- **iOS/macOS style**: rounded cards (12px), soft shadows, clean layout
- **Dark mode**: full dark theme, all UI elements adapted
- **DynamicResource**: global runtime theme switching, effective instantly
- **Custom window**: borderless rounded window with custom title bar (minimize / maximize / close)
- **Accent color**: blue (#007AFF), window border color customizable
- **Chinese UI**: all labels and messages are in Chinese

---

## Contact

- 🔗 GitHub: [github.com/nailao946/ME](https://github.com/nailao946/ME)
- 📱 WeChat: `shuaim888888` (tap the WeChat number in Settings to copy)

Feedback, issues and PRs are welcome — in either language!

---

## License

MIT

---

## Recent Updates

### v2.3.19

- Quantitative tasks and goals at 100% now remain completed on every date.

### v2.3.23

- **Quick capture from the tray**: the tray menu lists every custom module under "快速记一笔" and opens that module's record dialog directly
- **Global hotkey**: optional Ctrl+Alt+M shows/hides the main window from anywhere (Settings → General)
- **Number field constraints**: module fields can define min / max / step (e.g. only multiples of 0.5); the record dialog blocks out-of-range input on both platforms
- **Sync health log**: the last 30 upload/download results with duration are kept and shown in Settings (desktop) and the sync page (Android)

### v2.3.22

- **Module knowledge library**: every custom module can now hold HTML "library pages" with companion CSV — generate one with AI from the module's records, import a local HTML file, open it in the browser or export it; pages live in `html_library.json` and sync to Android automatically
- Conflict resolution gains a third option "keep both" (the cloud copy is saved as `*.from-cloud.json` and keeps syncing)
- New "auto upload on exit" option (best-effort, capped at 12s) and a sync decision-log export for troubleshooting

### v2.3.21

- **Theme system split into style × tone**: "Normal" and "Glass" are now two independent styles, each with light / dark / follow-system; the glass style adds a window-opacity slider, a choice of gradient / background image / fully transparent backdrop and eight gradient palettes
- **Tray menu rebuilt**: brand header, theme submenu with the six style × tone combinations, quick entries (tasks / calendar / modules / settings), a sound toggle and a one-tap cloud sync
- **Custom module dashboard gains two widget types**: a per-day bar chart and an inline recent-records card (six widget types total); the record dialog now sizes itself to the number of fields
- Removed the redundant "custom modules" entry from settings (the module page is the single entry point)

### v2.3.20

- **Task completion history**: one-off tasks and quantitative tasks that reach 100% now appear under "Done today" only on the day they were completed, then move to a new "Completed earlier" group showing the actual completion date; the "Today's goals" panel follows the same rule and no longer lists historically completed items
- **Calendar view**: removed the check-in rate — the first stat card now reads "Today's tasks: Done / Not done"; the check-in streak counts a day as checked in as soon as any single task is completed that day (days without tasks never break the streak). Task statistics only count tasks that exist on that date, and quantitative tasks only when a daily target is set
- **Cloud sync hardened**: upload pushes to every configured provider (GitHub / Gitee / WebDAV) and reports each one separately; "latest version" is now decided by content hashes and per-provider baselines instead of file timestamps, so downloading never overwrites local data that hasn't been uploaded yet (append-only files always merge, other files are kept with an explicit notice); Gitee fixed for missing response sha, wrong default branch and swallowed auth errors
- **Cloud sync diagnostics and conflict resolution**: new "Diagnose connection" runs connect → list → read on every cloud and reports each step with timings; files changed on both sides are now listed under "Resolve conflicts" where each one can be kept from the local device or overwritten from the cloud
- **Glass theme crash fixed**: theme brushes stay solid, the gradient moves to a dedicated window background key, and all brush casts go through a safe accessor
- **Custom modules editor enriched**: icon picker with a visible selected state, quick-start templates, a colour swatch palette, field reordering and a live card preview; quantitative task cards now show "still needs +N today" for the daily target

### v2.3.19

- **Glass theme**: new "Glass" (frosted-glass) appearance option — gradient backdrop with translucent frosted cards
- **Custom modules redesigned**: module cards now follow a Feishu-style layout — large-radius cards, tinted icon block, title with meta line and a record-count badge, a latest-record summary, a hairline divider and an icon+text action row

### v2.3.18

- Added bilingual UI preferences and synchronized goal completion history with today/past-completed sections.

### v2.3.15

- **Fixed WebDAV (Jianguoyun) uploads failing with HTTP 409**: Jianguoyun and other WebDAV services never create parent folders implicitly — uploading into a missing folder always returns 409 (AncestorsNotFound). The old code mistook the folder-creation request's 409 for "folder already exists, go ahead", so nothing was created and every file failed. The app now creates the sync folder level by level before uploading, and a 409 during upload triggers an automatic folder re-creation plus one retry; the server address defaults to Jianguoyun (https://dav.jianguoyun.com/dav/) and is pre-filled when switching to WebDAV

### v2.3.14

- **Fixed Gitee upload failing with "sha is missing" (0/15 files)**: Gitee's API differs from GitHub's — creating a file requires POST, while PUT is strictly an update endpoint that must carry the file's sha (requests without one are rejected with HTTP 400 even when the file doesn't exist yet). New files previously went through a sha-less PUT, so every first-time upload failed. File creation now uses POST, with an automatic fallback to a sha-carrying update when the file turns out to exist. GitHub / WebDAV behavior unchanged; the Gitee repo stays interoperable with the Android version

### v2.3.13

- **Review statistics reworked**: "Completed tasks" now shows **completed / total due**. Total due counts only tasks actually due that day — a recurring task scheduled for Sat/Sun doesn't count on Monday, subtasks don't count, quantitative tasks without a daily target don't count. Completion rate = completed ÷ total due. Quantitative tasks with a daily target (e.g. 1) count as completed once that day's check-in reaches the daily target; a finished quantitative task counts only up to the day it reached its target
- **"vs previous period" on Review cards**: time invested and completion rate now show week-over-week / month-over-month / day-over-day changes — together with the existing completed-tasks trend, all three cards show green-up / red-down deltas at a glance
- **"All" time chart fixed**: the time-statistics period "All" now draws a 12-month monthly comparison line instead of squeezing 365 days into an unreadable chart; daily line charts ("This month" etc.) thin out date labels automatically so they never overlap
- **Task list "Today" panel**: time invested and completed x/y now compare against the previous day (green up / red down)
- **Daily records for quantitative tasks**: once a no-schedule quantitative task has a daily target, a check-in whose daily increment reaches that target records a per-day completion — both the check-in heatmap and review statistics can now show quantitative completions by day
- Android companion (v2.4.38): same statistics on the Review screen, "All" time stats as a 12-month monthly bar chart, fixed the check-in heatmap never lighting up for recurring tasks

📖 Full changelog (Chinese): [README_CN.md](README_CN.md) · 📱 Android version: [ME-PE](https://github.com/nailao946/ME-PE)
