using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Health.Models.Responses;

internal sealed record HealthResponse(HealthStatus Status);
