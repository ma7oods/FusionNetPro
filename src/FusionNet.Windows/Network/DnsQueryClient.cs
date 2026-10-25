using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace FusionNet.Windows.Network;

/// <summary>One raw DNS answer, including how long the resolver took to respond.</summary>
internal sealed record DnsQueryResponse(
    bool Success,
    double RoundTripMs,
    IReadOnlyList<string> Addresses,
    string? ErrorText);

/// <summary>
/// Minimal DNS client: builds an <c>A</c> query by hand, sends it over UDP directly to one chosen
/// resolver and parses the answer (header, question and answer records). This is what lets the
/// benchmark measure <b>a specific server</b> instead of whatever Windows has configured, without
/// any external DNS library.
/// </summary>
internal static class DnsQueryClient
{
    public const int DefaultTimeoutMs = 1_500;

    private const ushort TypeA = 1;
    private const ushort ClassIn = 1;
    private const int HeaderLength = 12;

    /// <summary>
    /// Resolves <paramref name="domain"/>'s A record through <paramref name="server"/>.
    /// Returns a failure response for timeouts, malformed packets and DNS error codes;
    /// only caller cancellation is propagated as an exception.
    /// </summary>
    public static async Task<DnsQueryResponse> QueryAsync(
        string server,
        string domain,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(server, out var serverAddress) ||
            serverAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return new DnsQueryResponse(false, 0, [], "invalid DNS server address");
        }

        byte[] query;
        try
        {
            query = BuildQuery(domain);
        }
        catch (Exception ex)
        {
            return new DnsQueryResponse(false, 0, [], ex.Message);
        }

        using var udp = new UdpClient(serverAddress.AddressFamily);
        try
        {
            udp.Connect(serverAddress, 53);
        }
        catch (Exception ex)
        {
            return new DnsQueryResponse(false, 0, [], $"socket: {ex.Message}");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Math.Clamp(timeoutMs, 200, 10_000));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(query, timeoutCts.Token).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return ParseResponse(response.Buffer, query, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new DnsQueryResponse(false, stopwatch.Elapsed.TotalMilliseconds, [], "timeout");
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return new DnsQueryResponse(false, stopwatch.Elapsed.TotalMilliseconds, [], ex.SocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DnsQueryResponse(false, stopwatch.Elapsed.TotalMilliseconds, [], ex.Message);
        }
    }

    private static byte[] BuildQuery(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain is empty", nameof(domain));

        var asciiDomain = ToAscii(domain.Trim().TrimEnd('.'));
        if (asciiDomain.Length is 0 or > 253) throw new ArgumentException("invalid domain", nameof(domain));

        var queryId = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
        var buffer = new List<byte>(64 + asciiDomain.Length);

        WriteUInt16(buffer, queryId);      // ID
        WriteUInt16(buffer, 0x0100);       // RD (recursion desired)
        WriteUInt16(buffer, 1);            // QDCOUNT
        WriteUInt16(buffer, 0);            // ANCOUNT
        WriteUInt16(buffer, 0);            // NSCOUNT
        WriteUInt16(buffer, 0);            // ARCOUNT

        foreach (var label in asciiDomain.Split('.'))
        {
            if (label.Length is 0 or > 63) throw new ArgumentException("invalid domain label", nameof(domain));
            buffer.Add((byte)label.Length);
            buffer.AddRange(Encoding.ASCII.GetBytes(label));
        }

        buffer.Add(0);                     // root label
        WriteUInt16(buffer, TypeA);        // QTYPE
        WriteUInt16(buffer, ClassIn);      // QCLASS
        return [.. buffer];
    }

    private static string ToAscii(string domain)
    {
        try
        {
            if (domain.All(character => character < 128)) return domain;
            return new IdnMapping().GetAscii(domain);
        }
        catch (Exception)
        {
            return domain;
        }
    }

