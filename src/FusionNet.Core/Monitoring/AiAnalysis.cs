using System.Text.Json;
using FusionNet.Core.Models;

namespace FusionNet.Core.Monitoring;

public sealed record AiAnalysisPreview(Uri Endpoint, string Model, string Payload);
public sealed class AiAnalysisConsent
{
    private AiAnalysisConsent(AiAnalysisPreview preview) { Preview = preview; }
    public AiAnalysisPreview Preview { get; }
    public static AiAnalysisConsent Approve(AiAnalysisPreview preview) => new(preview);
}
public interface ISecurityAiAnalyzer
{
    Task<string> AnalyzeAsync(AiAnalysisPreview preview, AiAnalysisConsent? consent, string? key, CancellationToken token);
}
public static class AiAnalysis
{
    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("أدخل رابط HTTPS كاملًا لواجهة Responses دون اسم مستخدم أو query أو fragment.");
        return uri;
    }

    public static string BuildPayload(LocalMonitorReport report) => JsonSerializer.Serialize(new
    {
        schema = "fusionnet-minimized-v1",
        observations = report.Processes.Take(100).Select((process, index) => new
        { alias = "p" + index, process.CpuPercent, process.MemoryPercent, signature = process.Signature.StartsWith("Unknown", StringComparison.Ordinal) ? "Unknown" : process.Signature }),
        coverage = report.Sections.Take(20).Select(section => new { section.Name, section.Status }),
        findings = report.Findings.Take(80).Select(finding => new { finding.Rule, finding.Severity, finding.Confidence }),
        limitation = "One bounded local snapshot. CPU or unsigned status alone is not evidence of malware. No identities, paths, addresses or commands are included."
    });
}
