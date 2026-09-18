namespace GMS.Core.Configuration;

/// <summary>
/// Which commercial edition this running instance is. SaaS is the default and must remain the
/// safe fallback if configuration is missing or malformed — never silently become Local.
/// </summary>
public enum DeploymentEdition
{
    SaaS = 0,
    Local = 1,
}
