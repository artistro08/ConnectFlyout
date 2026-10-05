using CommunityToolkit.Mvvm.ComponentModel;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Scenes;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// One scene button in the flyout, lit up while the headset matches it.
/// </summary>
public sealed class SceneItemViewModel : ObservableObject
{
    private bool _isActive;

    public SceneItemViewModel(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene = scene;
    }

    public Scene Scene { get; }

    public string Name => Scene.Name;

    public string Glyph => Scene.Glyph;

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>
    /// A scene matches when the mode is the same; Ambient scenes also need the same level
    /// and Focus on voice setting (both only apply in Ambient).
    /// </summary>
    internal void Update(NoiseMode mode, int ambientLevel, bool focusOnVoice)
    {
        var setting = Scene.Setting;
        IsActive = setting.Mode == mode
            && (mode != NoiseMode.Ambient || (setting.AmbientLevel == ambientLevel && setting.FocusOnVoice == focusOnVoice));
    }
}
