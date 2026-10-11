namespace Player.App.Services;

/// <summary>Names the states for QML (strings are what the bridge handles best).</summary>
internal static class AccountView
{
    public static string StateName(AccountStage stage) => stage switch
    {
        AccountStage.SignedOut => "signedOut",
        AccountStage.SignedIn => "signedIn",
        AccountStage.Expired => "expired",
        _ => "unavailable"
    };
    
    public static string StateName(ScrobbleStage stage) => stage switch
    {
        ScrobbleStage.SignedOut => "signedOut",
        ScrobbleStage.Authorizing => "authorizing",
        ScrobbleStage.SignedIn => "signedIn",
        ScrobbleStage.Reconnect => "reconnect",
        _ => "unavailable",
    };
}