namespace GMS.Application.Interfaces;

/// <summary>
/// Service for logging application events.
/// </summary>
public interface ILoggerService
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(string message, Exception? exception = null, params object[] args);
}
