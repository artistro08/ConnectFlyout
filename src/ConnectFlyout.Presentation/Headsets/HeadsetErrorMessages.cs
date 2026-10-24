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
            TimeoutHResult => "The headphones did not respond. Try again.",
            DisconnectedHResult => "The headphones disconnected.",
            UnsupportedHResult => "The headphones do not support this setting.",
            InvalidDataHResult => "The headphones sent an unknown reply.",
            TransportFailureHResult => "Connection failed. Make sure that Bluetooth is on and the headphones are near the PC.",
            _ => "Communication with the headphones failed.",
        };
    }
}
