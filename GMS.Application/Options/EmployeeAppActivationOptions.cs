namespace GMS.Application.Options;

/// <summary>Bound from configuration section <c>EmployeeAppActivation</c>.</summary>
public class EmployeeAppActivationOptions
{
    public const string SectionName = "EmployeeAppActivation";

    /// <summary>How long a newly generated code remains usable. Default: 24h.</summary>
    public int ExpirationHours { get; set; } = 24;

    /// <summary>
    /// Server pepper mixed into the hash. Prefer a dedicated secret; falls back to JWT secret at runtime if empty.
    /// </summary>
    public string CodePepper { get; set; } = string.Empty;
}
