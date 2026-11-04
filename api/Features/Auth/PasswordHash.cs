using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace Api.Features.Auth
{
    /// <summary>
    /// Creates, verifies, and parses the admin password hash stored in the
    /// 'DockerUI:Auth:AdminPasswordHash' setting. The hash is self-describing: the algorithm
    /// name, the iteration count, the base64 salt, and the base64 derived hash, joined with
    /// the '$' character.
    /// </summary>
    internal static class PasswordHash
    {
        public const string Algorithm = "PBKDF2-SHA256";

        /// <summary>Iteration count aligned with the current OWASP guidance for PBKDF2-HMAC-SHA256.</summary>
        public const int Iterations = 600_000;

        const char Separator = '$';
        const int SaltSizeBytes = 16;
        const int HashSizeBytes = 32;
        const int MinimumHashSizeBytes = 16;

        public static string Create(string password)
        {
            ArgumentNullException.ThrowIfNull(password);
            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var hash = Derive(password, Iterations, salt);

            return $"{Algorithm}{Separator}{Iterations}{Separator}{Convert.ToBase64String(salt)}{Separator}{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string? password, string? encodedHash)
        {
            if (string.IsNullOrWhiteSpace(password) ||
                !TryParse(encodedHash, out var iterations, out var salt, out var expectedHash))
                return false;

            var actualHash = Derive(password, iterations.Value, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        public static bool TryParse(
            string? encodedHash,
            [NotNullWhen(true)] out int? iterations,
            [NotNullWhen(true)] out byte[]? salt,
            [NotNullWhen(true)] out byte[]? hash)
        {
            iterations = null;
            salt = null;
            hash = null;

            if (string.IsNullOrWhiteSpace(encodedHash))
                return false;

            var parts = encodedHash.Split(Separator);

            if (parts.Length != 4 ||
                !string.Equals(parts[0], Algorithm, StringComparison.Ordinal) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIterations) ||
                parsedIterations < 1 ||
                !TryDecodeBase64(parts[2], out var parsedSalt) ||
                !TryDecodeBase64(parts[3], out var parsedHash) ||
                parsedHash.Length < MinimumHashSizeBytes)
            {
                return false;
            }

            iterations = parsedIterations;
            salt = parsedSalt;
            hash = parsedHash;
            return true;
        }

        static byte[] Derive(string password, int iterations, byte[] salt)
            => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        static bool TryDecodeBase64(string value, [NotNullWhen(true)] out byte[]? bytes)
        {
            try
            {
                bytes = Convert.FromBase64String(value);
                return true;
            }
            catch (FormatException)
            {
                bytes = null;
                return false;
            }
        }
    }
}