    private static DnsQueryResponse ParseResponse(byte[] message, byte[] query, double roundTripMs)
    {
        try
        {
            if (message.Length < HeaderLength) return Failure(roundTripMs, "truncated response");

            var id = ReadUInt16(message, 0);
            if (id != ReadUInt16(query, 0)) return Failure(roundTripMs, "transaction id mismatch");

            var flags = ReadUInt16(message, 2);
            if ((flags & 0x8000) == 0) return Failure(roundTripMs, "not a response");
            if ((flags & 0x7800) != 0) return Failure(roundTripMs, "unexpected DNS opcode");
            // shortcut: benchmark is UDP-only; add TCP fallback if larger answers are needed.
            if ((flags & 0x0200) != 0) return Failure(roundTripMs, "truncated UDP response");

            var rcode = flags & 0x000F;
            if (rcode != 0) return Failure(roundTripMs, DescribeResponseCode(rcode));

            var questionCount = ReadUInt16(message, 4);
            var answerCount = ReadUInt16(message, 6);
            if (questionCount != 1) return Failure(roundTripMs, "question count mismatch");

            var offset = HeaderLength;
            var question = ReadName(message, ref offset);
            var queryOffset = HeaderLength;
            var expectedQuestion = ReadName(query, ref queryOffset);
            if (offset + 4 > message.Length) return Failure(roundTripMs, "truncated question");
            if (!question.AsSpan().SequenceEqual(expectedQuestion) ||
                ReadUInt16(message, offset) != TypeA || ReadUInt16(message, offset + 2) != ClassIn)
                return Failure(roundTripMs, "question mismatch");
            offset += 4;

            var addresses = new List<string>(2);
            var recordCount = answerCount + ReadUInt16(message, 8) + ReadUInt16(message, 10);
            for (var i = 0; i < recordCount; i++)
            {
                _ = ReadName(message, ref offset);
                if (offset + 10 > message.Length) return Failure(roundTripMs, "truncated record header");

                var type = ReadUInt16(message, offset);
                var recordClass = ReadUInt16(message, offset + 2);
                var dataLength = ReadUInt16(message, offset + 8);
                offset += 10;

                if (offset + dataLength > message.Length) return Failure(roundTripMs, "truncated record data");

                if (type == TypeA && recordClass == ClassIn)
                {
                    if (dataLength != 4) return Failure(roundTripMs, "invalid A record length");
                    if (i < answerCount) addresses.Add(new IPAddress(message.AsSpan(offset, 4)).ToString());
                }

                offset += dataLength;
            }

            if (addresses.Count == 0) return Failure(roundTripMs, "no A records in answer");
            return new DnsQueryResponse(true, Math.Round(roundTripMs, 2), addresses, null);
        }
        catch (Exception ex)
        {
            return Failure(roundTripMs, "malformed response: " + ex.Message);
        }
    }

    private static DnsQueryResponse Failure(double roundTripMs, string error) =>
        new(false, Math.Round(roundTripMs, 2), [], error);

    /// <summary>Expands a wire name, preserving label boundaries and folding only ASCII case.</summary>
    private static byte[] ReadName(byte[] message, ref int offset)
    {
        var cursor = offset;
        var jumped = false;
        var pointerCount = 0;
        var wireLength = 1;
        var name = new List<byte>(32);
        while (true)
        {
            if (cursor >= message.Length) throw new FormatException("name runs past end of packet");
            var labelOffset = cursor;
            var length = message[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                name.Add(0);
                return [.. name];
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor >= message.Length) throw new FormatException("truncated compression pointer");
                var pointer = ((length & 0x3F) << 8) | message[cursor++];
                if (pointer < HeaderLength || pointer >= labelOffset || ++pointerCount > 128)
                    throw new FormatException("invalid compression pointer");
                if (!jumped) offset = cursor;
                jumped = true;
                cursor = pointer;
                continue;
            }

            if ((length & 0xC0) != 0) throw new FormatException("invalid label length");
            if (cursor + length > message.Length) throw new FormatException("truncated label");
            wireLength += length + 1;
            if (wireLength > 255) throw new FormatException("name is too long");
            name.Add(length);
            for (var i = 0; i < length; i++)
            {
                var value = message[cursor++];
                name.Add(value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value);
            }
        }
    }

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static void WriteUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value & 0xFF));
    }

    private static string DescribeResponseCode(int responseCode) => responseCode switch
    {
        1 => "FORMERR",
        2 => "SERVFAIL",
        3 => "NXDOMAIN",
        4 => "NOTIMP",
        5 => "REFUSED",
        _ => $"DNS error {responseCode}"
    };
}
