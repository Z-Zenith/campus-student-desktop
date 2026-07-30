using StudentDesktop.Services;

namespace StudentDesktop.Tests;

public class UrlBarInterpreterTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?query=1")]
    public void Resolve_AbsoluteHttpUrl_IsUrlIntent(string input)
    {
        var intent = UrlBarInterpreter.Resolve(input);

        Assert.Equal(UrlBarInterpreter.IntentKind.Url, intent.Kind);
        Assert.Equal(new Uri(input), intent.TargetUri);
    }

    [Theory]
    [InlineData("github.com")]
    [InlineData("docs.google.com")]
    public void Resolve_BareDomain_PrefixesHttps(string input)
    {
        var intent = UrlBarInterpreter.Resolve(input);

        Assert.Equal(UrlBarInterpreter.IntentKind.BareDomain, intent.Kind);
        Assert.Equal(new Uri("https://" + input), intent.TargetUri);
    }

    [Theory]
    [InlineData("how do I center a div")]
    [InlineData("ftp://example.com")] // non-http scheme, not bare-domain-shaped (contains "/")
    [InlineData("weather")]
    public void Resolve_AnythingElse_BecomesAGoogleSearch(string input)
    {
        var intent = UrlBarInterpreter.Resolve(input);

        Assert.Equal(UrlBarInterpreter.IntentKind.Search, intent.Kind);
        Assert.Equal("www.google.com", intent.TargetUri.Host);
        Assert.Equal("/search", intent.TargetUri.AbsolutePath);
    }

    [Fact]
    public void Resolve_SearchQuery_IsUrlEscaped()
    {
        var intent = UrlBarInterpreter.Resolve("c++ vs c#");

        Assert.Equal(UrlBarInterpreter.IntentKind.Search, intent.Kind);
        Assert.DoesNotContain("++", intent.TargetUri.Query);
        Assert.Contains("q=", intent.TargetUri.Query);
    }

    [Fact]
    public void Resolve_TrimsWhitespaceBeforeClassifying()
    {
        var intent = UrlBarInterpreter.Resolve("  github.com  ");

        Assert.Equal(UrlBarInterpreter.IntentKind.BareDomain, intent.Kind);
        Assert.Equal(new Uri("https://github.com"), intent.TargetUri);
    }
}
