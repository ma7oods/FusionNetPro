using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionNet.Core.Utilities;

/// <summary>Shared JSON options so every persisted FusionNet file stays compatible.</summary>
public static class FusionJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Loads JSON, returning <paramref name="fallback"/> for missing or corrupted files.</summary>
    public static T LoadOrCreate<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var value = Deserialize<T>(File.ReadAllText(path));
            return value ?? fallback();
        }
        catch
        {
            return fallback();
        }
    }

    /// <summary>
    /// Atomic write: a unique temp file is written and then renamed over the destination, so a crash
    /// never leaves half a JSON document and concurrent writers never share (or truncate) a temp file.
    /// The rename is retried briefly because a concurrent reader can hold the destination open.
    /// </summary>
    public static void SaveAtomic<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Serialise before touching the file system: an unsupported value must not create temp files.
        var json = Serialize(value);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            MoveWithRetry(temp, fullPath);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    private static void MoveWithRetry(string source, string destination)
    {
        const int attempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < attempts &&
                                       (ex is IOException or UnauthorizedAccessException))
            {
                // Another reader/writer may momentarily hold the destination; retry briefly.
                Thread.Sleep(10);
            }
        }
    }
}
