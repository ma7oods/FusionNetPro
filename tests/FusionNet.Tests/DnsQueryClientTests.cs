using System.Reflection;
using FusionNet.Windows.Network;

namespace FusionNet.Tests;

public sealed class DnsQueryClientTests
{
    private static readonly byte[] Question = [7, .. "example"u8.ToArray(), 3, .. "com"u8.ToArray(), 0, 0, 1, 0, 1];

    [Fact]
    public void ParseResponse_AcceptsCompressedAnswer()
    {
        var result = Parse(Response());
        Assert.True(result.Success, result.ErrorText);
        Assert.Equal(["93.184.216.34"], result.Addresses);
    }

    [Theory]
    [InlineData(0x83, 0x80)] // Truncated UDP answer.
    [InlineData(0x89, 0x80)] // Different opcode.
    [InlineData(0x01, 0x80)] // Query, not a response.
    public void ParseResponse_RejectsInvalidFlags(int high, int low)
    {
        var message = Response();
        message[2] = (byte)high;
        message[3] = (byte)low;
        AssertRejected(message);
    }

    [Fact]
    public void ParseResponse_RejectsTruncatedRecordAfterValidAnswer()
    {
        var message = Response();
        message[7] = 2;
        AssertRejected([.. message, 0xC0, 0x0C, 0, 1]);
        AssertRejected([.. message, 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 93]);
    }

    [Theory]
    [InlineData(0, 0xFF)] // Transaction id.
    [InlineData(13, 120)] // Different question name.
    [InlineData(26, 28)] // AAAA question instead of A.
    [InlineData(28, 3)] // Different question class.
    [InlineData(5, 0)] // Missing question.
    public void ParseResponse_RejectsUnrelatedResponses(int offset, int value)
    {
        var message = Response();
        message[offset] = (byte)value;
        AssertRejected(message);
    }

    [Theory]
    [InlineData(0xFF, 0xFF)] // Out of range.
    [InlineData(0xC0, 0x1D)] // Points into itself.
    public void ParseResponse_RejectsInvalidNamePointers(int high, int low)
    {
        var message = Response();
        var answerOffset = 12 + Question.Length;
        message[answerOffset] = (byte)high;
        message[answerOffset + 1] = (byte)low;
        AssertRejected(message);
    }

    [Fact]
    public void ParseResponse_QuestionMatchingIgnoresAsciiCase()
    {
        var message = Response();
        message[13] = (byte)'E';
        Assert.True(Parse(message).Success);
    }

    [Fact]
    public void ParseResponse_RejectsDifferentLabelBoundaries()
    {
        var message = Response();
        byte[] differentQuestion = [11, .. "example.com"u8.ToArray(), 0, 0, 1, 0, 1];
        AssertRejected([.. message[..12], .. differentQuestion, .. message[(12 + Question.Length)..]]);
    }

    [Fact]
    public void ParseResponse_RejectsTruncatedAdditionalRecords()
    {
        var message = Response();
        message[11] = 1;
        AssertRejected(message);
    }

    [Fact]
    public void ParseResponse_ValidatesEveryPacketBoundary()
    {
        var message = Response();
        for (var length = 0; length < message.Length; length++)
            AssertRejected(message[..length]);
    }

    [Theory]
    [InlineData(1, "FORMERR")]
    [InlineData(2, "SERVFAIL")]
    [InlineData(3, "NXDOMAIN")]
    [InlineData(5, "REFUSED")]
    public void ParseResponse_PreservesDnsErrors(int code, string error)
    {
        var message = Response();
        message[3] = (byte)(0x80 | code);
        var result = Parse(message);
        Assert.False(result.Success);
        Assert.Equal(error, result.ErrorText);
    }

    [Fact]
    public void ParseResponse_AcceptsCnameAndIgnoresAdditionalAddresses()
    {
        var message = Response();
        message[7] = 2;
        message[11] = 1;
        byte[] cname = [0xC0, 0x0C, 0, 5, 0, 1, 0, 0, 0, 60, 0, 8, 5, .. "alias"u8.ToArray(), 0xC0, 0x0C];
        var answer = message[(12 + Question.Length)..];
        answer[1] = (byte)(12 + Question.Length + 12); // CNAME target's wire offset.
        byte[] additional = [0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 192, 0, 2, 1];

        var result = Parse([.. message[..(12 + Question.Length)], .. cname,
            .. answer, .. additional]);

        Assert.True(result.Success, result.ErrorText);
        Assert.Equal(["93.184.216.34"], result.Addresses);
    }

    private static byte[] Response() =>
    [
        0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0,
        .. Question,
        0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 93, 184, 216, 34
    ];

    private static DnsQueryResponse Parse(byte[] message)
    {
        // Invoke the actual parser; keep packet-level helpers out of the service API.
        var method = typeof(DnsQueryClient).GetMethod("ParseResponse", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] query = [0x12, 0x34, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, .. Question];
        return (DnsQueryResponse)method.Invoke(null, [message, query, 12.5])!;
    }

    private static void AssertRejected(byte[] message)
    {
        var result = Parse(message);
        Assert.False(result.Success);
        Assert.Empty(result.Addresses);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorText));
    }
}
