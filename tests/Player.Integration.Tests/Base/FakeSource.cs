using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.Integration.Tests.Base;

internal sealed class FakeSource(string name, Func<Track, Task<LyricsResult>> find) : ILyricsSource
{
    public int Calls;

    public string Name => name;

    public Task<LyricsResult> Find(Track track)
    {
        Interlocked.Increment(ref Calls);
        return find(track);
    }

    public static FakeSource Returns(LyricsResult result, string name = "S") => new(name, _ => Task.FromResult(result));

    public static FakeSource Throws(string name = "S") => new(name, _ => throw new HttpRequestException("down"));
}
