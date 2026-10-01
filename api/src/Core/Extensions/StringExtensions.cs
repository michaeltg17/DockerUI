namespace Core.Extensions
{
    public static class StringExtensions
    {
        extension(string)
        {
            public static string JoinNonEmpty(params string?[] values) =>
                string.Join(" ", values.Where(v => !string.IsNullOrEmpty(v)));
        }
    }
}
