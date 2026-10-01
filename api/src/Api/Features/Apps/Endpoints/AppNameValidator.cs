namespace Api.Features.Apps.Endpoints
{
    public static class AppNameValidator
    {
        public static void Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains(".."))
                throw new BadHttpRequestException($"Invalid app name '{name}'.");
        }
    }
}
