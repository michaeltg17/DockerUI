using System.Text;
using Api.Features.Apps.Models;
using Api.Features.Apps.Runtime;

namespace Api.Features.Apps.Endpoints
{
    internal static class GetAppLogsEndpoint
    {
        public static void Map(IEndpointRouteBuilder group)
        {
            group.MapGet("/{name}/logs", async (
                string name,
                IAppRuntime runtime,
                AppService appService,
                CancellationToken cancellationToken) =>
            {
                AppNameValidator.Validate(name);
                var targets = await appService.ResolveAppUnitsAsync(name, cancellationToken).ConfigureAwait(false);
                var outputs = await runtime.ReadLogsAsync(targets, cancellationToken).ConfigureAwait(false);
                var logs = new StringBuilder();
                var hasLogs = false;

                foreach (var (unit, output) in outputs)
                {
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        continue;
                    }

                    hasLogs = true;

                    // The dialog title already names a single-unit app, so only
                    // multi-unit apps need a section header per unit.
                    if (outputs.Count > 1)
                    {
                        logs.AppendLine("=== " + unit.Name + " ===");
                    }

                    logs.Append(output).AppendLine();
                }

                // With no log content at all the dialog shows its empty state instead of
                // bare (multi-unit) section headers.
                return Results.Ok(new AppLogsDto(hasLogs ? logs.ToString().TrimEnd() : string.Empty));
            });
        }
    }
}
