namespace Api.Features.Apps
{
    /// <summary>
    /// Remembers the base URL (scheme + host) of the most recent client request to the
    /// dashboard. HTTP endpoints refresh it; background broadcasts (which have no request
    /// context) reuse it, so app URLs always point at the host people are actually
    /// browsing the dashboard from.
    /// </summary>
    internal sealed class AppBaseUrlTracker
    {
        Uri? lastSeen;

        public void Set(Uri? baseUrl) => Interlocked.Exchange(ref lastSeen, baseUrl);

        public Uri? Current => Volatile.Read(ref lastSeen);
    }
}
