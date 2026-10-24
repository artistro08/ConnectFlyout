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

    internal void Update(NoiseMode mode, int ambientLevel, bool focusOnVoice) => IsActive = Scene.Matches(mode, ambientLevel, focusOnVoice);
}
