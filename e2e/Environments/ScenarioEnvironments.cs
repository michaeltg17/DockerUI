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

/// <summary>The <see cref="ScenarioEnvironment"/> for the LAN discovery scenario (on-demand scan).</summary>
public sealed class LanEnvironment : ScenarioEnvironment
{
    public LanEnvironment() : base(Scenarios.Lan)
    {
    }
}

/// <summary>The <see cref="ScenarioEnvironment"/> for the read-only settings scenario (boot failure).</summary>
public sealed class ReadOnlySettingsEnvironment : ScenarioEnvironment
{
    public ReadOnlySettingsEnvironment() : base(Scenarios.ReadOnlySettings)
    {
    }
}
