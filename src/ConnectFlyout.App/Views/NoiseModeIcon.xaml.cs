using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Icon for a noise control mode, drawn after Sony's own: a person in a solid ring for noise
/// cancelling, a person in a dotted ring for ambient sound, a small circle in a ring for off.
/// </summary>
/// <remarks>
/// Fluent line style (1.5 px strokes on a 24 px grid), built as filled outlines so it can be a
/// <see cref="PathIcon"/> and color itself like the rest of the text and icons.
/// </remarks>
public sealed partial class NoiseModeIcon : UserControl
{
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(NoiseMode), typeof(NoiseModeIcon), new PropertyMetadata(NoiseMode.Off, (icon, _) => ((NoiseModeIcon)icon).Update()));

    // Outer ring, solid: 10 px outside radius, 1.5 px thick
    private static readonly string SolidRing = Ring(12, 12, 10, 8.5);

    // Person: head ring and shoulders
    private const string Person =
        "M 12,5.75 A 3.5,3.5 0 1 1 11.99,5.75 Z M 12,7.25 A 2,2 0 1 0 12.01,7.25 Z " +
        "M 7,17.25 C 7,14.1 9.2,12.5 12,12.5 C 14.8,12.5 17,14.1 17,17.25 L 15.5,17.25 C 15.5,15.1 14,14 12,14 C 10,14 8.5,15.1 8.5,17.25 Z";

    // Off: small ring in the middle
    private static readonly string InnerRing = Ring(12, 12, 4.75, 3.25);

    public NoiseModeIcon()
    {
        InitializeComponent();
        Update();
    }

    public NoiseMode Mode
    {
        get => (NoiseMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void Update()
    {
        var data = Mode switch
        {
            NoiseMode.Off => SolidRing + " " + InnerRing,
            NoiseMode.Ambient => DottedRing() + " " + Person,
            _ => SolidRing + " " + Person,
        };

        // F0 = even-odd fill, so each inner circle cuts its ring out of the outer one
        Icon.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), "F0 " + data);
    }

    // A ring as two circles: the even-odd fill leaves the band between them
    private static string Ring(double x, double y, double outer, double inner) => string.Create(
        CultureInfo.InvariantCulture,
        $"M {x},{y - outer} A {outer},{outer} 0 1 1 {x - 0.01},{y - outer} Z M {x},{y - inner} A {inner},{inner} 0 1 0 {x + 0.01},{y - inner} Z");

    // Dotted outer ring: 18 dots, each 1.5 px across
    private static string DottedRing()
    {
        const int dots = 18;
        var path = new StringBuilder();
        for (var i = 0; i < dots; i++)
        {
            var angle = 2 * Math.PI * i / dots;
            var x = 12 + (9.25 * Math.Sin(angle));
            var y = 12 - (9.25 * Math.Cos(angle));
            path.Append(CultureInfo.InvariantCulture, $"M {x - 0.75:0.###},{y:0.###} A 0.75,0.75 0 1 1 {x + 0.75:0.###},{y:0.###} A 0.75,0.75 0 1 1 {x - 0.75:0.###},{y:0.###} Z ");
        }
        return path.ToString();
    }
}
