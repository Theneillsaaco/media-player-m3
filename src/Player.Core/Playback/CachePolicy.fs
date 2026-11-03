namespace Player.Core.Playback

open System.Collections.Generic

/// How many songs the disk cache keeps and which ones go when it is full.
module CachePolicy =
    /// The sizes offered in Settings. 0 turns the cache off.
    let limits = [| 0; 10; 30; 100 |]
    
    let defaultLimit = 30
    
    /// Anything unknown or negative gives the default; very large values are capped.
    let normalizeLimit (value: int) =
        if value < 0 then defaultLimit else min value 1000
        
    /// The songs to delete so that no more than `limit` stay: the least recently used first, and never one that
    /// is in `keep` (what plays now and what plays next). A cache full of kept songs is allowed to be over its limit.
    /// `files` are (id, last use in ticks).
    let evict (limit: int) (keep: string seq) (files: struct (string * int64) seq) : string[] =
        let protectedIds = HashSet<string>(keep)
        let all = Seq.toArray files
        let excess = all.Length - max 0 limit
        
        if excess <= 0 then
            [||]
        else
            all
            |> Array.filter (fun (struct (id, _)) -> not (protectedIds.Contains id))
            |> Array.sortBy (fun (struct (_, used)) -> used)
            |> Array.truncate excess
            |> Array.map (fun (struct (id, _)) -> id)
