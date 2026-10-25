using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Models;
using FusionNet.Core.Utilities;

namespace FusionNet.Windows.Network;

/// <summary>
/// Full system health check used by the Diagnostics page. Every individual check is isolated:
/// a failing check produces an Info/Warning finding with a human explanation, while the report
/// itself is always returned. All findings are bilingual so the Arabic and English UI can render
/// them without a translation pass.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private const string Category = "Monitor";
    private const string AppVersionFallback = "3.3.3";
    private const int ProbesPerTarget = 4;
    private const string DnsTestDomain = "www.microsoft.com";

    private static readonly string[] InternetTargets = ["1.1.1.1", "8.8.8.8"];

    private readonly ISystemInfoService _systemInfo;
    private readonly IAdapterInfoService _adapters;
    private readonly IRegistryTweakService _tweaks;
    private readonly IWindowsServiceController _windowsServices;
    private readonly IPingMonitor _ping;
    private readonly IElevationService _elevation;
    private readonly IActivityLog _activity;

    public DiagnosticsService(
        ISystemInfoService systemInfo,
        IAdapterInfoService adapters,
        IRegistryTweakService tweaks,
        IWindowsServiceController windowsServices,
        IPingMonitor ping,
        IElevationService elevation,
        IActivityLog activity)
    {
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _windowsServices = windowsServices ?? throw new ArgumentNullException(nameof(windowsServices));
        _ping = ping ?? throw new ArgumentNullException(nameof(ping));
        _elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public async Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var findings = new List<DiagnosticFinding>();

        IReadOnlyList<AdapterInfo> adapterList = [];
        var pingStatistics = new Dictionary<string, TargetStatistics>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ServiceStateInfo> serviceList = [];
        IReadOnlyList<TweakState> tweakList = [];

        var machineName = Safe(() => _systemInfo.MachineName, Environment.MachineName);
        var windowsVersion = Safe(() => _systemInfo.WindowsVersion, Environment.OSVersion.VersionString);
        var appVersion = Safe(() => _systemInfo.AppVersion, AppVersionFallback);
        var elevated = Safe(() => _elevation.IsElevated, NetworkInterop.IsElevated);
        var isWindows11 = Safe(() => _systemInfo.IsWindows11, false);

        LogInfo("Diagnostics started.");

        try
        {
            // Environment checks are cheap and always available.
            findings.AddRange(CheckElevation(elevated));
            findings.AddRange(CheckWindowsBuild(windowsVersion, isWindows11));
            findings.AddRange(CheckMemoryAndDisk());

            // Adapter enumeration also feeds the MTU and latency checks.
            try
            {
                adapterList = await Task.Run(() => _adapters.GetAdapters(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                findings.Add(Info("Network", "Network adapters could not be enumerated",
                    "تعذر قراءة محولات الشبكة",
                    $"The adapter list could not be read: {ex.Message}", "Adapter enumeration failed."));
                LogWarning("Adapter enumeration failed.", ex.Message);
            }

            findings.AddRange(EvaluateAdapters(adapterList));

            // Independent checks run in parallel so a full report takes seconds, not minutes.
            Task<List<DiagnosticFinding>> firewallTask = CheckFirewallAsync(cancellationToken);
            Task<List<DiagnosticFinding>> dnsTask = CheckDnsResolutionAsync(cancellationToken);
            Task<List<DiagnosticFinding>> mtuTask = CheckMtuAsync(adapterList, cancellationToken);
            Task<(List<DiagnosticFinding> Findings, IReadOnlyList<ServiceStateInfo> Services)> servicesTask =
                CheckServicesAsync(cancellationToken);
            Task<(List<DiagnosticFinding> Findings, IReadOnlyList<TweakState> Tweaks)> tweaksTask =
                CheckTweaksAsync(cancellationToken);
            Task<(List<DiagnosticFinding> Findings, Dictionary<string, TargetStatistics> Statistics)> latencyTask =
                CheckLatencyAndGatewayAsync(adapterList, cancellationToken);
            Task<List<DiagnosticFinding>> vpnTask = CheckVpnAsync(cancellationToken);

            try
            {
                await Task.WhenAll(new Task[] { firewallTask, dnsTask, mtuTask, servicesTask, tweaksTask, latencyTask, vpnTask })
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            findings.AddRange(await firewallTask.ConfigureAwait(false));
            findings.AddRange(await dnsTask.ConfigureAwait(false));
            findings.AddRange(await mtuTask.ConfigureAwait(false));
            findings.AddRange(await vpnTask.ConfigureAwait(false));

            var servicesResult = await servicesTask.ConfigureAwait(false);
            serviceList = servicesResult.Services;
            findings.AddRange(servicesResult.Findings);

            var tweaksResult = await tweaksTask.ConfigureAwait(false);
            tweakList = tweaksResult.Tweaks;
            findings.AddRange(tweaksResult.Findings);

            var latencyResult = await latencyTask.ConfigureAwait(false);
            findings.AddRange(latencyResult.Findings);
            foreach (var pair in latencyResult.Statistics) pingStatistics[pair.Key] = pair.Value;

            // Positive confirmation when nothing is wrong — users need to see "clean" explicitly.
            if (!findings.Any(finding => finding.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Critical))
            {
                findings.Insert(0, new DiagnosticFinding(
                    "General",
                    "All checks passed",
                    "جميع الفحوصات سليمة",
                    "No network, security or performance problems were detected on this machine.",
                    DiagnosticSeverity.Ok,
                    "Nothing to fix — your connection is in good shape.",
                    "لا يوجد ما يحتاج إلى إصلاح — اتصالك بحالة جيدة."));
            }
        }
        catch (OperationCanceledException)
        {
            findings.Add(Info("General", "Diagnostics cancelled",
                "أُلغي التشخيص",
                "The health check was cancelled before it finished; the report contains partial results.",
                "The check was cancelled."));
        }
        catch (Exception ex)
        {
            findings.Add(new DiagnosticFinding(
                "General",
                "Diagnostics engine failure",
                "فشل محرك التشخيص",
                $"The health check stopped unexpectedly: {ex.Message}",
                DiagnosticSeverity.Critical,
                "Try running the diagnostics again; if it keeps failing, restart FusionNet Pro.",
                "أعد تشغيل التشخيص؛ وإذا استمر الفشل فأعد تشغيل FusionNet Pro."));
            LogError("Diagnostics failed.", ex.Message);
        }

        var orderedFindings = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.TitleEn, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var report = new DiagnosticReport(
            generatedAt,
            machineName,
            windowsVersion,
            appVersion,
            elevated,
            orderedFindings,
            adapterList,
            pingStatistics,
            serviceList,
            tweakList);

        LogInfo($"Diagnostics finished: {report.CriticalCount} critical, {report.WarningCount} warning(s), overall {report.OverallSeverity}.");
        return report;
    }

    // ------------------------------------------------------------------ environment checks

    private static IEnumerable<DiagnosticFinding> CheckElevation(bool elevated)
    {
        if (elevated)
        {
            yield return new DiagnosticFinding(
                "Security",
                "Administrator rights are active",
                "صلاحيات المسؤول مفعّلة",
                "FusionNet Pro can apply DNS changes, firewall rules and service changes.",
                DiagnosticSeverity.Ok);
        }
        else
        {
            yield return new DiagnosticFinding(
                "Security",
                "Running without administrator rights",
                "التشغيل بدون صلاحيات المسؤول",
                "Diagnostics, ping monitoring and DNS benchmarking work normally. Applying fixes — DNS changes, firewall rules, service pausing — requires administrator rights.",
                DiagnosticSeverity.Info,
                "Restart FusionNet Pro as administrator before applying network fixes.",
                "أعد تشغيل FusionNet Pro كمسؤول قبل تطبيق إصلاحات الشبكة.");
        }
    }

    private static IEnumerable<DiagnosticFinding> CheckWindowsBuild(string windowsVersion, bool isWindows11)
    {
        var platform = isWindows11 ? "Windows 11" : "Windows 10 or earlier";
        var buildMatch = Regex.Match(windowsVersion, @"10\.0\.(\d{3,6})");
        var build = buildMatch.Success && int.TryParse(buildMatch.Groups[1].Value, out var parsed) ? parsed : 0;

        if (!isWindows11 && build is > 0 and < 19045)
        {
            yield return new DiagnosticFinding(
                "System",
                "Windows build is out of support",
                "إصدار ويندوز غير مدعوم",
                $"{windowsVersion} ({platform}) no longer receives servicing updates. An outdated network stack can miss latency and security fixes.",
                DiagnosticSeverity.Warning,
                "Install Windows Update and move to Windows 11 when your hardware supports it.",
                "ثبّت تحديثات ويندوز وانتقل إلى Windows 11 إذا كانت العتاد يدعم ذلك.");
            yield break;
        }

        yield return new DiagnosticFinding(
            "System",
            "Windows build",
            "إصدار ويندوز",
            $"{windowsVersion} ({platform})",
            DiagnosticSeverity.Info);
    }

    private IEnumerable<DiagnosticFinding> CheckMemoryAndDisk()
    {
        var totalMemory = Safe(() => _systemInfo.TotalPhysicalMemoryBytes, 0L);
        var availableMemory = Safe(() => _systemInfo.AvailablePhysicalMemoryBytes, 0L);
        var freeDisk = Safe(() => _systemInfo.TotalSystemDriveFreeBytes, 0L);

        if (totalMemory > 0)
        {
            var ratio = availableMemory / (double)totalMemory;
            if (availableMemory < 1_500_000_000 || ratio < 0.10)
            {
                yield return new DiagnosticFinding(
                    "Performance",
                    "Low available memory",
                    "ذاكرة متاحة منخفضة",
                    $"Only {Humanize.Bytes(availableMemory)} of {Humanize.Bytes(totalMemory)} RAM is free ({Humanize.Percent(ratio * 100)}). Low memory causes paging, stutter and latency spikes in games.",
                    DiagnosticSeverity.Warning,
                    "Close unused apps or browser tabs before playing, and consider adding RAM.",
                    "أغلق التطبيقات وعلامات التبويب غير المستخدمة قبل اللعب، وفكّر في زيادة الذاكرة.");
            }
            else
            {
                yield return new DiagnosticFinding(
                    "Performance",
                    "Memory headroom is healthy",
                    "الذاكرة المتاحة كافية",
                    $"{Humanize.Bytes(availableMemory)} of {Humanize.Bytes(totalMemory)} RAM is free.",
                    DiagnosticSeverity.Ok);
            }
        }
        else
        {
            yield return Info("Performance", "Memory information is unavailable",
                "معلومات الذاكرة غير متاحة", "Physical memory counters could not be read.", "Memory counters unavailable.");
        }

        if (freeDisk > 0)
        {
            if (freeDisk < 1_000_000_000)
            {
                yield return new DiagnosticFinding(
                    "System",
                    "System drive is almost full",
                    "قرص النظام شبه ممتلئ",
                    $"Only {Humanize.Bytes(freeDisk)} remains on the Windows drive. A full drive slows page-file and update operations and can stall games.",
                    DiagnosticSeverity.Critical,
                    "Free at least 10 GB on the Windows drive (Disk Cleanup, remove old downloads).",
                    "أفرغ 10 غيغابايت على الأقل من قرص ويندوز (تنظيف القرص، حذف التنزيلات القديمة).");
            }
            else if (freeDisk < 5_000_000_000)
            {
                yield return new DiagnosticFinding(
                    "System",
                    "Low disk space on the system drive",
                    "مساحة قرص النظام منخفضة",
                    $"Only {Humanize.Bytes(freeDisk)} remains on the Windows drive.",
                    DiagnosticSeverity.Warning,
                    "Free up space so Windows updates and the page file can work normally.",
                    "أفرغ مساحة حتى تعمل تحديثات ويندوز وملف الترحيل بشكل طبيعي.");
            }
            else
            {
                yield return new DiagnosticFinding(
                    "System",
                    "System drive space is healthy",
                    "مساحة قرص النظام جيدة",
                    $"{Humanize.Bytes(freeDisk)} is free on the Windows drive.",
                    DiagnosticSeverity.Ok);
            }
        }
        else
        {
            yield return Info("System", "Disk space is unavailable",
                "مساحة القرص غير متاحة", "The system drive free-space counter could not be read.", "Disk counters unavailable.");
        }
    }

    // ------------------------------------------------------------------ adapter checks

    private static IEnumerable<DiagnosticFinding> EvaluateAdapters(IReadOnlyList<AdapterInfo> adapters)
    {
        if (adapters.Count == 0)
        {
            yield return new DiagnosticFinding(
                "Network",
                "No network adapters found",
                "لم يتم العثور على محولات شبكة",
                "Windows did not report any physical network adapter. Without one there is no network connectivity.",
                DiagnosticSeverity.Critical,
                "Check Device Manager for disabled adapters, update the network driver, and make sure Wi-Fi is switched on.",
                "افحص إدارة الأجهزة بحثاً عن محولات معطّلة، وحدّث تعريف الشبكة، وتأكد من تشغيل واي فاي.");
            yield break;
        }

        var defaultAdapter = adapters.FirstOrDefault(adapter => adapter.IsDefaultRoute && IsUp(adapter))
                             ?? adapters.FirstOrDefault(IsUp);

        if (defaultAdapter is null)
        {
            yield return new DiagnosticFinding(
                "Network",
                "No active network connection",
                "لا يوجد اتصال شبكة نشط",
                "All adapters are down or disconnected, so no internet traffic can flow.",
                DiagnosticSeverity.Critical,
                "Connect the Ethernet cable or join a Wi-Fi network, then run diagnostics again.",
                "وصّل سلك الشبكة أو اتصل بشبكة واي فاي ثم أعد تشغيل التشخيص.");
        }
        else
        {
            var addresses = defaultAdapter.IPv4Addresses.Count > 0
                ? string.Join(", ", defaultAdapter.IPv4Addresses)
                : "no IPv4 address";
            yield return new DiagnosticFinding(
                "Network",
                "Default connection is active",
                "الاتصال الافتراضي نشط",
                $"{defaultAdapter.Name} ({defaultAdapter.Description}) is up at {Humanize.Speed(defaultAdapter.LinkSpeedBps)}, IPv4: {addresses}.",
                DiagnosticSeverity.Ok);
        }

        var slowAdapters = adapters
            .Where(adapter => IsUp(adapter) && adapter.LinkSpeedBps > 0 && adapter.LinkSpeedBps < 100_000_000)
            .ToArray();

        if (slowAdapters.Length > 0)
        {
            yield return new DiagnosticFinding(
                "Network",
                "Slow network link speed",
                "سرعة وصلة الشبكة ضعيفة",
                "These adapters negotiate less than 100 Mbps: " +
                string.Join(", ", slowAdapters.Select(adapter => $"{adapter.Name} ({Humanize.Speed(adapter.LinkSpeedBps)})")) +
                ". A slow link cannot carry a stable game stream even with low ping.",
                DiagnosticSeverity.Warning,
                "Use a Gigabit Ethernet cable/port or the 5 GHz Wi-Fi band, and check the router's link-speed LEDs.",
                "استخدم كبل ومنفذ إيثرنت بسرعة غيغابت أو نطاق واي فاي ٥ غيغاهرتز، وافحص مؤشرات السرعة في الراوتر.");
        }

        foreach (var wireless in adapters.Where(adapter => adapter.IsWireless))
        {
            if (!IsUp(wireless))
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi",
                    "Wi-Fi adapter is not connected",
                    "محول واي فاي غير متصل",
                    $"{wireless.Name} is {wireless.Status.ToLowerInvariant()}. This is fine when Ethernet is your active connection.",
                    DiagnosticSeverity.Info);
                continue;
            }

            if (wireless.SignalPercent is not { } signal)
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi",
                    "Wi-Fi signal strength is unavailable",
                    "قوة إشارة واي فاي غير متاحة",
                    $"{wireless.Name} is connected but Windows did not report a signal percentage (WLAN service may be stopped).",
                    DiagnosticSeverity.Info);
                continue;
            }

            var network = string.IsNullOrWhiteSpace(wireless.Ssid) ? "the current network" : wireless.Ssid;
            var detail = $"{network}: {signal}% signal strength, link speed {Humanize.Speed(wireless.LinkSpeedBps)}.";

            if (signal < 20)
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi", "Very weak Wi-Fi signal", "إشارة واي فاي ضعيفة جداً", detail,
                    DiagnosticSeverity.Critical,
                    "Move closer to the router, switch to the 5 GHz band, or use Ethernet for competitive games.",
                    "اقترب من الراوتر أو انتقل إلى نطاق ٥ غيغاهرتز أو استخدم سلك الشبكة في الألعاب التنافسية.");
            }
            else if (signal < 40)
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi", "Weak Wi-Fi signal", "إشارة واي فاي ضعيفة", detail,
                    DiagnosticSeverity.Warning,
                    "Move closer to the router or switch to the 5 GHz band to reduce retransmissions and latency spikes.",
                    "اقترب من الراوتر أو انتقل إلى نطاق ٥ غيغاهرتز لتقليل إعادة الإرسال وتقلب زمن الاستجابة.");
            }
            else if (signal < 60)
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi", "Moderate Wi-Fi signal", "إشارة واي فاي متوسطة", detail,
                    DiagnosticSeverity.Info,
                    "A wired connection is still more stable than Wi-Fi for competitive play.",
                    "يظل الاتصال السلكي أكثر ثباتاً من واي فاي في اللعب التنافسي.");
            }
            else
            {
                yield return new DiagnosticFinding(
                    "Wi-Fi", "Wi-Fi signal is strong", "إشارة واي فاي قوية", detail,
                    DiagnosticSeverity.Ok);
            }
        }

        if (defaultAdapter is not null && defaultAdapter.DnsServers.Count == 0)
        {
            yield return new DiagnosticFinding(
                "DNS",
                "No DNS servers are configured",
                "لا توجد خوادم DNS مُعدّة",
                $"{defaultAdapter.Name} has no IPv4 DNS server. Domain names cannot be resolved until one is set.",
                DiagnosticSeverity.Warning,
                "Set a DNS provider on the DNS page or run 'ipconfig /renew' to obtain one from DHCP.",
                "اختر مزوّد DNS من صفحة DNS أو نفّذ 'ipconfig /renew' للحصول عليه من DHCP.");
        }
    }

    // ------------------------------------------------------------------ firewall

    private async Task<List<DiagnosticFinding>> CheckFirewallAsync(CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await Task.Run(() => NetworkCommandRunner.Run(
                    NetworkCommandRunner.SystemTool("netsh.exe"),
                    ["advfirewall", "show", "allprofiles", "state"],
                    timeoutMs: 5_000),
                cancellationToken).ConfigureAwait(false);

            if (result.TimedOut)
            {
                findings.Add(Info("Firewall", "Firewall state check timed out",
                    "انتهت مهلة فحص جدار الحماية",
                    "Windows Firewall did not report its state in time.", "netsh advfirewall timed out."));
                return findings;
            }

            var states = ParseFirewallProfileStates(result.CombinedOutput);
            if (states.Count == 0)
            {
                findings.Add(Info("Firewall", "Firewall state could not be determined",
                    "تعذر تحديد حالة جدار الحماية",
                    "netsh did not return a recognizable profile state. The firewall state is unknown, not necessarily disabled.",
                    NetworkCommandRunner.Summarize(result.CombinedOutput)));
                return findings;
            }

            var disabled = states.Where(state => !state.Enabled).ToArray();
            if (disabled.Length == 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Firewall",
                    "Windows Firewall is enabled on all profiles",
                    "جدار حماية ويندوز مفعّل على جميع الملفات",
                    $"Enabled profiles: {string.Join(", ", states.Select(state => state.Profile))}.",
                    DiagnosticSeverity.Ok));
            }
            else
            {
                findings.Add(new DiagnosticFinding(
                    "Firewall",
                    "Windows Firewall is disabled",
                    "جدار حماية ويندوز معطّل",
                    $"Disabled for: {string.Join(", ", disabled.Select(state => state.Profile))}. The machine is exposed to untrusted network traffic.",
                    DiagnosticSeverity.Warning,
                    "Enable Windows Firewall for all profiles from Windows Security.",
                    "فعّل جدار حماية ويندوز لجميع الملفات من أمان ويندوز."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Firewall", "Firewall state could not be checked",
                "تعذر فحص جدار الحماية", ex.Message, ex.Message));
            LogWarning("Firewall check failed.", ex.Message);
        }

        return findings;
    }

    private static List<(string Profile, bool Enabled)> ParseFirewallProfileStates(string output)
    {
        var states = new List<(string Profile, bool Enabled)>();
        string? profile = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.EndsWith("Profile Settings:", StringComparison.OrdinalIgnoreCase))
            {
                profile = line[..^"Profile Settings:".Length].Trim();
                if (profile.Length == 0) profile = null;
                continue;
            }

            var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            var last = tokens[^1];
            bool? enabled = last.ToUpperInvariant() switch
            {
                "ON" or "ENABLED" => true,
                "OFF" or "DISABLED" => false,
                _ => null
            };

            if (enabled is null) continue;

            // Require the line to look like a state row ("State ON"), not a sentence.
            var first = tokens[0];
            if (!first.Equals("State", StringComparison.OrdinalIgnoreCase) &&
                !first.Equals("Status", StringComparison.OrdinalIgnoreCase) &&
                tokens.Length > 2)
            {
                continue;
            }

            states.Add((profile ?? $"Profile {states.Count + 1}", enabled.Value));
        }

        return states;
    }

    // ------------------------------------------------------------------ DNS resolution

    private async Task<List<DiagnosticFinding>> CheckDnsResolutionAsync(CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var addresses = await Dns.GetHostAddressesAsync(DnsTestDomain, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var first = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                        ?? addresses.FirstOrDefault();

            if (first is null)
            {
                findings.Add(new DiagnosticFinding(
                    "DNS",
                    "DNS resolution returned no addresses",
                    "لم يُرجع تحليل DNS أي عناوين",
                    $"{DnsTestDomain} was looked up but no address was returned.",
                    DiagnosticSeverity.Critical,
                    "Flush the DNS cache and set a reliable resolver such as 1.1.1.1.",
                    "أفرغ ذاكرة DNS المؤقتة واضبط مُحلّلاً موثوقاً مثل 1.1.1.1."));
            }
            else
            {
                findings.Add(new DiagnosticFinding(
                    "DNS",
                    "DNS resolution works",
                    "تحليل أسماء النطاقات يعمل",
                    $"{DnsTestDomain} resolved to {first} in {stopwatch.ElapsedMilliseconds} ms through the adapter's configured DNS servers.",
                    DiagnosticSeverity.Ok));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            findings.Add(new DiagnosticFinding(
                "DNS",
                "DNS resolution timed out",
                "انتهت مهلة تحليل أسماء النطاقات",
                $"{DnsTestDomain} did not resolve within 5 seconds. Domain lookups are effectively broken.",
                DiagnosticSeverity.Critical,
                "Check the DNS servers on the DNS page, then flush the DNS cache.",
                "افحص خوادم DNS في صفحة DNS ثم أفرغ ذاكرة DNS المؤقتة."));
        }
        catch (Exception ex)
        {
            findings.Add(new DiagnosticFinding(
                "DNS",
                "DNS resolution failed",
                "فشل تحليل أسماء النطاقات",
                $"Resolving {DnsTestDomain} failed: {ex.Message}",
                DiagnosticSeverity.Critical,
                "Set a working DNS provider and flush the resolver cache.",
                "اضبط مزوّد DNS يعمل وأفرغ ذاكرة التحليل المؤقتة."));
        }

        return findings;
    }

    // ------------------------------------------------------------------ MTU

    private async Task<List<DiagnosticFinding>> CheckMtuAsync(IReadOnlyList<AdapterInfo> adapters, CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await Task.Run(() => NetworkCommandRunner.Run(
                    NetworkCommandRunner.SystemTool("netsh.exe"),
                    ["interface", "ipv4", "show", "subinterfaces"],
                    timeoutMs: 5_000),
                cancellationToken).ConfigureAwait(false);

            var mtuByAdapter = ParseMtuTable(result.CombinedOutput);
            if (mtuByAdapter.Count == 0)
            {
                findings.Add(Info("Network", "MTU could not be verified",
                    "تعذر التحقق من وحدة MTU",
                    "Windows did not report MTU values for the network interfaces.",
                    NetworkCommandRunner.Summarize(result.CombinedOutput)));
                return findings;
            }

            var anomalies = new List<string>();
            var jumbo = new List<string>();

            foreach (var adapter in adapters.Where(IsUp))
            {
                var mtu = mtuByAdapter
                    .FirstOrDefault(pair => pair.Key.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase))
                    .Key;
                if (mtu is null) continue;

                var value = mtuByAdapter[mtu];
                if (value < 1400) anomalies.Add($"{adapter.Name} ({value} bytes)");
                else if (value > 1500) jumbo.Add($"{adapter.Name} ({value} bytes)");
            }

            if (anomalies.Count > 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Network",
                    "Unusually low MTU detected",
                    "وحدة MTU منخفضة بشكل غير معتاد",
                    $"Low MTU on: {string.Join(", ", anomalies)}. A low MTU fragments packets and adds latency.",
                    DiagnosticSeverity.Warning,
                    "Check VPN/PPPoE settings; a normal Ethernet or Wi-Fi MTU is 1500 bytes.",
                    "افحص إعدادات VPN أو PPPoE؛ وحدة MTU الطبيعية لإيثرنت وواي فاي هي 1500 بايت."));
            }
            else if (jumbo.Count > 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Network",
                    "Jumbo frames detected",
                    "إطارات جامبو مكتشفة",
                    $"MTU above 1500 on: {string.Join(", ", jumbo)}. Some routers and VPNs drop these packets.",
                    DiagnosticSeverity.Info,
                    "Use MTU 1500 unless every device on the path supports jumbo frames.",
                    "استخدم MTU 1500 ما لم تدعم كل أجهزة المسار إطارات جامبو."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Network", "MTU check failed",
                "فشل فحص MTU", ex.Message, ex.Message));
            LogWarning("MTU check failed.", ex.Message);
        }

        return findings;
    }

    private static Dictionary<string, int> ParseMtuTable(string output)
    {
        var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Modern format: MTU, MediaSenseState, Bytes In, Bytes Out, Interface
        var modernRow = new Regex(@"^\s*(?<mtu>\d{1,10})\s+\d+\s+\d+\s+\d+\s+(?<name>.+?)\s*$",
            RegexOptions.CultureInvariant);

        // Older format: Idx, Met, MTU, State, Name
        var legacyRow = new Regex(@"^\s*\d+\s+\d+\s+(?<mtu>\d{3,5})\s+\S+\s+(?<name>.+?)\s*$",
            RegexOptions.CultureInvariant);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var match = modernRow.Match(line);
            if (!match.Success) match = legacyRow.Match(line);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups["mtu"].Value, out var mtu)) continue;
            if (mtu is < 500 or > 9200) continue;

            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0) continue;
            table[name] = mtu;
        }

        return table;
    }

    // ------------------------------------------------------------------ VPN / tunnel

    private async Task<List<DiagnosticFinding>> CheckVpnAsync(CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            var activeVpn = await Task.Run(() =>
            {
                var names = new List<string>();
                try
                {
                    foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
                        if (!NetworkProbeHelper.LooksLikeVpnOrTunnel(networkInterface)) continue;
                        names.Add($"{networkInterface.Name} ({networkInterface.Description})");
                    }
                }
                catch (Exception)
                {
                    // Network stack rebuilding — report no VPN rather than failing the report.
                }

                return names;
            }, cancellationToken).ConfigureAwait(false);

            if (activeVpn.Count > 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Network",
                    "VPN or tunnel adapter is active",
                    "محول VPN أو نفق نشط",
                    $"Active encrypted/tunnel interfaces: {string.Join(", ", activeVpn)}. Encryption adds overhead and can raise ping by 10-50 ms.",
                    DiagnosticSeverity.Warning,
                    "Disable the VPN while gaming unless you need it for privacy or work.",
                    "أوقف VPN أثناء اللعب إلا إذا كنت تحتاجه للخصوصية أو العمل."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Network", "VPN adapter check failed",
                "فشل فحص محولات VPN", ex.Message, ex.Message));
            LogWarning("VPN check failed.", ex.Message);
        }

        return findings;
    }

    // ------------------------------------------------------------------ services & tweaks

    private async Task<(List<DiagnosticFinding> Findings, IReadOnlyList<ServiceStateInfo> Services)> CheckServicesAsync(
        CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        var services = new List<ServiceStateInfo>();

        try
        {
            var read = await Task.Run(() =>
            {
                var states = new List<ServiceStateInfo>();
                foreach (var definition in ServiceCatalog.All)
                {
                    try
                    {
                        states.Add(_windowsServices.GetState(definition));
                    }
                    catch (Exception ex)
                    {
                        states.Add(new ServiceStateInfo(
                            definition.Id, definition.NameEn, ServicePauseState.Unknown, false, ex.Message));
                    }
                }

                return states;
            }, cancellationToken).ConfigureAwait(false);

            services.AddRange(read);

            var updateServiceIds = new[] { "wuauserv", "dosvc", "bits" };
            var runningUpdates = services
                .Where(state => state.IsRunning &&
                                updateServiceIds.Contains(state.Id, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (runningUpdates.Length > 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Updates",
                    "Windows Update background services are running",
                    "خدمات تحديث ويندوز تعمل في الخلفية",
                    $"Running: {string.Join(", ", runningUpdates.Select(state => state.DisplayName))}. They can download updates mid-match and consume bandwidth.",
                    DiagnosticSeverity.Info,
                    "Enable 'Pause Windows download services' in Fusion Mode to stop them during play.",
                    "فعّل 'إيقاف خدمات تنزيل ويندوز' في وضع Fusion لإيقافها أثناء اللعب."));
            }
            else
            {
                findings.Add(new DiagnosticFinding(
                    "Updates",
                    "Windows Update background services are stopped",
                    "خدمات تحديث ويندوز متوقفة",
                    "No Windows Update download service is currently running, so it cannot compete for bandwidth.",
                    DiagnosticSeverity.Ok));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Services", "Service states could not be read",
                "تعذر قراءة حالة الخدمات", ex.Message, ex.Message));
            LogWarning("Service state check failed.", ex.Message);
        }

        return (findings, services);
    }

    private async Task<(List<DiagnosticFinding> Findings, IReadOnlyList<TweakState> Tweaks)> CheckTweaksAsync(
        CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        IReadOnlyList<TweakState> states = [];

        try
        {
            states = await Task.Run(() => _tweaks.ReadAll(TweakCatalog.All), cancellationToken).ConfigureAwait(false);

            var recommended = TweakCatalog.Recommended;
            var appliedById = states
                .Where(state => state.Applied)
                .Select(state => state.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = recommended
                .Where(definition => !appliedById.Contains(definition.Id))
                .ToArray();

            if (missing.Length > 0)
            {
                findings.Add(new DiagnosticFinding(
                    "Performance",
                    $"{missing.Length} recommended tweak(s) are not applied",
                    $"{missing.Length} من التحسينات الموصى بها غير مطبقة",
                    "Not applied: " + string.Join(", ", missing.Select(definition => definition.TitleEn)) + ".",
                    DiagnosticSeverity.Info,
                    "Review the Game Mode settings on the Optimizer page. Other TCP and registry options require measurement and do not guarantee lower ping.",
                    "راجع إعدادات وضع الألعاب من صفحة التحسين. تحتاج خيارات TCP والسجل الأخرى قياساً ولا تضمن خفض البنق."));
            }
            else
            {
                findings.Add(new DiagnosticFinding(
                    "Performance",
                    "All recommended tweaks are applied",
                    "جميع التحسينات الموصى بها مطبقة",
                    $"{recommended.Count} recommended Game Mode settings are active; this does not measure connection quality.",
                    DiagnosticSeverity.Ok));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Performance", "Tweak states could not be read",
                "تعذر قراءة حالة التحسينات", ex.Message, ex.Message));
            LogWarning("Tweak state check failed.", ex.Message);
        }

        return (findings, states);
    }

    // ------------------------------------------------------------------ latency & gateway

    private async Task<(List<DiagnosticFinding> Findings, Dictionary<string, TargetStatistics> Statistics)>
        CheckLatencyAndGatewayAsync(IReadOnlyList<AdapterInfo> adapters, CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>();
        var statistics = new Dictionary<string, TargetStatistics>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var defaultAdapter = adapters.FirstOrDefault(adapter => adapter.IsDefaultRoute && IsUp(adapter))
                                 ?? adapters.FirstOrDefault(IsUp);
            var gateway = string.IsNullOrWhiteSpace(defaultAdapter?.Gateway) ? null : defaultAdapter!.Gateway!.Trim();

            var targets = new List<string>(InternetTargets.Length + 1);
            if (gateway is not null) targets.Add(gateway);
            foreach (var target in InternetTargets)
            {
                if (!targets.Contains(target, StringComparer.OrdinalIgnoreCase)) targets.Add(target);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var probeTasks = targets.Select(target => ProbeTargetAsync(target, cancellationToken)).ToArray();
            var results = await Task.WhenAll(probeTasks).ConfigureAwait(false);
            foreach (var result in results) statistics[result.Target] = result;

            // Gateway reachability.
            if (gateway is null)
            {
                findings.Add(new DiagnosticFinding(
                    "Network",
                    "No default gateway is configured",
                    "لا توجد بوابة افتراضية مُعدّة",
                    "The active adapter has no default gateway, so internet traffic has no route.",
                    DiagnosticSeverity.Warning,
                    "Renew the adapter address (ipconfig /renew) or check the router's DHCP settings.",
                    "جدّد عنوان المحول (ipconfig /renew) أو افحص إعدادات DHCP في الراوتر."));
            }
            else if (statistics.TryGetValue(gateway, out var gatewayStats))
            {
                if (gatewayStats.Received > 0)
                {
                    findings.Add(new DiagnosticFinding(
                        "Network",
                        "Gateway is reachable",
                        "البوابة الافتراضية تستجيب",
                        $"Router {gateway} replied in {Humanize.Ms(gatewayStats.AverageMs, 1)} on average ({gatewayStats.Received}/{gatewayStats.Sent} replies).",
                        DiagnosticSeverity.Ok));
                }
                else
                {
                    findings.Add(new DiagnosticFinding(
                        "Network",
                        "Gateway is not responding",
                        "البوابة الافتراضية لا تستجيب",
                        $"No ICMP reply from {gateway} in {gatewayStats.Sent} probes. The router may block ping, or the local link is failing.",
                        DiagnosticSeverity.Warning,
                        "Reboot the router and re-check the cable; if internet still works, the router simply filters ICMP.",
                        "أعد تشغيل الراوتر وافحص الكبل؛ وإذا كان الإنترنت يعمل فالراوتر يحجب ICMP فقط."));
                }
            }

            // Internet latency / loss.
            var internetStats = InternetTargets
                .Where(target => statistics.ContainsKey(target))
                .Select(target => statistics[target])
                .ToArray();

            var anyReply = internetStats.Any(state => state.Received > 0);
            if (!anyReply)
            {
                findings.Add(new DiagnosticFinding(
                    "Network",
                    "No internet response",
                    "لا يوجد استجابة من الإنترنت",
                    $"All {InternetTargets.Length} public probes were lost. The machine has no working internet path even if the local network link is up.",
                    DiagnosticSeverity.Critical,
                    "Check the router, the ISP status, and whether another device can reach the internet.",
                    "افحص الراوتر وحالة مزوّد الخدمة وما إذا كان جهاز آخر يستطيع الوصول إلى الإنترنت."));
            }
            else
            {
                var problems = 0;
                foreach (var state in internetStats.Where(state => state.Sent > 0))
                {
                    if (state.LossPercent >= 25)
                    {
                        problems++;
                        findings.Add(new DiagnosticFinding(
                            "Network",
                            $"Packet loss to {state.Target}",
                            $"فقدان حزم إلى {state.Target}",
                            $"{Humanize.Percent(state.LossPercent, 0)} packet loss ({state.Lost}/{state.Sent}). Loss is far more damaging to online games than latency.",
                            state.LossPercent >= 50 ? DiagnosticSeverity.Critical : DiagnosticSeverity.Warning,
                            "Use Ethernet, restart the router, and close anything uploading in the background.",
                            "استخدم سلك الشبكة وأعد تشغيل الراوتر وأغلق أي رفع في الخلفية."));
                    }
                    else if (state.AverageMs > 120)
                    {
                        problems++;
                        findings.Add(new DiagnosticFinding(
                            "Network",
                            $"High latency to {state.Target}",
                            $"زمن استجابة مرتفع إلى {state.Target}",
                            $"Average {Humanize.Ms(state.AverageMs, 1)} with {Humanize.Ms(state.JitterMs, 1)} jitter over {state.Received} replies.",
                            DiagnosticSeverity.Warning,
                            "Pick a closer game server region and check whether a VPN or background download is adding latency.",
                            "اختر منطقة سيرفر أقرب وتحقق مما إذا كان VPN أو تنزيل في الخلفية يضيف زمن استجابة."));
                    }
                    else if (state.JitterMs > 60)
                    {
                        problems++;
                        findings.Add(new DiagnosticFinding(
                            "Network",
                            $"High jitter to {state.Target}",
                            $"تقلب زمن استجابة مرتفع إلى {state.Target}",
                            $"Jitter {Humanize.Ms(state.JitterMs, 1)} against an average of {Humanize.Ms(state.AverageMs, 1)}. Jitter causes rubber-banding in games.",
                            DiagnosticSeverity.Warning,
                            "Prefer a wired connection and disable Wi-Fi power saving on the adapter.",
                            "فضّل الاتصال السلكي وأوقف توفير الطاقة للواي فاي في المحول."));
                    }
                }

                if (problems == 0)
                {
                    var best = internetStats.OrderBy(state => state.AverageMs).First();
                    findings.Add(new DiagnosticFinding(
                        "Network",
                        "Latency and packet loss are healthy",
                        "زمن الاستجابة وفقدان الحزم بحالة جيدة",
                        $"Best public target {best.Target}: {Humanize.Ms(best.AverageMs, 1)} average, {Humanize.Percent(best.LossPercent, 0)} loss, {Humanize.Ms(best.JitterMs, 1)} jitter.",
                        DiagnosticSeverity.Ok));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            findings.Add(Info("Network", "Latency check failed",
                "فشل فحص زمن الاستجابة", ex.Message, ex.Message));
            LogWarning("Latency check failed.", ex.Message);
        }

        return (findings, statistics);
    }

    private async Task<TargetStatistics> ProbeTargetAsync(string target, CancellationToken cancellationToken)
    {
        var accumulator = new PingStatisticsAccumulator(target, capacity: 16);

        for (var index = 0; index < ProbesPerTarget; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PingSample sample;
            try
            {
                sample = await _ping.ProbeAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sample = new PingSample(target, index + 1, null, DateTimeOffset.UtcNow, ex.Message);
            }

            accumulator.Add(sample);
        }

        return accumulator.Snapshot();
    }

    // ------------------------------------------------------------------ text report

    public string BuildTextReport(DiagnosticReport report)
    {
        if (report is null) return "FusionNet Pro — Diagnostic Report: no report data was supplied.";

        try
        {
            var builder = new StringBuilder(8_192);
            const string rule = "================================================================================";
            const string thinRule = "--------------------------------------------------------------------------------";

            builder.AppendLine(rule);
            builder.AppendLine(" FusionNet Pro — Diagnostic Report");
            builder.AppendLine(" تقرير تشخيص FusionNet Pro");
            builder.AppendLine(rule);
            builder.AppendLine($"Generated (local)   : {report.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"Generated (UTC)     : {report.GeneratedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
            builder.AppendLine($"Machine             : {report.MachineName}");
            builder.AppendLine($"Windows             : {report.WindowsVersion}");
            builder.AppendLine($"App version         : {report.AppVersion}");
            builder.AppendLine($"Administrator       : {(report.Elevated ? "Yes" : "No")}");
            builder.AppendLine($"Overall severity    : {SeverityLabel(report.OverallSeverity)}");
            builder.AppendLine(thinRule);

            builder.AppendLine("SUMMARY / الملخص");
            builder.AppendLine($"  Critical / حرج  : {report.CriticalCount}");
            builder.AppendLine($"  Warning / تحذير : {report.WarningCount}");
            builder.AppendLine($"  Info / معلومات  : {report.Findings.Count(finding => finding.Severity == DiagnosticSeverity.Info)}");
            builder.AppendLine($"  Ok / سليم       : {report.Findings.Count(finding => finding.Severity == DiagnosticSeverity.Ok)}");
            builder.AppendLine($"  Adapters / المحولات: {report.Adapters.Count}   ·   Ping targets: {report.PingStatistics.Count}   ·   Services: {report.Services.Count}   ·   Tweaks: {report.Tweaks.Count}");
            builder.AppendLine(thinRule);

            builder.AppendLine($"FINDINGS / النتائج ({report.Findings.Count})");
            builder.AppendLine(thinRule);
            if (report.Findings.Count == 0)
            {
                builder.AppendLine("  (none)");
            }
            else
            {
                var index = 1;
                foreach (var finding in report.Findings)
                {
                    builder.AppendLine($" {index,2}. [{SeverityTag(finding.Severity)}] {finding.Category} — {finding.TitleEn}");
                    builder.AppendLine($"     AR: {finding.TitleAr}");
                    builder.AppendLine($"     Detail: {finding.Detail}");
                    if (!string.IsNullOrWhiteSpace(finding.SuggestionEn))
                        builder.AppendLine($"     Suggestion: {finding.SuggestionEn}");
                    if (!string.IsNullOrWhiteSpace(finding.SuggestionAr))
                        builder.AppendLine($"     AR: {finding.SuggestionAr}");
                    builder.AppendLine();
                    index++;
                }
            }

            builder.AppendLine(thinRule);
            builder.AppendLine($"ADAPTERS / المحولات ({report.Adapters.Count})");
            builder.AppendLine(thinRule);
            if (report.Adapters.Count == 0)
            {
                builder.AppendLine("  (no physical adapters reported)");
            }
            else
            {
                foreach (var adapter in report.Adapters)
                {
                    var flags = new List<string>();
                    if (adapter.IsDefaultRoute) flags.Add("default route");
                    if (adapter.IsWireless) flags.Add("Wi-Fi");
                    builder.AppendLine($"- {adapter.Name} [{adapter.Status}] · {Humanize.Speed(adapter.LinkSpeedBps)}"
                                       + (flags.Count > 0 ? " · " + string.Join(" · ", flags) : string.Empty));
                    builder.AppendLine($"    Description: {adapter.Description}");
                    builder.AppendLine($"    MAC: {(string.IsNullOrWhiteSpace(adapter.MacAddress) ? "—" : adapter.MacAddress)}");
                    builder.AppendLine($"    IPv4: {(adapter.IPv4Addresses.Count == 0 ? "—" : string.Join(", ", adapter.IPv4Addresses))}");
                    builder.AppendLine($"    DNS:  {(adapter.DnsServers.Count == 0 ? "—" : string.Join(", ", adapter.DnsServers))}");
                    builder.AppendLine($"    Gateway: {adapter.Gateway ?? "—"}");
                    if (adapter.IsWireless)
                    {
                        builder.AppendLine($"    Wi-Fi: SSID {(string.IsNullOrWhiteSpace(adapter.Ssid) ? "—" : adapter.Ssid)} · Signal {(adapter.SignalPercent.HasValue ? Humanize.Percent(adapter.SignalPercent.Value) : "—")}");
                    }
                    builder.AppendLine($"    Traffic: {Humanize.Bytes(adapter.ReceivedBytes)} received / {Humanize.Bytes(adapter.SentBytes)} sent");
                }
            }

            builder.AppendLine();
            builder.AppendLine(thinRule);
            builder.AppendLine($"PING / قياس زمن الاستجابة ({report.PingStatistics.Count})");
            builder.AppendLine(thinRule);
            if (report.PingStatistics.Count == 0)
            {
                builder.AppendLine("  (no ping statistics)");
            }
            else
            {
                var index = 1;
                foreach (var pair in report.PingStatistics.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var state = pair.Value;
                    builder.AppendLine($" {index,2}. {state.Target}");
                    builder.AppendLine($"     Received {state.Received}/{state.Sent} · Loss {Humanize.Percent(state.LossPercent, 0)} · " +
                                       $"Min {Humanize.Ms(state.MinMs, 1)} · Avg {Humanize.Ms(state.AverageMs, 1)} · Max {Humanize.Ms(state.MaxMs, 1)} · " +
                                       $"Jitter {Humanize.Ms(state.JitterMs, 1)} · Quality {state.QualityScore:0}/100");
                    index++;
                }
            }

            builder.AppendLine();
            builder.AppendLine(thinRule);
            builder.AppendLine($"SERVICES / الخدمات ({report.Services.Count})");
            builder.AppendLine(thinRule);
            if (report.Services.Count == 0)
            {
                builder.AppendLine("  (no service states)");
            }
            else
            {
                foreach (var service in report.Services)
                {
                    var state = service.State.ToString().ToLowerInvariant();
                    builder.AppendLine($"  [{state}] {service.DisplayName} ({service.Id})" +
                                       (string.IsNullOrWhiteSpace(service.ErrorText) ? string.Empty : $" — {service.ErrorText}"));
                }
            }

            builder.AppendLine();
            builder.AppendLine(thinRule);
            builder.AppendLine($"TWEAKS / التحسينات ({report.Tweaks.Count})");
            builder.AppendLine(thinRule);
            if (report.Tweaks.Count == 0)
            {
                builder.AppendLine("  (tweak states unavailable)");
            }
            else
            {
                foreach (var tweak in report.Tweaks)
                {
                    var definition = TweakCatalog.Find(tweak.Id);
                    var title = definition?.TitleEn ?? tweak.Id;
                    builder.AppendLine($"  [{(tweak.Applied ? "applied" : "  --   ")}] {title} ({tweak.Id})" +
                                       (string.IsNullOrWhiteSpace(tweak.ErrorText) ? string.Empty : $" — {tweak.ErrorText}"));
                }
            }

            builder.AppendLine();
            builder.AppendLine(thinRule);
            builder.AppendLine("ENVIRONMENT / البيئة");
            builder.AppendLine(thinRule);
            builder.AppendLine($"  Machine name : {report.MachineName}");
            builder.AppendLine($"  Windows      : {report.WindowsVersion}");
            builder.AppendLine($"  App version  : {report.AppVersion}");
            builder.AppendLine($"  Elevated     : {(report.Elevated ? "Yes" : "No")}");
            builder.AppendLine($"  Runtime      : .NET {Environment.Version}");
            builder.AppendLine($"  Architecture : {RuntimeInformation.OSArchitecture}");
            builder.AppendLine($"  Time zone    : {TimeZoneInfo.Local.DisplayName}");
            builder.AppendLine($"  Report folder: {GetReportsDirectory()}");
            builder.AppendLine(rule);
            builder.AppendLine(" End of report / نهاية التقرير");
            builder.AppendLine(rule);
            return builder.ToString();
        }
        catch (Exception ex)
        {
            // A malformed report must still leave the user with a file they can send us.
            return $"FusionNet Pro — Diagnostic Report{Environment.NewLine}" +
                   $"The report could not be rendered: {ex.Message}{Environment.NewLine}" +
                   $"Generated: {report.GeneratedAtUtc:O}{Environment.NewLine}" +
                   $"Machine: {report.MachineName}{Environment.NewLine}" +
                   "نعتذر، تعذر إنشاء التقرير النصي. يرجى إرسال هذا الملف مع وصف المشكلة.";
        }
    }

    private static string GetReportsDirectory()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "FusionNetPro", "reports");
        }
        catch (Exception)
        {
            return "%LOCALAPPDATA%\\FusionNetPro\\reports";
        }
    }

    private static string SeverityTag(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => "CRITICAL",
        DiagnosticSeverity.Warning => "WARNING ",
        DiagnosticSeverity.Info => "INFO    ",
        _ => "OK      "
    };

    private static string SeverityLabel(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Critical => "CRITICAL / حرج",
        DiagnosticSeverity.Warning => "WARNING / تحذير",
        DiagnosticSeverity.Info => "INFO / معلومات",
        _ => "OK / سليم"
    };

    // ------------------------------------------------------------------ helpers

    private static bool IsUp(AdapterInfo adapter) =>
        string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase);

    private static DiagnosticFinding Info(string category, string titleEn, string titleAr, string detail, string? errorText) =>
        new(category, titleEn, titleAr, detail, DiagnosticSeverity.Info, errorText, errorText);

    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            // A stats provider may fail on unusual hardware/VM configurations.
            return fallback;
        }
    }

    private void LogInfo(string message, string? detail = null)
    {
        try
        {
            _activity.Info(Category, message, detail);
        }
        catch (Exception)
        {
            // Logging must never take the diagnostics engine down.
        }
    }

    private void LogWarning(string message, string? detail = null)
    {
        try
        {
            _activity.Warning(Category, message, detail);
        }
        catch (Exception)
        {
            // Logging must never take the diagnostics engine down.
        }
    }

    private void LogError(string message, string? detail = null)
    {
        try
        {
            _activity.Error(Category, message, detail);
        }
        catch (Exception)
        {
            // Logging must never take the diagnostics engine down.
        }
    }
}


