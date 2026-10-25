using System.Globalization;

namespace FusionNet.Core.Models;

/// <summary>A DNS resolver FusionNet can benchmark and apply.</summary>
public sealed record DnsProvider(
    string Id,
    string Name,
    string DescriptionEn,
    string DescriptionAr,
    IReadOnlyList<string> Servers,
    bool Recommended);

public static class DnsCatalog
{
    private static readonly DnsProvider[] Providers =
    [
        new("cloudflare", "Cloudflare",
            "Fastest global resolver, strong privacy policy, no logging of your IP.",
            "أسرع مُحلّل عالمي، سياسة خصوصية قوية، ولا يسجل عنوان IP الخاص بك.",
            ["1.1.1.1", "1.0.0.1"], true),

        new("cloudflare-family", "Cloudflare Family",
            "Cloudflare with malware and adult content filtering.",
            "كلاودفلير مع حجب البرمجيات الضارة والمحتوى غير اللائق.",
            ["1.1.1.3", "1.0.0.3"], false),

        new("google", "Google Public DNS",
            "Very reliable global resolver with excellent peering in the Middle East.",
            "مُحلّل عالمي موثوق جداً وله ارتباطات ممتازة في الشرق الأوسط.",
            ["8.8.8.8", "8.8.4.4"], true),

        new("quad9", "Quad9",
            "Blocks known malicious domains using threat intelligence.",
            "يحجب النطاقات الخبيثة المعروفة باستخدام معلومات التهديدات.",
            ["9.9.9.9", "149.112.112.112"], true),

        new("adguard", "AdGuard DNS",
            "Filters ads and trackers at the DNS level.",
            "يحجب الإعلانات وأدوات التتبع على مستوى DNS.",
            ["94.140.14.14", "94.140.15.15"], false),

        new("opendns", "OpenDNS",
            "Cisco resolver with optional parental controls.",
            "مُحلّل سيسكو مع رقابة أبوية اختيارية.",
            ["208.67.222.222", "208.67.220.220"], false),

        new("controld", "Control D",
            "Fast anycast resolver with configurable filtering.",
            "مُحلّل anycast سريع مع تصفية قابلة للتخصيص.",
            ["76.76.2.0", "76.76.10.0"], false),

        new("nextdns", "NextDNS",
            "Configurable resolver with analytics; add your own configuration ID for full filtering.",
            "مُحلّل قابل للتخصيص مع تحليلات؛ أضف معرّف التكوين الخاص بك لتصفية كاملة.",
            ["45.90.28.0", "45.90.30.0"], false)
    ];

    public static IReadOnlyList<DnsProvider> All { get; } = Array.AsReadOnly(Providers);

    public static IReadOnlyList<DnsProvider> Recommended { get; } =
        Array.AsReadOnly(Providers.Where(p => p.Recommended).ToArray());

    public static DnsProvider? Find(string? id) =>
        Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates a literal IPv4 address in canonical four-part dotted-decimal form.
    /// <see cref="System.Net.IPAddress.TryParse(string, out System.Net.IPAddress)"/> alone is too
    /// lenient for configuration input: it also accepts legacy shorthand ("1.1" meaning 1.0.0.1)
    /// and octal/hex octets ("010.1.1.1" meaning 8.1.1.1), which would silently configure a
    /// different DNS server than the user typed.
    /// </summary>
    public static bool IsValidIPv4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value;
        if (!System.Net.IPAddress.TryParse(text, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length != 4) return false;

        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3) return false;
            if (part.Length > 1 && part[0] == '0') return false; // leading zero = ambiguous (octal)
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) ||
                octet is < 0 or > 255)
            {
                return false;
            }
        }

        return true;
    }
}
