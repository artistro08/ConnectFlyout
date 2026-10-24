using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.UITests;

/// <summary>
/// Opens a settings page the way the settings window does: through a Frame, with the page's
/// navigation parameter, so OnNavigatedTo runs and Back has somewhere to go.
/// </summary>
internal static class Pages
{
    public static async Task<(Frame Frame, T Page)> OpenAsync<T>(object parameter) where T : Page
    {
        var frame = await UiHost.LoadAsync(new Frame());
        var page = await NavigateAsync<T>(frame, parameter);
        return (frame, page);
    }

    public static async Task<T> NavigateAsync<T>(Frame frame, object parameter) where T : Page
    {
        frame.Navigate(typeof(T), parameter);
        var page = (T)frame.Content;
        await LoadedAsync(page);
        return page;
    }

    public static async Task LoadedAsync(FrameworkElement element)
    {
        if (!element.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            element.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await UiHost.IdleAsync();
    }

    /// <summary>
    /// Expands an expander and waits for its content to load.
    /// </summary>
    public static async Task ExpandAsync(Expander expander)
    {
        expander.IsExpanded = true;
        await UiHost.IdleAsync();
        await UiHost.IdleAsync();
    }
}
