# FusionNet Pro — Team Spec & Contracts (v3.0.0)

This is the single source of truth for everyone building FusionNet Pro. Read it fully before writing code.

## 1. What we are building

One unified Windows application that replaces three legacy prototypes living in the same workspace:

| Legacy | Tech | What we keep |
|---|---|---|
| `gemni\NetPrioritizer` | WPF net9, dark esports UI | ping/speed monitors, auto game detection, service pausing, registry latency tweaks, tray, per-game UI |
| `glm\GameFocus` | WinForms net10 | journaled 100% reversible focus mode, per-app Suspend/Throttle, fullscreen boost, power plan, Game Mode/DVR, standby RAM purge, hotkey, 2s re-apply, crash recovery |
| `gpt\GamingNetworkGuard` | WPF + Core + xunit | Windows Firewall per-app blocking, game profiles, managed app catalog, settings store, coordinator state machine, installer |

Product name: **FusionNet Pro**. Exe: `FusionNetPro.exe`. Version 3.0.0. UI language: **Arabic by default, English toggle** (RTL/LTR), all strings localized.

## 2. Repository layout and ownership

Root: the repository root (the folder that contains `FusionNet.slnx`).

```
FusionNet.slnx
Directory.Build.props
src/FusionNet.Core/            # models, catalogs, planners, interfaces, utils  (LEAD OWNS — frozen)
src/FusionNet.Windows/         # all OS integration                     (T1 + T2)
   Native/Win32Native.cs                     T1
   System/*.cs                               T1
   Orchestration/FusionCoordinator.cs        T1
   Network/*.cs                              T2
   WindowsServices.cs                        LEAD (do not touch)
src/FusionNet.App/             # WPF UI                                  (LEAD + T3 + T4 + T5)
tests/FusionNet.Tests/         # xunit                                   (T6)
docs/, build/, installer/      # docs + packaging                        (T6)
```

**Never edit files outside your scope.** If a contract change is unavoidable, message the Lead instead of editing Core. Build with the solution at the repo root; never edit `bin/`, `obj/`, or `publish/`.

## 3. Build & test commands

```powershell
cd FusionNetPro   # repository root
dotnet build FusionNet.slnx -v q --nologo            # whole solution
dotnet build src\FusionNet.Windows -v q --nologo     # your project only
dotnet test tests\FusionNet.Tests -v q --nologo
```

.NET 10.0.402 SDK, Windows 10/11 x64. **No new NuGet packages** (only `System.ServiceProcess.ServiceController` 10.0.13 is pre-approved in FusionNet.Windows).

## 4. Frozen contracts (read the code, do not change it)

- `FusionNet.Core.Models`: `FusionSettings`, `GameProfile`/`GameCatalog`, `ManagedApp`/`ManagedAppCatalog`, `CustomManagedApp`, `CustomGame`, `TweakDefinition`/`TweakCatalog`, `ServiceDefinition`/`ServiceCatalog`, `DnsProvider`/`DnsCatalog`, `ProcessSnapshot`, `TargetStatistics`, `AdapterInfo`, `JournalEntry`/`JournalDocument`, `OperationResult`, `FusionSnapshot`, `ActivityEntry`, enums.
- `FusionNet.Core.Planning`: `OptimizationPlanner.Build(...)`, `OptimizationPlan`, `FirewallPlanner`, `PlannedFirewallRule`, `AppRuleTarget`.
- `FusionNet.Core.Abstractions`: every service interface + `IAppServices`.
- `FusionNet.Core.Utilities`: `AppPaths`, `FusionJson`, `Humanize`, `PingStatisticsAccumulator`, `HotkeyGesture`, `KnownPaths`.

The Lead implements `FusionNet.Windows.WindowsServices : IAppServices` (composition root). Concrete class names and constructors below are binding, because the Lead wires them:

