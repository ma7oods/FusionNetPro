#:property TargetFramework=net10.0-windows
#:property PublishAot=false

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

const string version = "3.3.3";
var root = Path.GetFullPath(".");
var dist = Path.Combine(root,"dist");
var app = Path.Combine(dist,$"FusionNetPro-{version}.exe");
var expected = Hash(app);
var sums = File.ReadAllLines(Path.Combine(dist,"SHA256SUMS.txt"));
Require(sums.Length==3,"Expected three release hash entries.");
foreach(var line in sums)
{
    var parts=line.Split("  ",StringSplitOptions.None);
    Require(parts.Length==2 && Path.GetFileName(parts[1])==parts[1],"Invalid release manifest entry.");
    Require(Hash(Path.Combine(dist,parts[1]))==parts[0],"Release hash mismatch: "+parts[1]);
}
foreach(var path in new[]{app,Path.Combine(dist,$"FusionNetPro-Setup-{version}.exe")})
    Require(FileVersionInfo.GetVersionInfo(path).FileVersion==version+".0","Wrong EXE file version: "+path);
using(var zip=ZipFile.OpenRead(Path.Combine(dist,$"FusionNetPro-{version}-portable.zip"))) VerifyZip(zip,expected);
var setup=Assembly.LoadFile(Path.Combine(root,"installer/FusionNet.Setup/bin/Release/net10.0-windows/win-x64/FusionNetPro-Setup.dll"));
using(var payload=setup.GetManifestResourceStream("payload.zip") ?? throw new Exception("Missing installer payload."))
using(var zip=new ZipArchive(payload,ZipArchiveMode.Read)) VerifyZip(zip,expected);
foreach(var name in new[]{"FusionNet.Windows.dll","FusionNet.Core.dll","FusionNetPro.dll"})
{
    var project=name=="FusionNetPro.dll"?"FusionNet.App":Path.GetFileNameWithoutExtension(name);
    var framework=project=="FusionNet.Core"?"net10.0": "net10.0-windows";
    var source=Path.Combine(root,"src",project,"bin/Release",framework,project=="FusionNet.App"?"win-x64":"",name);
    Require(Hash(source)==Hash(Path.Combine(dist,"app",name)),"Published/source assembly mismatch: "+name);
}
var appAssembly=Assembly.LoadFile(Path.Combine(dist,"app/FusionNetPro.dll"));
using(var license=appAssembly.GetManifestResourceStream("FusionNet.App.Assets.THIRD-PARTY-NOTICES.txt") ?? throw new Exception("Standalone EXE source assembly lacks licenses."))
    Require(HashStream(license)==Hash(Path.Combine(root,"src/FusionNet.App/Assets/THIRD-PARTY-NOTICES.txt")),"Embedded notices differ.");
Console.WriteLine("Release3.3.3 passed: 3 manifest hashes, EXE versions, portable/embedded Setup payload, notices and published/source assemblies. No EXE executed.");
return 0;

static string Hash(string path) { using var stream=File.OpenRead(path); return HashStream(stream); }
static string HashStream(Stream stream)=>Convert.ToHexString(SHA256.HashData(stream));
static void Require(bool test,string message) { if(!test) throw new InvalidOperationException(message); }
static void VerifyZip(ZipArchive zip,string expected)
{
    using var app=zip.GetEntry("FusionNetPro.exe")?.Open() ?? throw new Exception("Missing payload app.");
    Require(HashStream(app)==expected,"Payload EXE mismatch.");
    using var notices=zip.GetEntry("THIRD-PARTY-NOTICES.txt")?.Open() ?? throw new Exception("Missing payload license notices.");
    using var reader=new StreamReader(notices);
    var noticeText=reader.ReadToEnd();
    Require(noticeText.Contains("Permission is hereby granted"),"MIT license text incomplete.");
    Require(noticeText.Contains("DnsClient.NET 1.8.0 (Apache-2.0)") &&
            noticeText.Contains("Version 2.0, January 2004"),"DnsClient Apache-2.0 attribution or license missing.");
}

