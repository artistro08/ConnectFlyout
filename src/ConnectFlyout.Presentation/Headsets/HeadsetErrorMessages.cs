namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// Turns the HRESULTs the native layer reports (sony::protocol::toHresult) into messages for people.
/// </summary>
public static class HeadsetErrorMessages
{
    public const int TimeoutHResult = unchecked((int)0x800705B4);
    public const int DisconnectedHResult = unchecked((int)0x8007048F);
    public const int UnsupportedHResult = unchecked((int)0x80004001);
    public const int InvalidDataHResult = unchecked((int)0x8007000D);
    public const int TransportFailureHResult = unchecked((int)0x800704C9);

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.HResult switch
        {
            TimeoutHResult => "Your headphones didn't respond. Try again.",
            DisconnectedHResult => "Your headphones disconnected.",
            UnsupportedHResult => "These headphones don't support that setting.",
            InvalidDataHResult => "Your headphones sent a reply the app didn't understand.",
            TransportFailureHResult => "Couldn't connect. Make sure Bluetooth is on and your headphones are nearby.",
            _ => "Something went wrong talking to your headphones.",
        };
    }
}
