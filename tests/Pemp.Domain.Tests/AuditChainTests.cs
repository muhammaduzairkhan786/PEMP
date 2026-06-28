using Pemp.Domain.Audit;
using Xunit;

namespace Pemp.Domain.Tests;

public class AuditChainTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Eng = Guid.NewGuid();

    [Fact]
    public void Each_entry_links_to_the_previous_hash()
    {
        var chain = new InMemoryHashChain();
        var a = chain.Append(Eng, "u", "A", "-", "Intake", "api", At);
        var b = chain.Append(Eng, "u", "B", "Intake", "Assignment", "api", At);

        Assert.Equal(InMemoryHashChain.GenesisHash, a.PrevHash);
        Assert.Equal(a.Hash, b.PrevHash);
        Assert.True(chain.Verify());
    }

    [Fact]
    public void Verify_detects_tampering()
    {
        var chain = new InMemoryHashChain();
        chain.Append(Eng, "u", "A", "-", "Intake", "api", At);
        chain.Append(Eng, "u", "B", "Intake", "Assignment", "api", At);
        Assert.True(chain.Verify());

        // Tamper with a stored entry's content (reach the backing list via reflection,
        // since the API is append-only by design).
        var field = typeof(InMemoryHashChain).GetField("_entries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<AuditEntry>)field.GetValue(chain)!;
        list[0] = list[0] with { After = "Tampered" };

        Assert.False(chain.Verify());   // hash no longer matches content
    }

    [Fact]
    public void Hash_is_deterministic_for_same_input()
    {
        var c1 = new InMemoryHashChain();
        var c2 = new InMemoryHashChain();
        var e1 = c1.Append(Eng, "u", "A", "-", "Intake", "api", At);
        var e2 = c2.Append(Eng, "u", "A", "-", "Intake", "api", At);
        Assert.Equal(e1.Hash, e2.Hash);
    }
}
