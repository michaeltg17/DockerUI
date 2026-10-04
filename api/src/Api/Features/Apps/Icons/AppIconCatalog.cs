using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Api.Settings;

namespace Api.Features.Apps.Icons
{
    public interface IAppIconCatalog
    {
        /// <summary>Tries to resolve an icon path for the given container image reference.</summary>
        bool TryGetIcon(string? image, [NotNullWhen(true)] out string? icon);
    }

    /// <summary>
    /// Resolves container images to app icons. Lookup first tries the full normalized
    /// image name, then the last path segment (so 'postgres' also matches
    /// 'docker.io/library/postgres:16'). Later mappings override earlier ones.
    /// </summary>
    public sealed class AppIconCatalog : IAppIconCatalog
    {
        readonly FrozenDictionary<string, string> _byImage;
        readonly FrozenDictionary<string, string> _byImageName;

        public AppIconCatalog(IReadOnlyCollection<AppIconMapping> mappings)
        {
            ArgumentNullException.ThrowIfNull(mappings);

            BuildMaps(mappings, out _byImage, out _byImageName);
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

        static string LastSegment(string reference)
        {
            var slash = reference.LastIndexOf('/');
            return slash < 0 ? reference : reference[(slash + 1)..];
        }

        static void BuildMaps(
            IEnumerable<AppIconMapping> mappings,
            out FrozenDictionary<string, string> byImage,
            out FrozenDictionary<string, string> byImageName)
        {
            var images = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);

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
            }

            byImage = FrozenDictionary.ToFrozenDictionary(images);
            byImageName = FrozenDictionary.ToFrozenDictionary(names);
        }
    }
}
