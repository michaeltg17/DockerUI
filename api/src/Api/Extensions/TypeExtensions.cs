namespace Api.Extensions
{
    public static class TypeExtensions
    {
        public static string GetNameWithoutGenericArity(this Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            var index = type.Name.IndexOf('`', StringComparison.Ordinal);
            return index == -1 ? type.Name : type.Name[..index];
        }
    }
}
