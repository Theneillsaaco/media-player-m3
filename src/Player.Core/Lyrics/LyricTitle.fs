namespace Player.Core.Lyrics

open System
open System.Text.RegularExpressions

/// A track title as lyrics sites know it.
module LyricTitle =
    let private credit =
        Regex(@"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

    /// The title without what YouTube Music adds to it: the translation or version after " - " and a
    /// "(feat. ...)" credit.
    let shorten (title: string) : string =
        let text = if isNull title then "" else title
        let index = text.IndexOf(" - ", StringComparison.Ordinal)
        let cut = if index < 0 then text else text.Substring(0, index)
        credit.Replace(cut, "").Trim()