```csharp
// namespace FusionNet.Windows.System
SettingsStore();  ActivityLog();  ElevationService();  SystemInfoService();
WindowsServiceController();  AutoStartService();  StandbyMemoryService();  JournalService();
ProcessController(IActivityLog activity);
FirewallService(IActivityLog activity);
RegistryTweakService(IActivityLog activity);
PowerPlanService(IActivityLog activity);
GameDetectionService(ISettingsStore settingsStore);

// namespace FusionNet.Windows.Orchestration
FusionCoordinator(
    Func<FusionSettings> settingsAccessor, ISettingsStore settingsStore, IJournal journal,
    IActivityLog activity, IGameDetectionService games, IFirewallService firewall,
    IWindowsServiceController windowsServices, IProcessController processes,
    IRegistryTweakService tweaks, IPowerPlanService power, IStandbyMemoryService memory,
    ISystemInfoService systemInfo, IElevationService elevation);

// namespace FusionNet.Windows.Network
PingMonitor(IActivityLog activity);  ThroughputMonitor();  AdapterInfoService();
DnsService(IActivityLog activity);  PublicIpService();
DiagnosticsService(ISystemInfoService systemInfo, IAdapterInfoService adapters,
    IRegistryTweakService tweaks, IWindowsServiceController windowsServices,
    IPingMonitor ping, IElevationService elevation, IActivityLog activity);
```

### Journal conventions (must match exactly — crash recovery depends on it)

`IJournal.Record(kind, target, description, previousValue, appliedValue)` before/while applying:

| Kind | target | previousValue | appliedValue |
|---|---|---|---|
| `Service` | service name (`wuauserv`) | `running`/`stopped`/`absent` | `stopped` |
| `Registry` | `tweak:<tweakId>` | serialized previous (e.g. `dword:0`) or `absent` | `dword:4294967295` |
| `PowerPlan` | `powerplan` | active plan GUID | new GUID |
| `Process` | `<pid>|<processName>` | `priority:Normal` or `suspended` | `suspended` / `priority:High` |
| `Firewall` | rule name | `absent` | rule display name |
| `Dns` | adapter id | `dhcp` or `1.1.1.1,1.0.0.1` | applied server list / `dhcp` |
| `Memory` | `standby` | `-` | freed bytes |

Every journal mutation calls `Save()` immediately (atomic write). `Begin()` persists `Active=true`; `Complete()` sets `Active=false` and saves. On startup `HasPendingRestore` tells the coordinator a previous session died and `RestoreInterruptedSessionAsync()` must undo everything, then `Complete()`.

## 5. Behaviour rules

- **Never require admin for startup.** App runs `asInvoker`. Any action needing admin returns `OperationResult.NotElevated("...")`; the UI offers "Restart as administrator". `IElevationService.TryRestartElevated()` relaunches the exe with `runas`.
- **Nothing irreversible.** Every service stop, registry write, firewall rule, DNS change, power-plan switch and process suspension is journaled and restorable.
- **Never touch system-critical processes** (`System`, `svchost`, `lsass`, `csrss`, `explorer`, `dwm`, Defender, ...) and never block the running game or Discord (see `FirewallPlanner.IsProtected`).
- **All OS calls wrapped in try/catch**, returning `OperationResult.Fail(...)`; log via `IActivityLog` with category names (`Fusion`, `Firewall`, `Tweaks`, `DNS`, `Monitor`, `Games`, `Services`, `Apps`, `Settings`).
- **Threading:** services raise events from background threads. UI view models marshal to the WPF dispatcher themselves. Never block the UI thread; no `Thread.Sleep` in UI code.
- **Idempotent apply:** re-applying a rule/service/tweak that already matches must be a no-op (check state first).
- **Performance:** game detection and reconciler tick every 2 s with cheap snapshots (no full WMI scans). Ping monitor default 1 s. UI stays at 60 fps.
- **Arabic:** natural, professional Modern Standard Arabic — not literal translation. No English leakage in user-facing strings except technical identifiers (DNS, TCP, FPS, ms, Windows). Arabic-Indic digits are optional; Latin digits are fine and preferred for metrics.

## 6. UI guide

See `docs/UI_GUIDE.md` for theme resource keys, control catalog, page/view-model contract, localization registration and navigation. The shell, theme and localization engine are Lead-owned; pages are independent `UserControl` files in `src/FusionNet.App/Views/`.

## 7. Definition of done (per teammate)

1. `dotnet build` of your project/solution is green with zero errors and no new warnings you introduced.
2. Your code path can run **without admin** without throwing (it may report NotElevated).
3. No TODO placeholders, no `NotImplementedException`, no empty `catch {}` without a comment.
4. You report: files created/changed, behaviours implemented, how you verified, and anything the Lead must wire.
