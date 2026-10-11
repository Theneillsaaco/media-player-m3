namespace Player.Core

open System
open System.IO
open System.Runtime.InteropServices
open System.Text

type LogLevel =
    | Debug = 0
    | Info = 1
    | Warn = 2
    | Error = 3
    
module internal LogFile =
    let maxBytes = 1_048_576L // 1 MB
    let keep = 3
    let gate = obj ()
    let mutable private ready = false
    let mutable private size = 0L
    
    let minLevel =
        match Environment.GetEnvironmentVariable "PLAYER_LOG" with
        | null -> LogLevel.Info
        | s when s.Equals("debug", StringComparison.OrdinalIgnoreCase) -> LogLevel.Debug
        | s when s.Equals("warn", StringComparison.OrdinalIgnoreCase) -> LogLevel.Warn
        | s when s.Equals("error", StringComparison.OrdinalIgnoreCase) -> LogLevel.Error
        | _ -> LogLevel.Info
        
    /// $XDG_STATE_HOME/player/player.log, falling back to ~/.local/state.
    let path =
        lazy
            (let state =
                match Environment.GetEnvironmentVariable "XDG_STATE_HOME" with
                | null
                | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".local", "state")
                | dir -> dir
                
            Path.Combine(state, "player", "player.log"))
            
    let private rotate (file: string) =
        for i in keep - 1 .. -1 .. 1 do
            let older = $"{file}.{i}"
            if File.Exists older then File.Move(older, $"{file}.{i + 1}", true)
            
        if File.Exists file then File.Move(file, file + ".1", true)
        
    let private prepare (file: string) =
        match Path.GetDirectoryName file with
        | null
        | "" -> ()
        | dir ->
            Directory.CreateDirectory(
                dir,
                UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            ) |> ignore
            
        size <- if File.Exists file then FileInfo(file).Length else 0L
        ready <- true
        
    /// Appends one line. Logging must never throw, so every failure is swallowed.
    let append (line: string) =
        lock gate (fun () ->
            try
                let file = path.Value
                if not ready then prepare file
                let bytes = Encoding.UTF8.GetBytes(line + "\n")
                
                if size + int64 bytes.Length > maxBytes then
                    rotate file
                    size <- 0L
                
                let options =
                    FileStreamOptions(
                        Mode = FileMode.Append,
                        Access = FileAccess.Write,
                        Share = FileShare.Read,
                        UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                    )
                
                use stream = new FileStream(file, options)
                stream.Write(bytes, 0, bytes.Length)
                size <- size + int64 bytes.Length
                
            with _ -> ())
      
/// Application log: stderr plus a rotating file. Never pass cookies, tokens or session keys.
/// Set PLAYER_LOG=debug|warn|error to change the level (default info).
[<AbstractClass; Sealed>]
type Log() =
    static member private Write(level: LogLevel, source: string, message: string, ex: exn) =
        if level >= LogFile.minLevel then
            let tag =
                match level with
                | LogLevel.Debug -> "DEBUG"
                | LogLevel.Info -> "INFO "
                | LogLevel.Warn -> "WARN "
                | _ -> "ERROR"
                
            let detail =
                if isNull ex then ""
                elif level = LogLevel.Debug then ": " + ex.ToString()
                else ": " + ex.GetType().Name + ": " + ex.Message
                
            let stamp = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
            let line = $"{stamp} [{tag}] {source}: {message}{detail}"
            
            Console.Error.WriteLine line
            LogFile.append line
            
    static member Debug(source: string, message: string) =
        Log.Write(LogLevel.Debug, source, message, null)
        
    static member Info(source: string, message: string) =
        Log.Write(LogLevel.Info, source, message, null)
        
    static member Warn(source: string, message: string, [<Optional; DefaultParameterValue(null)>] ex: exn) =
        Log.Write(LogLevel.Warn, source, message, ex)
        
    static member Error(source: string, message: string, [<Optional; DefaultParameterValue(null)>] ex: exn) =
        Log.Write(LogLevel.Error, source, message, ex)
