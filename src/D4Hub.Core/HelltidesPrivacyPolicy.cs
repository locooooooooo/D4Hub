namespace D4Hub.Core;

public static class HelltidesPrivacyPolicy
{
    private static readonly string[] BlockedHostSuffixes =
    [
        "adnxs.com",
        "amazon-adsystem.com",
        "criteo.com",
        "doubleclick.net",
        "google-analytics.com",
        "googlesyndication.com",
        "googletagmanager.com",
        "inmobi.com",
        "openx.net",
        "pubmatic.com",
        "quantcast.com",
        "rubiconproject.com"
    ];

    public const string DomSanitizerScript = """
        (() => {
          const selectors = [
            '#qc-cmp2-ui',
            '#qc-cmp2-container',
            '#qc-cmp2-persistent-link'
          ];
          const removePrivacyUi = () => {
            for (const selector of selectors) {
              for (const element of document.querySelectorAll(selector)) {
                element.remove();
              }
            }
          };
          const start = () => {
            removePrivacyUi();
            new MutationObserver(removePrivacyUi).observe(document.documentElement, {
              childList: true,
              subtree: true
            });
          };
          if (document.documentElement) {
            start();
          } else {
            document.addEventListener('DOMContentLoaded', start, { once: true });
          }
        })();
        """;

    public static bool ShouldBlockRequest(string? candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (BlockedHostSuffixes.Any(suffix =>
                string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var path = uri.AbsolutePath;
        return path.Contains("/feedbackfin@", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/prebid-load.js", StringComparison.OrdinalIgnoreCase);
    }
}
