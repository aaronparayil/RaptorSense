namespace RaptorSense.Automation;

/// <summary>
/// Knows which of the apps named in the rules are open. Other programs are ignored and never recorded. An app counts as
/// open from when it is found running or comes to the front until it has closed, so alt-tabbing away doesn't end a rule.
/// </summary>
public sealed class AppTracker(Func<string, bool> isRunning)
{
    readonly HashSet<string> watched = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> open = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Open => open;

    /// <summary>Sets which apps to look for and checks whether they are running already. True if the open apps changed.</summary>
    public bool Watch(IEnumerable<string> apps)
    {
        watched.Clear();
        watched.UnionWith(apps);
        var running = watched.Where(isRunning).ToList();
        bool changed = !open.SetEquals(running);
        open.Clear();
        open.UnionWith(running);
        return changed;
    }

    /// <summary>A program came to the front. True if it is one the rules care about and it wasn't already open.</summary>
    public bool NoticeFront(string app) => watched.Contains(app) && open.Add(app);

    /// <summary>Drops apps that have closed. True if any did.</summary>
    public bool Sweep() => open.RemoveWhere(app => !isRunning(app)) > 0;
}
