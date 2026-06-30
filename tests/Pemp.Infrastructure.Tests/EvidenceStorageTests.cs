using Pemp.Infrastructure.Evidence;
using Pemp.Infrastructure.Persistence;
using Xunit;

namespace Pemp.Infrastructure.Tests;

/// <summary>
/// The local evidence signed-token mechanic (rank 33 / SEC-EVD-02): tokens validate exactly once,
/// expire after their TTL, and reject tampering — so a leaked link is short-lived and single-use.
/// </summary>
public sealed class EvidenceStorageTests
{
    private static EvidenceRecord Ev() => new()
    {
        Id = Guid.NewGuid(), EngagementId = Guid.NewGuid(),
        FileName = "sqli-poc.png", Kind = EvidenceKind.Screenshot,
    };

    private static string TokenOf(string url) => url.Split('/')[^1];

    [Fact]
    public void Token_validates_once_then_is_single_use()
    {
        var now = DateTimeOffset.UnixEpoch;
        var storage = new LocalEvidenceStorage(() => now);
        var ev = Ev();

        var token = TokenOf(storage.CreateDownloadUrl(ev));

        var first = storage.Redeem(token);
        Assert.NotNull(first);
        Assert.Equal(ev.Id, first!.EvidenceId);
        Assert.Equal(ev.EngagementId, first.EngagementId);
        Assert.Equal(ev.FileName, first.FileName);

        Assert.Null(storage.Redeem(token));   // single-use: a replay is rejected
    }

    [Fact]
    public void Token_expires_after_its_ttl()
    {
        var now = DateTimeOffset.UnixEpoch;
        var storage = new LocalEvidenceStorage(() => now);
        var token = TokenOf(storage.CreateDownloadUrl(Ev()));

        now = now.AddMinutes(5);               // past the 2-minute TTL
        Assert.Null(storage.Redeem(token));
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var now = DateTimeOffset.UnixEpoch;
        var storage = new LocalEvidenceStorage(() => now);
        var token = TokenOf(storage.CreateDownloadUrl(Ev()));

        // Flip the last signature character — the HMAC no longer matches.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        Assert.Null(storage.Redeem(tampered));
        Assert.Null(storage.Redeem("not-a-token"));
    }

    [Fact]
    public void Materialize_streams_a_named_placeholder_without_the_real_bytes()
    {
        var storage = new LocalEvidenceStorage(() => DateTimeOffset.UnixEpoch);
        var ev = Ev();
        var ticket = storage.Redeem(TokenOf(storage.CreateDownloadUrl(ev)))!;

        var (bytes, contentType, name) = storage.Materialize(ticket);
        Assert.NotEmpty(bytes);
        Assert.Contains("text/plain", contentType);
        Assert.Contains(ev.FileName, name);
    }
}
