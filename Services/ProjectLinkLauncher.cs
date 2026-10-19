using System;
using System.Diagnostics;

namespace OmniLauncher.Services;

public static class ProjectLinkLauncher
{
    public static string ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Enter a complete http:// or https:// URL.");
        return uri.AbsoluteUri;
    }

    public static void Open(string url)
    {
        using var process = Process.Start(new ProcessStartInfo(ValidateUrl(url)) { UseShellExecute = true });
    }
}
