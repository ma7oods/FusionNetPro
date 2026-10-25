namespace FusionNet.Core.Models;

/// <summary>A user-added background application (any .exe) that FusionNet can control.</summary>
public sealed record CustomManagedApp(
    string Id,
    string Name,
    string ExecutablePath,
    AppControlAction Action,
    bool Enabled = true)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(ExecutablePath);

    public CustomManagedApp Normalized(AppControlAction fallbackAction = AppControlAction.BlockNetwork) => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? BuildId(ExecutablePath) : Id,
        Name = string.IsNullOrWhiteSpace(Name) ? ProcessName : Name.Trim(),
        ExecutablePath = Path.GetFullPath(ExecutablePath),
        Action = Action == AppControlAction.Ignore ? fallbackAction : Action
    };

    public static string BuildId(string executablePath) =>
        "custom-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(executablePath).ToLowerInvariant())))[..12].ToLowerInvariant();

    public static CustomManagedApp Create(string executablePath, AppControlAction action = AppControlAction.BlockNetwork)
    {
        var full = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a valid .exe file.", nameof(executablePath));

        return new CustomManagedApp(BuildId(full), Path.GetFileNameWithoutExtension(full), full, action);
    }

    public ManagedApp ToManagedApp(string category = "Custom") => new(
        Id,
        Name,
        category,
        "User added application",
        [ProcessName],
        Action,
        false,
        [ExecutablePath]);
}
