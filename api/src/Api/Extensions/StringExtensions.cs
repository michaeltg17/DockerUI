namespace Api.Extensions
{
    internal static class StringExtensions
    {
        //C# 14 requires extension blocks to live in a top-level static class; the
        //analyzer sees the block as a nested type, which is a false positive here.
#pragma warning disable CA1034
        extension(string)
        {
            public static string JoinNonEmpty(params string?[] values) =>
                string.Join(" ", values.Where(v => !string.IsNullOrEmpty(v)));
        }
#pragma warning restore CA1034
    }
}
