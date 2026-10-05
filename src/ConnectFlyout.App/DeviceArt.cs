using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ConnectFlyout.App;

/// <summary>
/// Product picture for a headset model, shown in the flyout and on the Settings Devices page.
/// </summary>
/// <remarks>
/// Pictures live in Assets as "&lt;model name&gt;.png" (e.g. "WF-1000XM6.png"). Models without a
/// picture show none; drop a matching file in Assets to add one.
/// </remarks>
public static class DeviceArt
{
    // A file path, not ms-appx:, so it works for both the MSIX and the classic install
    public static ImageSource? For(string? modelName) => Has(modelName)
        ? new BitmapImage(new Uri(PathFor(modelName!)))
        : null;

    public static Visibility VisibleFor(string? modelName) => Has(modelName) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Visible when the model has no picture, for a fallback icon.
    /// </summary>
    public static Visibility HiddenFor(string? modelName) => Has(modelName) ? Visibility.Collapsed : Visibility.Visible;

    // An unknown model's name is its Bluetooth name, which anyone can set: refuse anything that
    // isn't a plain file name, so it can't reach a network share or another folder
    private static bool Has(string? modelName) =>
        !string.IsNullOrEmpty(modelName)
        && modelName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && File.Exists(PathFor(modelName));

    private static string PathFor(string modelName) => Path.Combine(AppContext.BaseDirectory, "Assets", modelName + ".png");
}
