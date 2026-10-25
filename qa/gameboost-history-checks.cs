#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:project ../src/FusionNet.Windows/FusionNet.Windows.csproj

using System.Reflection;
using System.Text.Json;
using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;
using FusionNet.Windows.Monitoring;

// Workspace fixture only; no ProgramData, identity API, UAC, Firewall or Task Scheduler access.
var root=Path.Combine(Directory.GetCurrentDirectory(),"qa","history-fixture-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var find=typeof(WindowsGameBoostBackend).GetMethod("FindPending",BindingFlags.Static|BindingFlags.NonPublic)!;
const string user="synthetic-current-user";
var count=0;
try
{
    for(var index=0;index<2001;index++)Add(index%2==0?user:"synthetic-other-user","Restored");
    Require(Find()==null,"Completed history blocked an idle installation.");count++;
    var pending=Add(user,"Pending");
    Require(Find()==pending,"Pending session after retained history was missed.");count++;
    File.WriteAllText(Path.Combine(root,pending.ToString("N"),"status.json"),"invalid JSON");
    Require(Find()==pending,"Malformed current status was treated as restored.");count++;
    File.Delete(Path.Combine(root,pending.ToString("N"),"status.json"));
    Require(Find()==pending,"Missing current status was treated as restored.");count++;
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{Find(cancelled.Token);throw new Exception("Cancellation ignored.");}
    catch(TargetInvocationException ex)when(ex.InnerException is OperationCanceledException){count++;}
    Console.WriteLine($"Session history: {count}/{count} passed; 2,001 completed records and workspace-only state.");
}
finally
{
    // The absolute GUID fixture was created above under this workspace; no computed external paths.
    Directory.Delete(root,true);
}
Guid? Find(CancellationToken token=default)=>(Guid?)find.Invoke(null,[root,user,token]);
Guid Add(string sid,string state)
{
    var id=Guid.NewGuid();var directory=Path.Combine(root,id.ToString("N"));Directory.CreateDirectory(directory);
    var preview=new GameBoostPreview(id,new ProcessIdentity(42,DateTimeOffset.UnixEpoch,@"C:\Synthetic\app.exe"),DateTimeOffset.UnixEpoch,[],[],[]);
    File.WriteAllText(Path.Combine(directory,"snapshot.json"),JsonSerializer.Serialize(new GameBoostWorkerRequest(preview,sid,"synthetic.stop")));
    File.WriteAllText(Path.Combine(directory,"status.json"),JsonSerializer.Serialize(new GameBoostStatus(state,0,"synthetic")));
    return id;
}
static void Require(bool value,string message){if(!value)throw new Exception(message);}
