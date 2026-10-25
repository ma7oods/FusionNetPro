using System.Buffers.Binary;

namespace FusionNet.Core.Monitoring;

public sealed record AppLaunchEvidence(string Entry, DateTimeOffset LastRecordedLaunchUtc, bool OlderThanSixMonths);

public static class ExplorerLaunchHistory
{
    public static AppLaunchEvidence? Parse(string encodedName, ReadOnlySpan<byte> data, DateTimeOffset observedUtc)
    {
        if(data.Length!=72||encodedName.Length is 0 or >2048)return null;
        var name=new string(encodedName.Select(character=>character switch
        {
            >= 'a' and <= 'z'=>(char)('a'+(character-'a'+13)%26),
            >= 'A' and <= 'Z'=>(char)('A'+(character-'A'+13)%26),
            _=>character
        }).ToArray());
        if(name.Any(char.IsControl)||!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return null;
        var drive=name.Length>3&&char.IsAsciiLetter(name[0])&&name[1]==':'&&name[2]=='\\';
        var folder=name.Length>40&&name[38]=='\\'&&Guid.TryParseExact(name[..38],"B",out _);
        if(!drive&&!folder)return null;
        var time=BinaryPrimitives.ReadInt64LittleEndian(data[60..]);
        try
        {
            var date=DateTimeOffset.FromFileTime(time).ToUniversalTime();
            if(date.Year<2000||date>observedUtc)return null;
            return new(name,date,date<observedUtc.AddMonths(-6));
        }
        catch(ArgumentOutOfRangeException){return null;}
    }
}
