#:property TargetFramework=net10.0-windows
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using System.Net.NetworkInformation;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using FusionNet.Core.Models;
using FusionNet.Windows.Network;
using FusionNet.Windows.Orchestration;

// Read-only regression checks: missing adapter ids must never reach a system command.
var failures = new List<string>();
Check("Explicit missing adapter does not select another NIC", () =>
{
    var automatic = Invoke(typeof(DnsService), "ResolveAdapter", [null]);
    Require(automatic is not null, "This check requires at least one available network adapter.");
    Require(Invoke(typeof(DnsService), "ResolveAdapter", [MissingAdapterId()]) is null,
        "An explicitly requested missing adapter fell back to another NIC.");
});
Check("Missing restore target stays pending", () =>
{
    var result = (OperationResult)Invoke(typeof(FusionCoordinator), "ApplyDns", [MissingAdapterId(), "dhcp"])!;
    Require(!result.Success, "A missing adapter was incorrectly considered restored.");
});
Check("Static DNS restore preserves server order", () =>
{
    var commands = new List<string[]>();
    var result = Restore("1.1.1.1,8.8.8.8,9.9.9.9", arguments =>
    {
        commands.Add(arguments);
        return true;
    });
    Require(result.Success && commands.Count == 3, "The complete DNS server list was not restored.");
    Require(commands[1].Contains("index=2") && commands[2].Contains("index=3"),
        "Secondary servers were restored in the wrong position.");
});
Check("Secondary restore failure is reported", () =>
{
    var calls = 0;
    var result = Restore("1.1.1.1,8.8.8.8,9.9.9.9", _ => ++calls == 1);
    Require(!result.Success, "A secondary DNS failure was incorrectly reported as restored.");
    Require(calls == 2, "Restore continued after a secondary DNS failure.");
});
Check("Primary restore failure stops the operation", () =>
{
    var calls = 0;
    Require(!Restore("1.1.1.1,8.8.8.8", _ => { calls++; return false; }).Success && calls == 1,
        "Restore continued after the primary DNS failed.");
});
Check("Automatic DNS restore reports failure", () =>
{
    var result = Restore("dhcp", arguments =>
    {
        Require(arguments.Contains("source=dhcp"), "Automatic DNS did not request DHCP.");
        return false;
    });
    Require(!result.Success, "A DHCP restore failure was incorrectly reported as restored.");
});
Check("Invalid static journal values never issue commands", () =>
{
    foreach (var previous in new[] { ",,,", "1.1.1.1,invalid", "1.1.1.1,010.1.1.1" })
    {
        var calls = 0;
        var result = Restore(previous, _ => { calls++; return true; });
        Require(!result.Success && calls == 0, "Invalid journal DNS changed the adapter: " + previous);
    }
});
Check("Restore preserves original lists longer than the apply limit", () =>
{
    var servers = new[] { "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9" };
    var commands = new List<string[]>();
    var result = Restore(string.Join(',', servers), arguments => { commands.Add(arguments); return true; });
    Require(result.Success && commands.Count == servers.Length, "Original DNS servers were truncated.");
    for (var index = 0; index < servers.Length; index++)
    {
        Require(commands[index].Contains("address=" + servers[index]), "An original DNS server was not restored.");
        if (index > 0) Require(commands[index].Contains("index=" + (index + 1)), "Wrong original server position.");
    }
});

Console.WriteLine($"Network recovery checks: {8 - failures.Count}/8 passed.");
foreach (var failure in failures) Console.WriteLine("FAIL: " + failure);
return failures.Count == 0 ? 0 : 1;

void Check(string name, Action check)
{
    try { check(); Console.WriteLine("PASS: " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.GetBaseException().Message); }
}

static OperationResult Restore(string previous, Func<string[], bool> run) =>
    (OperationResult)Invoke(typeof(FusionCoordinator), "RestoreDnsServers", ["TEST-NO-SYSTEM-COMMAND", previous, run])!;

static object? Invoke(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type,
    string method, object?[] arguments) =>
    (type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(type.FullName, method)).Invoke(null, arguments);

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string MissingAdapterId()
{
    var id = Guid.NewGuid().ToString("B");
    Require(!NetworkInterface.GetAllNetworkInterfaces().Any(adapter =>
        string.Equals(adapter.Id.Trim('{', '}'), id.Trim('{', '}'), StringComparison.OrdinalIgnoreCase)),
        "The generated missing id unexpectedly exists.");
    return id;
}
