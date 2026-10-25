#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../src/FusionNet.App/FusionNet.App.csproj

using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using FusionNet.App.Views.Security;
using FusionNet.Core.Monitoring;
using FusionNet.Windows.Monitoring;

var transport = new FakeTransport();
using var client = new HttpClient(transport);
var analyzer = new ResponsesAiAnalyzer(client);
var preview = new AiAnalysisPreview(new Uri("https://example.test/v1/responses"), "test-model", "{\"test\":true}");
await Reject(() => analyzer.AnalyzeAsync(preview, null, "synthetic-key", default));
Require(transport.Calls == 0, "Request without consent.");
await Reject(() => analyzer.AnalyzeAsync(preview with { Model = "other" }, AiAnalysisConsent.Approve(preview), "synthetic-key", default));
Require(transport.Calls == 0, "Changed request reused consent.");
var response = await analyzer.AnalyzeAsync(preview, AiAnalysisConsent.Approve(preview), "synthetic-key", default);
Require(response.Contains("advisory"), "Output parsing failed.");
using (var body = JsonDocument.Parse(transport.Body!))
    Require(body.RootElement.GetProperty("input").GetString() == preview.Payload && !body.RootElement.GetProperty("store").GetBoolean(), "Reviewed input/storage setting differs.");
Require(transport.Authorization == "Bearer synthetic-key", "Authentication missing.");
foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })
{
    transport.Status = status;
    await Reject(() => analyzer.AnalyzeAsync(preview, AiAnalysisConsent.Approve(preview), "synthetic-key", default));
}
transport.Status = HttpStatusCode.OK;
foreach (var malformed in new[] { "{}", "not JSON", new string('x',65537) })
{
    transport.Response = malformed;
    await Reject(() => analyzer.AnalyzeAsync(preview, AiAnalysisConsent.Approve(preview), "synthetic-key", default));
}
using var canceled = new CancellationTokenSource(); canceled.Cancel();
await Reject(() => analyzer.AnalyzeAsync(preview, AiAnalysisConsent.Approve(preview), null, canceled.Token));
var directory = Path.Combine(Path.GetFullPath("qa"), "ai-key-test-" + Guid.NewGuid().ToString("N"));
var store = new AiCredentialStore(directory);
try
{
    store.Save(preview.Endpoint, "synthetic-key");
    Require(store.Load(preview.Endpoint) == "synthetic-key", "DPAPI round trip failed.");
    Require(store.Load(new Uri("https://different.test/responses")) is null, "Credential leaked to another provider.");
    var file = Directory.GetFiles(directory).Single();
    Require(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("synthetic-key"), "Plaintext credential at rest.");
    File.WriteAllText(file,"corrupt");
    try { store.Load(preview.Endpoint); throw new Exception("Corrupt key accepted."); } catch (CryptographicException) { }
    store.Remove(preview.Endpoint);
    Require(store.Load(preview.Endpoint) is null, "Remove key failed.");
}
finally { if (Directory.Exists(directory)) { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); } }
Console.WriteLine("AI checks passed: consent, exact payload, provider binding, redirects/errors/cancellation/bounds, DPAPI. No live API calls.");
return 0;

static async Task Reject(Func<Task<string>> action)
{
    try { await action(); throw new Exception("Expected rejection."); }
    catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) { }
}
static void Require(bool value,string message) { if(!value) throw new Exception(message); }
sealed class FakeTransport : HttpMessageHandler
{
    public int Calls;
    public string? Body, Authorization;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Response = """{"output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"advisory only"}]}]}""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++;
        Body=await request.Content!.ReadAsStringAsync(token); Authorization=request.Headers.Authorization?.ToString();
        return new HttpResponseMessage(Status) { Content=new StringContent(Response) };
    }
}
