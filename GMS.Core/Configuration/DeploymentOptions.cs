namespace GMS.Core.Configuration;

/// <summary>
/// Binds the "Deployment" configuration section. Parsed explicitly (not via enum model-binding)
/// so an unrecognized or missing value fails safe to <see cref="DeploymentEdition.SaaS"/> instead
/// of throwing or defaulting to Local.
/// </summary>
public class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>Raw string from config, e.g. "SaaS" or "Local". Use <see cref="Edition"/> for the parsed, safe value.</summary>
    public string? Edition { get; set; }

    /// <summary>Parses <see cref="Edition"/>, defaulting to SaaS for null/empty/unrecognized values.</summary>
    public DeploymentEdition ResolveEdition() =>
        Enum.TryParse<DeploymentEdition>(Edition, ignoreCase: true, out var parsed)
            ? parsed
            : DeploymentEdition.SaaS;
}
