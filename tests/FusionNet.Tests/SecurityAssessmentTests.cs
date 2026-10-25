using FusionNet.Core.Models;
using FusionNet.Core.Monitoring;

namespace FusionNet.Tests;

public class SecurityAssessmentTests
{
    private static ProcessObservation Process(string name, double cpu = 0) => new(42, name,
        new(42, DateTimeOffset.UnixEpoch, @"C:\Users\test\AppData\Local\Temp\app.exe"), cpu,
        1000, 1, null, null, "sampled", "NotSigned");

    [Fact]
    public void HighCpuOrUnsignedAloneNeverMeansMalware()
    {
        var findings = SecurityAssessment.Assess([Process("renderer", 85)], [], []);
        Assert.DoesNotContain(findings, finding => finding.Rule == "mining-indicators");
        Assert.Contains(findings, finding => finding.Rule == "resource-use" && finding.Severity == "Info");
    }

    [Fact]
    public void MinerNameAloneIsInsufficient() =>
        Assert.DoesNotContain(SecurityAssessment.Assess([Process("xmrig")], [], []), finding => finding.Rule == "mining-indicators");

    [Fact]
    public void MiningNeedsIndependentEvidenceAndIsStillAdvisory()
    {
        var findings = SecurityAssessment.Assess([Process("xmrig", 70)],
            [new(42, "TCP", "127.0.0.1", 50100, "192.0.2.1", 3333, "Established", Process("xmrig").Identity)], []);
        var mining = Assert.Single(findings, finding => finding.Rule == "mining-indicators");
        Assert.True(mining.Evidence.Count >= 3);
        Assert.Equal("Medium", mining.Confidence);
    }
    [Fact] public void RemoteControlToolIsReportedOnceWithConnectionEvidence()
    {
        var first = Process("remoting_host");
        var second = first with { Pid = 43, Identity = first.Identity! with { Pid = 43 } };
        var findings = SecurityAssessment.Assess([first, second],
            [new(42, "TCP", "192.168.1.5", 50100, "198.51.100.7", 443, "Established", first.Identity)], []);
        var remote = Assert.Single(findings, finding => finding.Rule == "remote-access");
        Assert.Equal("Info", remote.Severity);
        Assert.Contains(remote.Evidence, evidence => evidence.Contains("198.51.100.7"));
    }
    [Fact] public void OrdinaryAppsAreNotRemoteAccess() =>
        Assert.DoesNotContain(SecurityAssessment.Assess([Process("Discord")], [], []), finding => finding.Rule == "remote-access");
    [Fact] public void ReusedPidCannotJoinUnrelatedMiningConnection()
    {
        var current = Process("xmrig",70);
        var old = current.Identity! with { StartedUtc = current.Identity!.StartedUtc.AddSeconds(-1) };
        Assert.DoesNotContain(SecurityAssessment.Assess([current], [new(42,"TCP","127.0.0.1",1234,"192.0.2.1",3333,"Established",old)], []),
            finding => finding.Rule == "mining-indicators");
    }

    [Fact]
    public void EncodedStartupIsReviewEvidenceNeverAutomaticRemediation()
    {
        var findings = SecurityAssessment.Assess([], [],
            [new("Startup", "Complete", "", [new("task", "Run", "powershell -EncodedCommand AAAA")])]);
        Assert.Contains(findings, finding => finding.Rule == "encoded-persistence" && finding.Confidence == "Low");
    }

    [Fact]
    public void UnavailableChecksRemainVisible() =>
        Assert.Contains(SecurityAssessment.Assess([], [], [new("Defender", "Unavailable", "denied", [])]),
            finding => finding.Rule == "coverage");
    [Fact] public void HistoricalDetectionIsNotAnActiveDiagnosis()
    {
        var findings = SecurityAssessment.Assess([], [], [new("Defender", "Complete", "",
            [new("old", "Get-MpThreatDetection (history; not necessarily active)", "{\"ActionSuccess\":true}"),
             new("old", "Get-MpThreat", "{\"IsActive\":false}")])]);
        Assert.DoesNotContain(findings, finding => finding.Rule == "defender-active");
    }
    [Fact] public void DefenderActiveStateAndPassiveProtectionAreSeparateFindings()
    {
        var findings = SecurityAssessment.Assess([], [], [new("Defender", "Complete", "",
            [new("test", "Get-MpThreat", "{\"IsActive\":true}"),
             new("status", "Get-MpComputerStatus", "{\"RealTimeProtectionEnabled\":false}")])]);
        Assert.Contains(findings, finding => finding.Rule == "defender-active" && finding.Confidence == "High");
        Assert.Contains(findings, finding => finding.Rule == "defender-realtime" && finding.Severity == "Info");
    }
}
