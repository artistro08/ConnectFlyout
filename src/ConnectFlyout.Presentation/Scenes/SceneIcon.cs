namespace ConnectFlyout.Presentation.Scenes;

/// <summary>
/// An icon a scene can use: a Segoe Fluent Icons glyph and the name the picker searches by.
/// </summary>
public sealed record SceneIcon(string Glyph, string Name)
{
    /// <summary>
    /// Every icon the picker offers. The first three are the default scenes' icons.
    /// </summary>
    public static IReadOnlyList<SceneIcon> All { get; } =
    [
        new("\uE708", "Moon"),
        new("\uE821", "Briefcase"),
        new("\uE805", "Walk"),
        new("\uE80F", "Home"),
        new("\uE8D6", "Music"),
        new("\uE7F6", "Headphones"),
        new("\uE7FC", "Game"),
        new("\uE709", "Airplane"),
        new("\uE716", "People"),
        new("\uE77B", "Contact"),
        new("\uE714", "Video"),
        new("\uE722", "Camera"),
        new("\uE720", "Microphone"),
        new("\uE717", "Phone"),
        new("\uE8BD", "Message"),
        new("\uE715", "Mail"),
        new("\uE787", "Calendar"),
        new("\uE823", "Clock"),
        new("\uE916", "Stopwatch"),
        new("\uE734", "Star"),
        new("\uEB51", "Heart"),
        new("\uE713", "Settings"),
        new("\uE7E8", "Power"),
        new("\uE768", "Play"),
        new("\uE769", "Pause"),
        new("\uE767", "Volume"),
        new("\uE702", "Bluetooth"),
        new("\uE701", "Wi-Fi"),
        new("\uE774", "Globe"),
        new("\uE707", "Map pin"),
        new("\uE81D", "Location"),
        new("\uE719", "Shop"),
        new("\uE8B7", "Folder"),
        new("\uE8A5", "Document"),
        new("\uE7C3", "Page"),
        new("\uE7BC", "Reading list"),
        new("\uE8F1", "Library"),
        new("\uE945", "Lightbulb"),
        new("\uE946", "Info"),
        new("\uE7BA", "Warning"),
        new("\uE7C1", "Flag"),
        new("\uE718", "Pin"),
        new("\uE72E", "Lock"),
        new("\uE721", "Search"),
        new("\uE70F", "Edit"),
        new("\uE765", "Keyboard"),
        new("\uE772", "Devices"),
        new("\uE7F8", "Laptop"),
        new("\uE753", "Cloud"),
        new("\uE756", "Command prompt"),
        new("\uE90F", "Repair"),
        new("\uE706", "Brightness"),
        new("\uE790", "Color"),
        new("\uE771", "Personalize"),
        new("\uE770", "System"),
        new("\uE749", "Print"),
        new("\uE72D", "Share"),
        new("\uE71B", "Link"),
        new("\uE723", "Attach"),
        new("\uE724", "Send"),
        new("\uE72C", "Refresh"),
        new("\uE73E", "Check mark"),
        new("\uE7A7", "Undo"),
        new("\uE74E", "Save"),
    ];

    /// <summary>
    /// The icons whose name contains the query, in any case; all of them for an empty query.
    /// </summary>
    public static IReadOnlyList<SceneIcon> Search(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? All
            : [.. All.Where(icon => icon.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))];
}
