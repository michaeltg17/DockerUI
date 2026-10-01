using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Features.Health.Models.Responses;

public sealed record HealthResponse(HealthStatus Status);
