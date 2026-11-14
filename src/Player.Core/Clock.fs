namespace Player.Core

open System

/// The wall clock in one place. Services that need to be tested with another time take a `Func<long>` and default
/// to `Clock.UnixNow`; nobody else needs to write `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` again.
module Clock =
    [<CompiledName("UnixNow")>]
    let UnixNow () : int64 = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    