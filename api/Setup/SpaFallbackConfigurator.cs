using Microsoft.Extensions.Options;
using System.Net;
using Api.Settings;

namespace Api.Setup
{
    /// <summary>
    /// Serves the built React app for any route no endpoint matched, injecting the
    /// configured dashboard name into the document title so a page refresh never
    /// flashes the static default title before the SPA has loaded its settings.
    /// </summary>
    internal static class SpaFallbackConfigurator
    {
        const string IndexFileName = "index.html";
        const string TitleOpenTag = "<title>";
        const string TitleCloseTag = "</title>";
        const string HtmlContentType = "text/html; charset=utf-8";
        const string NoCache = "no-cache";

        public static WebApplication UseSpaFallback(this WebApplication app)
        {
            app.MapFallback(async httpContext =>
            {
                var webRootPath = app.Environment.WebRootPath;
                var indexPath = webRootPath is null ? null : Path.Combine(webRootPath, IndexFileName);

                if (indexPath is null || !File.Exists(indexPath))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var settings = httpContext.RequestServices
                    .GetRequiredService<IOptionsMonitor<DockerUISettings>>()
                    .CurrentValue;
                var html = await File.ReadAllTextAsync(indexPath, httpContext.RequestAborted);

                httpContext.Response.ContentType = HtmlContentType;

                //The document embeds the dashboard name, so a cached copy must never
                //be served after the name changes.
                httpContext.Response.Headers.CacheControl = NoCache;
                await httpContext.Response.WriteAsync(WithDashboardTitle(html, settings), httpContext.RequestAborted);
            });

            return app;
        }

        static string WithDashboardTitle(string html, DockerUISettings settings)
        {
            var openTag = html.IndexOf(TitleOpenTag, StringComparison.Ordinal);
            var closeTag = openTag < 0 ? -1 : html.IndexOf(TitleCloseTag, openTag, StringComparison.Ordinal);

            if (openTag < 0 || closeTag < 0)
                return html;

            var name = string.IsNullOrWhiteSpace(settings.Name)
                ? DockerUISettings.DefaultName
                : settings.Name;

            return html[..openTag] + TitleOpenTag + WebUtility.HtmlEncode(name) + html[closeTag..];
        }
    }
}
