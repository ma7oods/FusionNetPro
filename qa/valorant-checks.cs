#:property TargetFramework=net10.0-windows
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using FusionNet.Windows.System;

// Check the native process-control guard without opening, suspending or changing a process.
foreach (var name in new[] { "RiotClientServices", "RiotClientUx", "RiotClientUxRender", "vgc", "vgtray" })
{
    if (!ProcessController.IsSystemCriticalProcess(name))
        throw new InvalidOperationException("Missing Riot protection: " + name);
}
Console.WriteLine("Valorant process-control protections: 5/5 passed; no OS actions performed.");
