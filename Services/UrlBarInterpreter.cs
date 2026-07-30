using System;

namespace StudentDesktop.Services;

// SDA-03: Chrome-style omnibox behavior for the browser's address bar. Pure and
// unit-testable on its own (no WebView/HTTP dependency) — BrowserTabViewModel.Navigate()
// calls this to decide what a raw typed string actually means before applying the
// whitelist/classifier gate, exactly the same way Chrome's own omnibox decides
// "URL vs. search term" before navigating.
public static class UrlBarInterpreter
{
    public enum IntentKind
    {
        // Parses outright as an absolute http(s) URI — navigate to it directly.
        Url,
        // Not an absolute URI, but shaped like a bare domain (e.g. "github.com") — try
        // it with an https:// prefix rather than treating it as a search term.
        BareDomain,
        // Anything else — route through a Google search, which itself still has to pass
        // the whitelist/classifier gate like any other navigation (not a bypass).
        Search,
    }

    public readonly record struct Intent(IntentKind Kind, Uri TargetUri);

    // A bare-domain heuristic, not a strict validator: contains a dot, no whitespace, and
    // doesn't look like a search phrase (no spaces). Deliberately permissive — a false
    // positive here just means "https://" is tried and the address bar/whitelist gate
    // rejects it same as any other bad input; a false negative just means one extra
    // Google search rather than a direct hit. Neither is a correctness problem.
    public static Intent Resolve(string input)
    {
        var trimmed = input.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return new Intent(IntentKind.Url, absolute);
        }

        if (LooksLikeBareDomain(trimmed))
        {
            return new Intent(IntentKind.BareDomain, new Uri("https://" + trimmed, UriKind.Absolute));
        }

        return new Intent(IntentKind.Search, BuildSearchUri(trimmed));
    }

    private static bool LooksLikeBareDomain(string input) =>
        input.Length > 0
        && !input.Contains(' ', StringComparison.Ordinal)
        && input.Contains('.', StringComparison.Ordinal)
        && !input.Contains('/', StringComparison.Ordinal)
        // Rules out "3.14" / "a..b" style non-domains without a real TLD-shaped suffix.
        && input.LastIndexOf('.') < input.Length - 1;

    private static Uri BuildSearchUri(string query) =>
        new("https://www.google.com/search?q=" + Uri.EscapeDataString(query), UriKind.Absolute);
}
