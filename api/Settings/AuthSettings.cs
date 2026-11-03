namespace Api.Settings
{
    /// <summary>
    /// Optional single-password authentication for the dashboard: the hash of the admin
    /// password and how long a signed-in session stays valid. When
    /// <see cref="AdminPasswordHash"/> is absent the dashboard is open (no password).
    /// </summary>
    internal sealed record AuthSettings
    {
        public const int DefaultSessionLifetimeDays = 7;

        /// <summary>
        /// The dashboard's admin password as a self-describing hash, e.g.
        /// 'PBKDF2-SHA256$600000$&lt;saltB64&gt;$&lt;hashB64&gt;'. Absent means no password is set.
        /// </summary>
        public string? AdminPasswordHash { get; set; }

        /// <summary>
        /// How many days a session issued at sign-in stays valid. Sliding: activity past
        /// half the lifetime extends the expiry by the same span.
        /// </summary>
        public int SessionLifetimeDays { get; set; } = DefaultSessionLifetimeDays;
    }
}
