namespace Player.Core.Playback

open System
open Player.Core.Domain

/// Items are kept in insertion order. Order is the play order (a permutation of item
/// indices; the identity unless shuffled) and Position is an index into Order, -1 = none.
type QueueState = {
    Items: Track[]
    Order: int[]
    Position: int
    Repeat: RepeatMode
    Shuffle: bool
}
    
/// Pure functions over QueueState. Anything random takes a seed so tests are deterministic.
module PlayQueue =
    let empty = {
        Items = [||]
        Order = [||]
        Position = -1
        Repeat = Off
        Shuffle = false
    }
    
    let count (q: QueueState) = q.Items.Length
    
    /// Index into Items of the current track, or -1.
    let currentIndex (q: QueueState) =
        if q.Position < 0 || q.Position >= q.Items.Length then
            -1
        else q.Order[q.Position]
        
    let current (q: QueueState) =
        let i = currentIndex q
        if i < 0 then None else Some q.Items[i]
        
    /// Tracks in play order (what the queue panel shows).
    let ordered (q: QueueState) =
        q.Order |> Array.map (fun i -> q.Items[i])
        
    let private shuffleArray (seed: int) (a: int[]) =
        let rng = Random(seed)
        let b = Array.copy a
        
        for i in b.Length - 1 .. -1 .. 1 do
            let j = rng.Next(i + 1)
            let t = b[i]
            b[i] <- b[j]
            b[j] <- t
            
        b
       
    /// Play order and position for n items, with `current` (an item index or -1) kept playing. 
    let private orderFor shuffle seed n current =
        let all = Array.init n id
        
        if not shuffle then
            all, current
        else
            let rest = all |> Array.filter (fun i -> i <> current) |> shuffleArray seed
                    
            if current >= 0 then
                Array.append [| current |] rest, 0
            else rest, -1
            
    /// New queue from tracks, starting at item `start`. Keeps repeat and shuffle settings.
    let replace (seed: int) (tracks: Track seq) (start: int) (q: QueueState)  =
        let items = Seq.toArray tracks
        
        if items.Length = 0 then
            { empty with Repeat = q.Repeat; Shuffle = q.Shuffle }
        else
            let s = max 0 (min start (items.Length - 1))
            let order, pos = orderFor q.Shuffle seed items.Length s
            
            { q with Items = items; Order = order; Position = pos; }
      
    /// Appends at the end of the play order. The current track does not change.
    let add (track: Track seq) (q: QueueState) =
        let extra = Seq.toArray track
        
        if extra.Length = 0 then
            q
        else
            let n = q.Items.Length
            
            { q with
                Items = Array.append q.Items extra
                Order = Array.append q.Order (Array.init extra.Length (fun i -> n + i)) }
          
    /// Inserts right after the current track in the play order. The current track does not change.
    /// Without a current track it behaves like `add`.
    let addNext (track: Track seq) (q: QueueState) =
        let extra = Seq.toArray track
        
        if extra.Length = 0 then
            q
        else
            let at = min q.Order.Length (q.Position + 1)
            
            if q.Shuffle then
                let n = q.Items.Length
                let added = Array.init extra.Length (fun i -> n + i)
                
                { q with
                    Items = Array.append q.Items extra
                    Order = Array.concat [ Array.take at q.Order; added; Array.skip at q.Order ] }
            else
                // Not shuffled: play order is item order, so the items themselves move.
                let items = Array.concat [ Array.take at q.Items; extra; Array.skip at q.Items ]
                { q with Items = items; Order = Array.init items.Length id }
          
    let private moveInArray (source: int) (target: int) (a: 'T[]) =
        let list = ResizeArray<'T>(a)
        let item = list[source]
        list.RemoveAt source
        list.Insert(target, item)
        list.ToArray()
          
    /// Moves the entry at play-order position `source` so it ends up at `target`. The current track stays current.
    let move (source: int) (target: int) (q: QueueState) =
        let n = q.Order.Length
        
        if source < 0 || source >= n || target < 0 || target >= n || source = target then
            q
        else
            let position =
                if q.Position = source then target
                elif source < q.Position && target >= q.Position then q.Position - 1
                elif source > q.Position && target <= q.Position then q.Position + 1
                else q.Position
                
            if q.Shuffle then
                { q with Order = moveInArray source target q.Order; Position = position }
            else
                { q with Items = moveInArray source target q.Items; Position = position }

    
    /// Removes the entry at play-order position `position`. When it was the current one, the entry that
    /// followed it becomes current (the last one when nothing follows); callers decide what to play.
    let remove (position: int) (q: QueueState) =
        if position < 0 || position >= q.Order.Length then
            q
        else
            let item = q.Order[position]
            
            let items =
                q.Items |> Array.indexed |> Array.filter (fun (i, _) -> i <> item) |> Array.map snd
            
            let order =
                q.Order
                |> Array.indexed
                |> Array.filter (fun (p, _) -> p <> position)
                |> Array.map (fun (_, i) -> if i > item then i - 1 else i)
                
            let current =
                if items.Length = 0 then -1
                elif position < q.Position then q.Position - 1
                elif position = q.Position then min q.Position (items.Length - 1)
                else q.Position
                
            { q with Items = items; Order = order; Position = current }
            
    /// Drops every entry; repeat and shuffle stay.
    let clear (q: QueueState) = {
        empty with Repeat = q.Repeat; Shuffle = q.Shuffle
    }
        
    /// Jump to a position in the play order.
    let jumpTo (position: int) (q: QueueState) =
        if position >= 0  && position < q.Order.Length then
            { q with Position = position }
        else q
        
    let hasNext (q: QueueState) =
        q.Order.Length > 0 && (q.Position + 1 < q.Order.Length || q.Repeat = All)
        
    /// Manual "next". At the end with repeat off it stays where it is (check hasNext).
    let next (q: QueueState) =
        if not (hasNext q) then q
        elif q.Position + 1 < q.Order.Length then
            { q with Position = q.Position + 1 }
        else { q with Position = 0 }
        
    /// Manual "previous". At the start it stays (or wraps with repeat all).
    let previous (q: QueueState) =
        if q.Position > 0 then
            { q with Position = q.Position - 1 }
        elif q.Position = 0 && q.Repeat = All && q.Order.Length > 0 then
            { q with Position = q.Order.Length - 1 }
        else q
        
    /// What happens when a track ends by itself. None = the queue is finished.  
    let advance (q: QueueState) =
        if q.Order.Length = 0 then None
        elif q.Repeat = One && currentIndex q >= 0 then
            Some q
        elif q.Position + 1 < q.Order.Length then
            Some { q with Position = q.Position + 1 }
        elif q.Repeat = All then
            Some { q with Position = 0 }
        else None
        
    let setRepeat (repeat: RepeatMode) (q: QueueState) = { q with Repeat = repeat }
    
    let cycleRepeat (q: QueueState) =
        let m =
            match q.Repeat with
            | Off -> All
            | All -> One
            | One -> Off
            
        { q with Repeat = m }

    let repeatName (q: QueueState) =
        match q.Repeat with
        | Off -> "off"
        | One -> "one"
        | All -> "all"
        
    /// Turns shuffle on or off keeping the current track playing.
    let setShuffle (seed: int) (on: bool) (q: QueueState) =
        if q.Shuffle = on then
            q
        else
            let c = currentIndex q
            let order, pos = orderFor on seed q.Items.Length c
            
            { q with Shuffle = on; Order = order; Position = pos }