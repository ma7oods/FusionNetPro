namespace FusionNet.Core.Models;

/// <summary>
/// One registry value FusionNet can apply and later restore.
/// Descriptions are bilingual because the UI ships in Arabic and English.
/// </summary>
public sealed record TweakDefinition(
    string Id,
    string Category,
    TweakScope Scope,
    string RegistryPath,
    string ValueName,
    TweakValueKind Kind,
    object Value,
    TweakRisk Risk,
    bool Recommended,
    bool RequiresRestart,
    string TitleEn,
    string TitleAr,
    string DescriptionEn,
    string DescriptionAr)
{
    public object EffectiveValue => Kind switch
    {
        TweakValueKind.Dword => unchecked((int)Convert.ToInt64(Value)),
        TweakValueKind.Qword => Convert.ToInt64(Value),
        _ => Convert.ToString(Value) ?? string.Empty
    };

    public string ExpectedDisplay => Kind switch
    {
        TweakValueKind.Dword => $"0x{Convert.ToInt64(Value):X8}",
        TweakValueKind.Qword => $"0x{Convert.ToInt64(Value):X16}",
        _ => Convert.ToString(Value) ?? string.Empty
    };

    public override string ToString() => TitleEn;
}
