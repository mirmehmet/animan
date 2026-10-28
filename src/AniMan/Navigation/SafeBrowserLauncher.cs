using System.Diagnostics;
using AniMan.Core.Common;
using Serilog;

namespace AniMan.Navigation;

/// <summary>
/// Opens validated <c>http://</c> and <c>https://</c> URLs in the user's default browser,
/// blocking non-web schemes (<c>file://</c>, <c>javascript:</c>, UNC paths) and absorbing
/// OS shell association failures cleanly.
/// </summary>
public static class SafeBrowserLauncher
{
    public static bool TryOpen(string? url)
    {
        if (!HttpUrlGuard.IsAllowedWebUrl(url, out var uri))
        {
            Log.ForContext(typeof(SafeBrowserLauncher))
                .Warning("Blocked browser launch for disallowed URL: {Url}", url);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(SafeBrowserLauncher))
                .Warning(ex, "Failed to launch external browser for {Url}", uri.AbsoluteUri);
            return false;
        }
    }
}

