using System.IO;
using System.Security.Cryptography;
using System.Text;
using FusionNet.Core.Monitoring;

namespace FusionNet.App.Views.Security;

public sealed class AiCredentialStore(string directory)
{
    private static byte[] Entropy(Uri endpoint) => SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri));
    private string FilePath(Uri endpoint) => Path.Combine(directory, Convert.ToHexString(Entropy(endpoint)) + ".dpapi");
    public void Save(Uri endpoint, string key)
    {
        AiAnalysis.ValidateEndpoint(endpoint.AbsoluteUri);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl)) throw new ArgumentException("Invalid API key.");
        var plaintext = Encoding.UTF8.GetBytes(key);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, Entropy(endpoint), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(directory);
        var destination = FilePath(endpoint);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string? Load(Uri endpoint)
    {
        var path = FilePath(endpoint);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new CryptographicException("Invalid credential file; re-enter the key.");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(endpoint), DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Remove(Uri endpoint) => File.Delete(FilePath(endpoint));
}
