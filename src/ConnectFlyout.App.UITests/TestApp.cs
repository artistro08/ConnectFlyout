using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ConnectFlyout.App.UITests;

/// <summary>
/// The Application the tests run under: the app's own resource dictionaries, and the app's
/// XAML type metadata, so its pages and custom controls parse. Code only, so the XAML compiler
/// generates nothing for this project's own Application.
/// </summary>
internal sealed partial class TestApp : Application, IXamlMetadataProvider
{
    private readonly ConnectFlyout_App_XamlTypeInfo.XamlMetaDataProvider _app = new();

    public TestApp()
    {
        // A failure in here or in OnLaunched is a stowed exception that ends the process with no
        // message, so say what it was first
        UnhandledException += (_, e) => UiHost.Note("Unhandled XAML exception: " + e.Message + Environment.NewLine + e.Exception);
    }

    // The resources go in once the Application exists in full; in the constructor WinUI refuses them
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            foreach (var source in new[] { "Styles/AppStyles.xaml", "Styles/KeyCharPresenter.xaml", "Styles/KeyVisual.xaml" })
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///" + source) });
            }
            UiHost.OnLaunched();
        }
        catch (Exception exception)
        {
            UiHost.Note("OnLaunched failed: " + exception);
            throw;
        }
    }

    public IXamlType GetXamlType(Type type) => _app.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _app.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _app.GetXmlnsDefinitions();
}
