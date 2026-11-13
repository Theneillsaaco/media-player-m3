using Player.Storage.Cache;

namespace Player.Integration.Tests;

public class AudioCacheTests : IDisposable
{
    private const string A = "aaaaaaaaaaa", B = "bbbbbbbbbbb", C = "ccccccccccc";
 
    private readonly string _dir = Directory.CreateTempSubdirectory("player-cache-").FullName;
 
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* temp folder */ }
    }
 
    /// <summary>Writes a song file whose last use is <paramref name="age"/> minutes ago.</summary>
    private string Song(string id, int age, int bytes = 10)
    {
        var path = Path.Combine(_dir, id + ".m4a");
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-age));
        return path;
    }
 
    [Fact]
    public void Complete_songs_of_an_earlier_run_are_indexed()
    {
        var path = Song(A, 5, bytes: 100);
 
        using var cache = new AudioCache(_dir, limit: 10);
 
        Assert.True(cache.IsCached(A));
        Assert.Equal(path, cache.Get(A));
        Assert.Equal((1, 100L), cache.Stats);
    }
 
    [Fact]
    public void Half_downloads_empty_files_and_strangers_are_deleted_at_start()
    {
        var part = Path.Combine(_dir, ".part-" + A + "-1234");
        var empty = Path.Combine(_dir, B + ".m4a");
        var stranger = Path.Combine(_dir, "notes.txt");
        File.WriteAllBytes(part, [1]);
        File.WriteAllBytes(empty, []);
        File.WriteAllBytes(stranger, [1]);
 
        using var cache = new AudioCache(_dir, limit: 10);
 
        Assert.False(File.Exists(part));
        Assert.False(File.Exists(empty));
        Assert.False(File.Exists(stranger));
        Assert.Equal(0, cache.Stats.Files);
    }
 
    [Fact]
    public void Starting_over_the_limit_drops_the_least_recently_used()
    {
        var oldest = Song(A, 30);
        Song(B, 20);
        Song(C, 10);
 
        using var cache = new AudioCache(_dir, limit: 2);
 
        Assert.False(cache.IsCached(A));
        Assert.False(File.Exists(oldest));
        Assert.True(cache.IsCached(B));
        Assert.True(cache.IsCached(C));
    }
 
    [Fact]
    public void A_cache_that_is_off_never_serves_anything()
    {
        Song(A, 1);
 
        using var cache = new AudioCache(_dir, limit: 0);
 
        Assert.Null(cache.Get(A));
        Assert.False(cache.IsCached(A));
    }
 
    [Fact]
    public void Get_returns_null_and_forgets_a_file_that_vanished()
    {
        var path = Song(A, 1);
        using var cache = new AudioCache(_dir, limit: 5);
        File.Delete(path);
 
        Assert.Null(cache.Get(A));
        Assert.False(cache.IsCached(A));
    }
 
    [Fact]
    public void Lowering_the_limit_evicts_but_never_what_is_protected()
    {
        var protectedPath = Song(A, 30);
        Song(B, 20);
        using var cache = new AudioCache(_dir, limit: 5);
        cache.Protect([A]);
 
        cache.SetLimit(1);
 
        Assert.True(cache.IsCached(A));
        Assert.True(File.Exists(protectedPath));
        Assert.False(cache.IsCached(B));
    }
 
    [Fact]
    public void A_limit_of_zero_empties_the_cache()
    {
        var path = Song(A, 1);
        using var cache = new AudioCache(_dir, limit: 5);
 
        cache.SetLimit(0);
 
        Assert.False(File.Exists(path));
        Assert.Equal(0, cache.Stats.Files);
    }
 
    [Fact]
    public void Clear_deletes_every_song_and_bumps_the_version()
    {
        Song(A, 1);
        Song(B, 2);
        using var cache = new AudioCache(_dir, limit: 5);
        var before = cache.Version;
 
        cache.Clear();
 
        Assert.Empty(Directory.EnumerateFiles(_dir));
        Assert.True(cache.Version > before);
    }
 
    [Fact]
    public void Remove_drops_one_song()
    {
        var path = Song(A, 1);
        Song(B, 2);
        using var cache = new AudioCache(_dir, limit: 5);
 
        cache.Remove(A);
 
        Assert.False(File.Exists(path));
        Assert.True(cache.IsCached(B));
    }
 
    [Fact]
    public void Add_ignores_bad_ids_and_empty_urls()
    {
        using var cache = new AudioCache(_dir, limit: 5);
 
        cache.Add("short", "http://example.invalid/x", []);
        cache.Add(A, "", []);
 
        Assert.False(cache.IsPending("short"));
        Assert.False(cache.IsPending(A));
    }
}