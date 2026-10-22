namespace DeadLinks.Services;

public enum LinkKind
{
    /// <summary>Windows shortcut (.lnk).</summary>
    Shortcut,
    /// <summary>Internet shortcut (.url) that points to a local file (file:// address).</summary>
    InternetShortcut,
    /// <summary>Symbolic link or junction.</summary>
    SymbolicLink,
}

public enum LinkStatus
{
    /// <summary>The target exists.</summary>
    Ok,
    /// <summary>The target is gone although its drive or share is reachable: a dead link.</summary>
    TargetMissing,
    /// <summary>The drive or network share is not connected right now, so the link may be fine later.</summary>
    Unavailable,
    /// <summary>DeadLinks cannot tell (app or Store shortcut, web address, no permission): never offered for removal.</summary>
    Unchecked,
}

/// <param name="Path">Full path of the shortcut or link.</param>
/// <param name="Target">Where it points (empty when unknown).</param>
/// <param name="Note">Short explanation for the user.</param>
/// <param name="IsDirectory">For links: the link itself is a folder link.</param>
public sealed record LinkItem(string Path, LinkKind Kind, string Target, LinkStatus Status, string Note, bool IsDirectory = false)
{
    public string KindText => Kind switch
    {
        LinkKind.Shortcut => "Shortcut",
        LinkKind.InternetShortcut => "Internet shortcut",
        _ => "Symbolic link / junction",
    };

    public string StatusText => Status switch
    {
        LinkStatus.Ok => "Target exists",
        LinkStatus.TargetMissing => "Target missing",
        LinkStatus.Unavailable => "Drive offline",
        _ => "Not checked",
    };
}
