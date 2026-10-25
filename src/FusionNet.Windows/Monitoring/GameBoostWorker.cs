using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;
using FusionNet.Windows.Native;
using FusionNet.Windows.Network;
using FusionNet.Windows.System;

namespace FusionNet.Windows.Monitoring;

public sealed record GameBoostWorkerRequest(GameBoostPreview Preview, string UserSid, string StopPath);

public sealed class GameBoostWorker(GameBoostWorkerRequest request) : IGameBoostTransactionPlatform
{
    private readonly GameBoostPreview _preview = request.Preview;
    private readonly ProtectedSessionStorage _storage = new(request.Preview.SessionId, request.UserSid);
    private string Group => "FusionNetPro.GB." + _preview.SessionId.ToString("N");
    private bool _snapshotSaved;

    public static async Task<int> RestoreSavedAsync(Guid id)
    {
        if (!ElevationService.CheckIsElevated() || id == Guid.Empty) return 1;
        try
        {
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            var storage = new ProtectedSessionStorage(id, sid);
            storage.ValidateExisting();
            var path = Path.Combine(storage.StateDirectory, "snapshot.json");
            if (new FileInfo(path).Length > 65536) return 1;
            var saved = JsonSerializer.Deserialize<GameBoostWorkerRequest>(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            if (saved is null || saved.UserSid != sid || saved.Preview.SessionId != id || saved.Preview.Targets.IsDefaultOrEmpty || saved.Preview.Targets.Length > 10) return 1;
            using var sessionLock = storage.OpenLock();
            return GameBoostTransaction.Restore(new GameBoostWorker(saved)).State == "Restored" ? 0 : 1;
        }
        catch { return 1; }
    }

    public async Task<int> RunAsync()
    {
        FileStream? sessionLock = null;
        try
        {
            ValidateRequest();
            _storage.Create();
            sessionLock = _storage.OpenLock();
            var status = GameBoostTransaction.Start(this, _preview.Targets.Length);
            sessionLock.Dispose();
            sessionLock = null;
            if (status.State != "Active") return 1;
            while (OwnerAlive() && DateTimeOffset.UtcNow < _preview.ExpiresUtc && !File.Exists(request.StopPath) && GamesAlive())
                await Task.Delay(2000).ConfigureAwait(false);
            sessionLock = _storage.OpenLock();
            return GameBoostTransaction.Restore(this).State == "Restored" ? 0 : 1;
        }
        catch
        {
            // Once the snapshot exists, unknown failures remain visible and the registered task can recover.
            if (_snapshotSaved)
            {
                try { SaveStatus(new("Pending", -1, "لم يكتمل مساعد الجلسة؛ راجع حالة الاسترجاع قبل بدء جلسة أخرى.")); } catch { }
            }
            return 1;
        }
        finally { sessionLock?.Dispose(); }
    }

    private void ValidateRequest()
    {
        RequireAdministrator();
        if (_preview.SessionId == Guid.Empty || _preview.Targets.IsDefaultOrEmpty || _preview.Targets.Length > 10 ||
            _preview.Targets.Select(target => target.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _preview.Targets.Length ||
            _preview.ExpiresUtc <= DateTimeOffset.UtcNow || _preview.ExpiresUtc > DateTimeOffset.UtcNow.AddHours(6) ||
            WindowsIdentity.GetCurrent().User?.Value != request.UserSid)
            throw new InvalidOperationException("Invalid worker request.");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FusionNetPro", "GameBoostStop", _preview.SessionId.ToString("N") + ".stop");
        if (!string.Equals(request.StopPath, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid stop path.");
    }

    public void RequireAdministrator()
    { if (!ElevationService.CheckIsElevated()) throw new UnauthorizedAccessException("Game Boost changes require administrator approval."); }

    public void ValidateTargets()
    {
        if (!OwnerAlive() || DateTimeOffset.UtcNow >= _preview.ExpiresUtc) throw new InvalidOperationException("Session owner or expiry changed.");
        var services = WindowsLocalMonitor.QueryAsync("Services", default).GetAwaiter().GetResult();
        var connections = WindowsLocalMonitor.QueryAsync("Connections", default).GetAwaiter().GetResult();
        var products = WindowsLocalMonitor.QueryAsync("SecurityProducts", default).GetAwaiter().GetResult();
        if (services.Status != "Complete" || connections.Status != "Complete" || products.Status != "Complete")
            throw new InvalidOperationException("Protection inventory is incomplete.");
        var protectedPids = services.Items.Where(item => item.Pid > 0).Select(item => item.Pid!.Value)
            .Concat(connections.Connections.Where(connection => connection.LocalPort == 53 || connection.RemotePort == 53).Select(connection => connection.Pid)).ToHashSet();
        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = Process.GetProcesses();
        try
        {
            foreach (var process in all)
                if (protectedPids.Contains(process.Id) || GameCatalog.All.Any(game => game.MatchesProcess(process.ProcessName)) ||
                    GameCatalog.IsRiotSupportProcess(process.ProcessName) || _preview.ProtectedProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                    if (Win32Native.TryQueryProcessPath(process.Id, out var path, out _)) protectedPaths.Add(path);
        }
        finally { foreach (var process in all) process.Dispose(); }
        using var owner = Process.GetProcessById(_preview.Owner.Pid);
        foreach (var target in _preview.Targets)
        {
            if (!IdentityAlive(target) || protectedPids.Contains(target.Pid) || protectedPaths.Contains(target.Path)) throw new InvalidOperationException("Protected or changed process identity.");
            using var process = Process.GetProcessById(target.Pid);
            var observation = new ProcessObservation(target.Pid, process.ProcessName, target, null, null, null, null, null, "verified", OfflineSignature.Verify(target.Path));
            if (process.SessionId != owner.SessionId || GameBoostPolicy.BlockReason(observation, _preview.ProtectedProcessNames,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), _preview.Owner.Path) is not null)
                throw new InvalidOperationException("Protected or unknown target role.");
            FileSystemInfo? item = new FileInfo(target.Path);
            while (item is not null)
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked target paths are not supported.");
                item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent;
            }
            foreach (var product in products.Items)
                if (product.Detail.Contains(target.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Security product is protected.");
        }
    }

    private bool OwnerAlive() => IdentityAlive(_preview.Owner);
    private bool GamesAlive() => _preview.Games.IsDefaultOrEmpty || _preview.Games.Any(IdentityAlive);
    internal static bool IdentityAlive(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            return process.StartTime.ToUniversalTime().Ticks == identity.StartedUtc.UtcTicks &&
                Win32Native.TryQueryProcessPath(identity.Pid, out var path, out _) && path.Equals(identity.Path, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void SaveSnapshot()
    {
        if (GetOwnedRules().Count != 0) throw new InvalidOperationException("Existing rule name collision.");
        _storage.Write("snapshot.json", JsonSerializer.Serialize(request), createOnly: true);
        _snapshotSaved = true;
        SaveStatus(new("Preparing", 0, "حُفظت حالة الجلسة قبل أول تغيير."));
    }

    public void SaveStatus(GameBoostStatus status) => _storage.Write("status.json", JsonSerializer.Serialize(status));

    public IReadOnlyList<string> GetOwnedRules()
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { Names = Enumerable.Range(0, _preview.Targets.Length).Select(_preview.RuleName).ToArray(), Group })));
        var script = "$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$q=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data +
            "'))|ConvertFrom-Json;$r=@(@(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop)+@(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop))|Where-Object{$q.Names -contains $_.DisplayName};if(@($r|Where-Object{$_.Group -ne $q.Group -or $_.Description -ne $q.Group}).Count){throw 'Ownership mismatch'};ConvertTo-Json -InputObject @($r|Select-Object -ExpandProperty DisplayName -Unique) -Compress";
        var result = NetworkCommandRunner.Run(WindowsLocalMonitor.PowerShellPath, ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], 12000);
        if (!result.Success) throw new InvalidOperationException("Rule query failed; absence cannot be confirmed.");
        var names = JsonSerializer.Deserialize<string[]>(result.StandardOutput) ?? throw new JsonException("Invalid rule query response.");
        if (names.Length > _preview.Targets.Length || names.Any(name => !Enumerable.Range(0, _preview.Targets.Length).Select(_preview.RuleName).Contains(name)))
            throw new JsonException("Unexpected rule query result.");
        return names;
    }

    public void AddRule(int index)
    {
        if (!IdentityAlive(_preview.Targets[index]) || !OwnerAlive() || DateTimeOffset.UtcNow >= _preview.ExpiresUtc)
            throw new InvalidOperationException("Target identity, owner or session expiry changed before application.");
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!;
        try
        {
            rule.Name = _preview.RuleName(index); rule.Description = Group; rule.Grouping = Group;
            rule.ApplicationName = _preview.Targets[index].Path; rule.Protocol = 256; rule.Direction = 2;
            rule.Action = 0; rule.Profiles = int.MaxValue; rule.Enabled = true; rule.InterfaceTypes = "All";
            policy.Rules.Add(rule);
        }
        finally { Marshal.FinalReleaseComObject(rule); Marshal.FinalReleaseComObject(policy); }
    }

    public void RemoveRule(string name)
    {
        if (!GetOwnedRules().Contains(name)) return;
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;
        try { policy.Rules.Remove(name); } finally { Marshal.FinalReleaseComObject(policy); }
    }

    public void InstallRecovery()
    {
        var check = NetworkCommandRunner.Run(WindowsLocalMonitor.PowerShellPath, ["-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop';Get-Command Get-NetFirewallRule,Remove-NetFirewallRule,Unregister-ScheduledTask -ErrorAction Stop | Out-Null;if(@(Get-NetFirewallProfile -PolicyStore ActiveStore -ErrorAction Stop | Where-Object { $_.Enabled -eq $false -or $_.AllowLocalFirewallRules -eq $false }).Count){throw 'Firewall disabled or local policy disallowed; no global changes will be made.'}"], 8000);
        if (!check.Success) throw new InvalidOperationException("Recovery cmdlets unavailable.");
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        try
        {
            scheduler.Connect();
            dynamic folder = scheduler.GetFolder(@"\");
            dynamic definition = scheduler.NewTask(0);
            definition.RegistrationInfo.Description = Group;
            definition.Principal.UserId = "SYSTEM"; definition.Principal.LogonType = 5; definition.Principal.RunLevel = 1;
            definition.Settings.Enabled = true; definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = false; definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT2M"; definition.Settings.MultipleInstances = 2;
            definition.Triggers.Create(8).Enabled = true;
            dynamic timer = definition.Triggers.Create(1);
            timer.StartBoundary = DateTime.Now.AddMinutes(1).ToString("s"); timer.Enabled = true; timer.Repetition.Interval = "PT1M";
            dynamic action = definition.Actions.Create(0);
            action.Path = WindowsLocalMonitor.PowerShellPath;
            action.Arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -Command \"& { " + BuildRecoveryScript() + " }\"";
            folder.RegisterTaskDefinition(_preview.RecoveryTaskName, definition, 2, "SYSTEM", null, 5, null);
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }

    internal string BuildRecoveryScript()
    {
        using var helper = Process.GetCurrentProcess();
        var data = JsonSerializer.Serialize(new { SessionId = _preview.SessionId.ToString("N"), OwnerPid = _preview.Owner.Pid,
            OwnerTicks = _preview.Owner.StartedUtc.UtcTicks, _preview.ExpiresUtc, request.StopPath,
            HelperPid = helper.Id, HelperTicks = helper.StartTime.ToUniversalTime().Ticks,
            RuleNames = Enumerable.Range(0, _preview.Targets.Length).Select(_preview.RuleName).ToArray(), Group, TaskName = _preview.RecoveryTaskName });
        var script = WindowsLocalMonitor.ReadScript("GameBoostRecovery.ps1").Replace("REQUEST_BASE64", Convert.ToBase64String(Encoding.UTF8.GetBytes(data)));
        if (script.Contains('"')) throw new InvalidOperationException("Recovery script quoting is invalid.");
        return script;
    }

    public void RemoveRecovery()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        try
        {
            scheduler.Connect(); dynamic folder = scheduler.GetFolder(@"\");
            dynamic task;
            try { task = folder.GetTask(_preview.RecoveryTaskName); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return; }
            if ((string)task.Definition.RegistrationInfo.Description != Group) throw new InvalidOperationException("Task ownership mismatch.");
            folder.DeleteTask(_preview.RecoveryTaskName, 0);
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }
}
