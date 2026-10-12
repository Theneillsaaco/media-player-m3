namespace Player.Core.Playback

/// What the player does with the saved session when it starts.
type StartupMode =
    /// Queue and position.
    | Continue = 0
    /// Queue, but the track starts from 0
    | FromStart = 1
    /// Nothing; volume, repeat and shuffle still come back.
    | NoRestore = 2
    
type RestorePlan = {
    Queue: bool
    Seconds: float
}

module Resume =
    let settingKey = "startup.restore"
    
    /// Anything unknown or missing gives the default, FromStart.
    let parse (value: string) =
        match value with
        | "continue" -> StartupMode.Continue
        | "none" -> StartupMode.NoRestore
        | _ -> StartupMode.FromStart
        
    let toSetting (mode: StartupMode) =
        match mode with
        | StartupMode.Continue -> "continue"
        | StartupMode.NoRestore -> "none"
        | _ -> "fromStart"
        
    let plan (mode: StartupMode) (savedSeconds: float) =
        match mode with
        | StartupMode.Continue -> { Queue = true; Seconds = max 0.0 savedSeconds }
        | StartupMode.NoRestore -> { Queue = false; Seconds = 0.0 }
        | _ -> { Queue = true; Seconds = 0.0 }
