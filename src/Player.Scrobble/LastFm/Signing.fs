namespace Player.Scrobble.LastFm

open System
open System.Security.Cryptography
open System.Text

module LastFmSigning =
    let private unsigned = set [ "format"; "callback" ]
    
    let sign (secret: string) (parameters: (string * string) seq) : string =
        let payload =
            parameters
            |> Seq.filter (fun ( name, _) -> not (unsigned.Contains name))
            |> Seq.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> Seq.map (fun (name, value) -> name + value)
            |> String.concat ""
            
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(payload + secret)))