namespace StudentDesktop.ViewModels;

// SDA-03 classification policy engine (SDA/SEK plan, Work Item A2). Replaces the old
// synchronous Func<Uri, bool> IsWhitelisted delegate — a classify call can't be
// synchronous, and "blocked" vs. "couldn't verify" need distinct messaging even though
// both fail closed (block navigation) the same way.
public enum NavigationDecisionKind
{
    Allowed,
    Blocked,
    Error,
}

public readonly record struct NavigationDecision(NavigationDecisionKind Kind, string? Message)
{
    public static NavigationDecision Allowed() => new(NavigationDecisionKind.Allowed, null);
    public static NavigationDecision Blocked(string message) => new(NavigationDecisionKind.Blocked, message);
    public static NavigationDecision Error(string message) => new(NavigationDecisionKind.Error, message);
}
