#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Reflection;
using System.Runtime.CompilerServices;
using FusionNet.App.Views.Dashboard;
using FusionNet.Core.Models;
using FusionNet.Core.Planning;
using FusionNet.Windows.System;

// Inspect actual command construction and presentation without invoking Windows services.
var failures = new List<string>();
foreach (var direction in new[] { "in", "out" })
    Check(direction + " command uses supported netsh arguments", () =>
    {
        var rule = new PlannedFirewallRule("FNP_APP_TEST", "App test", @"C:\Apps & Games\app.exe");
        var arguments = Build(rule, direction);
        Require(arguments.Take(4).SequenceEqual(["advfirewall", "firewall", "add", "rule"]), "Wrong command.");
        string[] supported = ["name", "dir", "action", "program", "enable", "profile", "description", "service"];
        Require(arguments.Skip(4).All(argument => supported.Contains(argument.Split('=', 2)[0])), "Unsupported add-rule option.");
        Require(arguments.Contains("program=" + rule.ProgramPath), "Executable path must remain one argument.");
        Require(arguments.Contains("name=FNP_APP_TEST_" + direction.ToUpperInvariant()), "Ownership name missing.");
        Require(arguments.Contains("dir=" + direction) && arguments.Contains("action=block"), "Wrong rule scope.");
    });
Check("service rule retains its service scope", () =>
{
    var arguments = Build(new("FNP_SVC_BITS", "BITS", @"C:\Windows\System32\svchost.exe", "BITS"), "out");
    Require(arguments.Contains("service=BITS"), "Service restriction missing.");
});
Check("failed activation never displays full protection", () =>
{
    var model = (DashboardViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DashboardViewModel));
    var field = typeof(DashboardViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(model, FusionSnapshot.Idle(false) with { State = FusionModeState.Error, LastError = "failed" });
    Require(model.RingProgress == 0 && model.RingPercent == "0%" && !model.IsActive, "Error displayed as protected.");
    field.SetValue(model, FusionSnapshot.Idle(false) with { State = FusionModeState.Active });
    Require(model.RingProgress == 1 && model.RingPercent == "100%" && model.IsActive, "Active display changed.");
});
foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"Firewall command/dashboard checks: {4 - failures.Count}/4 passed");
return failures.Count == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add("FAIL " + name + ": " + ex.Message); }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static IReadOnlyList<string> Build(PlannedFirewallRule rule, string direction) =>
    (IReadOnlyList<string>)typeof(FirewallService)
        .GetMethod("BuildAddArguments", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [rule, rule.Name + "_" + direction.ToUpperInvariant(), direction])!;
