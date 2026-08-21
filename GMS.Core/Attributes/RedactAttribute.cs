namespace GMS.Core.Attributes;

/// <summary>
/// Marks a property to be replaced with a redacted placeholder when an object is
/// serialized into an audit event's BeforeJson/AfterJson snapshot.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RedactAttribute : Attribute
{
}
