using System.Text.RegularExpressions;

namespace Api.Features.Lan
{
    /// <summary>The enrichment fetched for a service: its page title and a favicon as a data URI.</summary>
    internal sealed record Enrichment(string? IconDataUri, string? Title);

    /// <summary>
    /// Best-effort enrichment of a discovered service: fetches the page to read its
    /// <c>&lt;title&gt;</c> and resolves its favicon to a data URI, so the card shows a real
    /// name and icon instead of the bare address.
    /// </summary>
    internal sealed class LanEnricher(HttpClient http)
    {
        const int MaxIconBytes = 512 * 1024;
        const int MaxTitleLength = 120;

        static readonly Regex LinkTagRegex = new("<link[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RelAttributeRegex = new("rel\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex HrefAttributeRegex = new("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex TitleRegex = new("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        public async Task<Enrichment> FetchAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                var html = await http.GetStringAsync(url, cancellationToken);
                var title = ExtractTitle(html);
                var icon = await ResolveIconAsync(html, url, cancellationToken);

                return new Enrichment(icon, title);
            }
#pragma warning disable CA1031 // Enrichment is best-effort; a down, slow, or non-HTML service must not fail the scan.
            catch
            {
                return new Enrichment(null, null);
            }
#pragma warning restore CA1031
        }

        static string? ExtractTitle(string html)
        {
            var match = TitleRegex.Match(html);
            var raw = match.Success ? match.Groups[1].Value : null;

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var title = string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();

            return title.Length is > 0 and <= MaxTitleLength ? title : null;
        }

        async Task<string?> ResolveIconAsync(string html, Uri pageUrl, CancellationToken cancellationToken)
        {
            foreach (Match link in LinkTagRegex.Matches(html))
            {
                var tag = link.Value;

                if (RelAttributeRegex.Match(tag) is not { Success: true } rel)
                    continue;

                var relValue = rel.Groups[1].Value.Trim().ToUpperInvariant();

                if (relValue is not ("ICON" or "APPLE-TOUCH-ICON" or "SHORTCUT ICON" or "MASK-ICON"))
                    continue;

                if (HrefAttributeRegex.Match(tag) is not { Success: true } href)
                    continue;

                var iconUrl = new Uri(pageUrl, href.Groups[1].Value);

                if (await TryFetchIconAsync(iconUrl, cancellationToken) is { } dataUri)
                    return dataUri;
            }

            // Fall back to the conventional location.
            return await TryFetchIconAsync(new Uri(pageUrl, "favicon.ico"), cancellationToken);
        }

        async Task<string?> TryFetchIconAsync(Uri iconUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(iconUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (bytes.Length is 0 or > MaxIconBytes)
                    return null;

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/x-icon";

                return $"data:{NormalizeMediaType(mediaType)};base64,{Convert.ToBase64String(bytes)}";
            }
#pragma warning disable CA1031 // Fetching the favicon is best-effort; any failure means no icon.
            catch
            {
                return null;
            }
#pragma warning restore CA1031
        }

        static string NormalizeMediaType(string mediaType) => mediaType.ToUpperInvariant() switch
        {
            "IMAGE/SVG+XML" => "image/svg+xml",
            "IMAGE/PNG" => "image/png",
            "IMAGE/JPEG" or "IMAGE/JPG" => "image/jpeg",
            "IMAGE/GIF" => "image/gif",
            "IMAGE/WEBP" => "image/webp",
            _ => "image/x-icon",
        };
    }
}
