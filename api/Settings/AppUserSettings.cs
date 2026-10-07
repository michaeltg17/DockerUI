namespace Api.Settings
{
    /// <summary>User overrides for a single app.</summary>
#pragma warning disable CA1056 // URL-like members stay strings so one bad value degrades gracefully instead of invalidating the whole settings
    internal sealed record AppUserSettings
    {
        /// <summary>Display name shown on the card (wins over the compose project name).</summary>
        public string? Name { get; set; }

        /// <summary>Full URL the app should open with (wins over port-based resolution).</summary>
        public string? Url { get; set; }

        /// <summary>Icon path or URL (wins over the 'dockerui.icon' label and the built-in catalog).</summary>
        public string? Icon { get; set; }

        /// <summary>Hide the app from the dashboard.</summary>
        public bool Hidden { get; set; }
    }
#pragma warning restore CA1056
}
