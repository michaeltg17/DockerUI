namespace E2E.Environments;

/// <summary>The <see cref="ScenarioEnvironment"/> for the basic scenario (default settings).</summary>
public sealed class BasicEnvironment : ScenarioEnvironment
{
    public BasicEnvironment() : base(Scenarios.Basic)
    {
    }
}

/// <summary>The <see cref="ScenarioEnvironment"/> for the settings scenario (per-app settings).</summary>
public sealed class SettingsEnvironment : ScenarioEnvironment
{
    public SettingsEnvironment() : base(Scenarios.Settings)
    {
    }
}

/// <summary>The <see cref="ScenarioEnvironment"/> for the error scenario (unreachable daemon).</summary>
public sealed class ErrorEnvironment : ScenarioEnvironment
{
    public ErrorEnvironment() : base(Scenarios.Error)
    {
    }
}
