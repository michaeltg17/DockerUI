namespace Api.Features.Apps.Endpoints
{
    public static class AppNameValidator
    {
        public static void Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
                throw new BadHttpRequestException($"Invalid app name '{name}'.");
        }
    }
}
