using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Api.Settings;

namespace Api.Features.Apps.Icons
{
    internal interface IAppIconCatalog
    {
        /// <summary>Tries to resolve an icon path for the given container image reference.</summary>
        bool TryGetIcon(string? image, [NotNullWhen(true)] out string? icon);

        /// <summary>
        /// Tries to resolve an icon path from an app name: an exact match against the
        /// normalized icon names first, then a fuzzy match (boundary prefix/suffix,
        /// reordered words, or a small typo).
        /// </summary>
        bool TryGetIconForName(string? name, [NotNullWhen(true)] out string? icon);
    }

    /// <summary>
    /// Resolves container images to app icons. Image lookup first tries the full
    /// normalized image name, then the last path segment (so 'postgres' also matches
    /// 'docker.io/library/postgres:16'). Name lookup matches the app name against the
    /// normalized icon names. Later mappings override earlier ones.
    /// </summary>
    internal sealed partial class AppIconCatalog : IAppIconCatalog
    {
        const double BoundaryScore = 0.9;
        const double TokenScore = 0.85;
        const double WeakSuffixScore = 0.75;
        const double MinFuzzyScore = 0.8;

        readonly FrozenDictionary<string, string> _byImage;
        readonly FrozenDictionary<string, string> _byImageName;
        readonly FrozenDictionary<string, string> _byName;
        readonly KeyValuePair<string, string>[] _candidates;
        readonly ILogger _logger;

        public AppIconCatalog(IReadOnlyCollection<AppIconMapping> mappings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(mappings);

            _logger = logger;
            BuildMaps(mappings, out _byImage, out _byImageName, out _byName, out _candidates);
        }

        public bool TryGetIcon(string? image, [NotNullWhen(true)] out string? icon)
        {
            icon = null;

            if (string.IsNullOrWhiteSpace(image))
                return false;

            var reference = NormalizeImage(image);

            if (_byImage.TryGetValue(reference, out icon))
                return true;

            var name = LastSegment(reference);
            return name.Length > 0 && _byImageName.TryGetValue(name, out icon);
        }

        public bool TryGetIconForName(string? name, [NotNullWhen(true)] out string? icon)
        {
            icon = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            var normalized = NormalizeName(name);

            if (normalized.Length == 0)
                return false;

            if (_byName.TryGetValue(normalized, out icon))
                return true;

            var best = string.Empty;
            var bestScore = 0.0;

            foreach (var candidate in _candidates)
            {
                var score = Score(normalized, candidate.Key);

                if (score > bestScore ||
                    (score == bestScore &&
                     (candidate.Key.Length > best.Length ||
                     (candidate.Key.Length == best.Length && string.CompareOrdinal(candidate.Key, best) < 0))))
                {
                    best = candidate.Key;
                    bestScore = score;
                }
            }

            if (bestScore >= MinFuzzyScore && _byName.TryGetValue(best, out icon))
            {
                LogFuzzyNameMatch(_logger, name, best);
                return true;
            }

            return false;
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "App name '{Name}' matched the '{Icon}' icon.")]
        static partial void LogFuzzyNameMatch(ILogger logger, string name, string icon);

        /// <summary>
        /// Normalizes a container image reference for mapping lookup: trims, uppercases,
        /// and strips the digest and tag (keeping the registry host and port).
        /// </summary>
        public static string NormalizeImage(string image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var reference = image.Trim().ToUpperInvariant();

            var digest = reference.IndexOf('@', StringComparison.Ordinal);
            if (digest >= 0)
                reference = reference[..digest];

            var tag = reference.LastIndexOf(':');
            if (tag >= 0 && !reference[(tag + 1)..].Contains('/', StringComparison.Ordinal))
                reference = reference[..tag];

            return reference;
        }

        /// <summary>
        /// Normalizes an app name for icon lookup: trims, uppercases, and replaces every
        /// run of non-alphanumeric characters with a single '-'.
        /// </summary>
        public static string NormalizeName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            var result = new StringBuilder(name.Trim().Length);

            foreach (var character in name.Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    result.Append(character);
                }
                else if (result.Length > 0 && result[^1] != '-')
                {
                    result.Append('-');
                }
            }

            return result.ToString().Trim('-');
        }

        /// <summary>
        /// Scores how well an app name matches a normalized icon name. Exact equality is
        /// handled by the caller; the highest boundary, token, and similarity scores win.
        /// </summary>
        static double Score(string name, string candidate)
        {
            var score = 0.0;

            if (candidate.StartsWith(name + '-', StringComparison.Ordinal))
                score = BoundaryScore;
            else if (name.StartsWith(candidate + '-', StringComparison.Ordinal))
                score = BoundaryScore;
            else if (name.EndsWith("-" + candidate, StringComparison.Ordinal) || candidate.EndsWith("-" + name, StringComparison.Ordinal))
                score = name.Contains('-', StringComparison.Ordinal) ? TokenScore : WeakSuffixScore;

            if (score < TokenScore && TokensAreSubset(candidate, name))
                score = TokenScore;

            return Math.Max(score, Similarity(name, candidate));
        }

        static bool TokensAreSubset(string candidate, string name)
        {
            var candidateTokens = candidate.Split('-');
            var nameTokens = name.Split('-');

            return candidateTokens.Length <= nameTokens.Length
                && candidateTokens.All(token => nameTokens.Contains(token, StringComparer.Ordinal));
        }

        static double Similarity(string a, string b) =>
            1 - (LevenshteinDistance(a, b) / (double)Math.Max(a.Length, b.Length));

        static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0)
                return b.Length;

            if (b.Length == 0)
                return a.Length;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (var j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;

                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        static string LastSegment(string reference)
        {
            var slash = reference.LastIndexOf('/');
            return slash < 0 ? reference : reference[(slash + 1)..];
        }

        static void BuildMaps(
            IEnumerable<AppIconMapping> mappings,
            out FrozenDictionary<string, string> byImage,
            out FrozenDictionary<string, string> byImageName,
            out FrozenDictionary<string, string> byName,
            out KeyValuePair<string, string>[] candidates)
        {
            var images = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var iconNames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var mapping in mappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Image) || string.IsNullOrWhiteSpace(mapping.Icon))
                    continue;

                var icon = $"/icons/{mapping.Icon}";
                var image = NormalizeImage(mapping.Image);

                if (image.Length == 0)
                    continue;

                images[image] = icon;

                var name = LastSegment(image);
                if (name.Length > 0)
                    names[name] = icon;

                var fileName = mapping.Icon;
                var dot = fileName.LastIndexOf('.');
                var key = NormalizeName(dot > 0 ? fileName[..dot] : fileName);
                if (key.Length > 0)
                    iconNames.TryAdd(key, icon);
            }

            byImage = FrozenDictionary.ToFrozenDictionary(images);
            byImageName = FrozenDictionary.ToFrozenDictionary(names);
            byName = FrozenDictionary.ToFrozenDictionary(iconNames);
            candidates = [.. iconNames];
        }
    }
}
