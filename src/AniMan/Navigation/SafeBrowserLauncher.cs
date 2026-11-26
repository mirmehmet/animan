using System.Diagnostics;
using AniMan.Core.Common;
using Serilog;

namespace AniMan.Navigation;

/// <summary>
/// Opens validated <c>http://</c> and <c>https://</c> URLs in the user's default browser,
/// blocking non-web schemes (<c>file://</c>, <c>javascript:</c>, UNC paths), and — unless the
/// link is one the user stored themselves — localhost and LAN addresses. OS shell association
/// failures are absorbed cleanly.
/// </summary>
public static class SafeBrowserLauncher
{
    /// <param name="allowLocalNetwork">
    /// <c>true</c> only for links the user entered themselves (streaming overrides from their
    /// own backup), which may legitimately point at a home media server.
    /// </param>
    public static bool TryOpen(string? url, bool allowLocalNetwork = false)
    {
        if (!HttpUrlGuard.IsLaunchableWebUrl(url, allowLocalNetwork, out var uri))
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

