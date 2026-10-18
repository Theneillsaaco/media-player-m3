namespace Player.Core.Localization

open System
open System.Collections.Generic
open System.Globalization
open System.Text.Json

/// The interface language: Spanish (the default) or English. The texts live in Translations. QML reads the
/// whole table of the current language as JSON; C# asks for single texts with `t` and `tf`.
module I18n =
    let settingKey = "ui.language"
    
    let languages = [| "es"; "en" |]
    
    /// "en" or "es"; anything else gives Spanish.
    let normalize (value: string) =
        match value with
        | "es" -> "es"
        | _ -> "en"
        
    let private tables =
        dict [ "es", dict Translations.spanish
               "en", dict Translations.english ]
        
    let mutable private language = "en"
    
    let current () = language
    
    let select (value: string) = language <- normalize value
    
    let private lookup (lang: string) (key: string) =
        match tables[lang].TryGetValue key with
        | true, text -> text
        | _ -> key
        
    /// The text of a key in the current language (the key itself when it does not exist).
    let t (key: string) = lookup language key
    
    /// Like `t`, filling {0}, {1}... with the arguments.
    let tf (key: string) (args: obj[]) =
        String.Format(CultureInfo.InvariantCulture, t key, args)

    /// Every text of a language as a JSON object, ready for JSON.parse in QML.
    let tableJson (lang: string) =
        JsonSerializer.Serialize(Dictionary<string, string>(tables[normalize lang]))
        
    /// Spanish writes decimals with a point ("0.5 s"), English with a comma.
    let decimalSeparator () = if language = "es" then '.' else ','

