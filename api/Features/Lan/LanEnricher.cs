using System.Net;
using System.Text.RegularExpressions;

namespace Api.Features.Lan
{
    /// <summary>The enrichment fetched for a service: its best page name and a favicon as a data URI.</summary>
    internal sealed record Enrichment(string? IconDataUri, string? Title);

    /// <summary>
    /// Best-effort enrichment of a discovered service: fetches the page to read its name
    /// and fetches its favicon as a data URI, so the card shows a real name and icon
    /// instead of the bare address. The name comes from the first source the page offers:
    /// its &lt;title&gt;, then the social meta titles (og:title, twitter:title), the
    /// application-name meta, the first heading, and finally the site name — so services
    /// without a plain title still get a meaningful card instead of an ip-named one.
    /// </summary>
    internal sealed partial class LanEnricher(HttpClient http)
    {
        const int MaxIconBytes = 512 * 1024;
        const int MaxTitleLength = 120;

        [GeneratedRegex("<link[^>]*>")]
        private static partial Regex CreateLinkTagRegex();

        [GeneratedRegex("rel\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
        private static partial Regex CreateRelAttributeRegex();

        [GeneratedRegex("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
        private static partial Regex CreateHrefAttributeRegex();

        [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex CreateTitleRegex();

        [GeneratedRegex("<meta[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex CreateMetaTagRegex();

        [GeneratedRegex("(?:property|name)\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
        private static partial Regex CreateMetaNameRegex();

        [GeneratedRegex("content\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
        private static partial Regex CreateMetaContentRegex();

        [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex CreateH1Regex();

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex CreateTagsRegex();

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

        /// <summary>
        /// The page's best display name: the first source that yields something usable,
        /// in order of how well it names the service rather than a page of it.
        /// </summary>
        static string? ExtractTitle(string html)
            => CleanTitle(CreateTitleRegex().Match(html).Groups[1].Value)
            ?? MetaContent(html, "og:title")
            ?? MetaContent(html, "twitter:title")
            ?? MetaContent(html, "application-name")
            ?? CleanTitle(CreateTagsRegex().Replace(CreateH1Regex().Match(html).Groups[1].Value, string.Empty))
            ?? MetaContent(html, "og:site_name");

        static string? MetaContent(string html, string name)
        {
            foreach (Match tag in CreateMetaTagRegex().Matches(html))
            {
                var attribute = CreateMetaNameRegex().Match(tag.Value);

                if (!attribute.Success ||
                    !string.Equals(attribute.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CleanTitle(CreateMetaContentRegex().Match(tag.Value).Groups[1].Value) is { } content)
                    return content;
            }

            return null;
        }

        /// <summary>Decodes entities, collapses whitespace, and keeps only usable titles.</summary>
        static string? CleanTitle(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var title = string.Join(' ', WebUtility.HtmlDecode(raw).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

            return title.Length is > 0 and <= MaxTitleLength ? title : null;
        }

        async Task<string?> ResolveIconAsync(string html, Uri pageUrl, CancellationToken cancellationToken)
        {
            foreach (Match link in CreateLinkTagRegex().Matches(html))
            {
                var tag = link.Value;

                if (CreateRelAttributeRegex().Match(tag) is not { Success: true } rel)
                    continue;

                var relValue = rel.Groups[1].Value.Trim().ToUpperInvariant();

                if (relValue is not ("ICON" or "APPLE-TOUCH-ICON" or "SHORTCUT ICON" or "MASK-ICON"))
                    continue;

                if (CreateHrefAttributeRegex().Match(tag) is not { Success: true } href)
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
