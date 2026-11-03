namespace RaptorSense;

/// <summary>Name, author and licence, shown in the app and the command line.</summary>
public static class AppInfo
{
    public const string Name = "RaptorSense";
    public const string Author = "aaronparayil";
    public const string AuthorUrl = "https://github.com/aaronparayil";
    public const string Licence = "GPL-3.0-or-later";

    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Credit => $"{Name} {Version} · created by {Author} ({AuthorUrl}) · free and open source under {Licence}";
}
