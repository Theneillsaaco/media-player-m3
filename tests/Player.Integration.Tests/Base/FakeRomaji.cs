using Player.Core.Contracts;
using Player.Core.Domain;

namespace Player.Integration.Tests.Base;

internal sealed class FakeRomaji(string body) : IRomajiSource
{
    public int Calls;

    public Task<string> FindRomaji(Track track)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(body);
    }
}
