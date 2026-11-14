using Player.Core.Contracts;

namespace Player.Integration.Tests.Base;

internal sealed class FakeTranslator(Func<string[], string[]> translate) : ILineTranslator
{
    public int Calls;

    public Task<string[]> Translate(string[] lines, string language)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(translate(lines));
    }
}