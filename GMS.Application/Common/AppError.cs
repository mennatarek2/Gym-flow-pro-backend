namespace GMS.Application.Common;

/// <summary>
/// Additive localized error: stable machine code + EN/AR messages.
/// Legacy clients keep reading slash bilingual via <see cref="Slash"/> / Result.Error.
/// </summary>
public sealed class AppError
{
    public string Code { get; }
    public string Message { get; }
    public string MessageAr { get; }

    public AppError(string code, string message, string messageAr)
    {
        Code = code;
        Message = message;
        MessageAr = messageAr;
    }

    /// <summary>Flutter/web legacy: "English / العربية".</summary>
    public string Slash => string.IsNullOrWhiteSpace(MessageAr) || MessageAr == Message
        ? Message
        : $"{Message} / {MessageAr}";

    public object ToAnonymousBody() => new
    {
        code = Code,
        error = Slash,
        message = Message,
        messageAr = MessageAr
    };

    public static AppError FromCatalog(string code) =>
        AppMessageCatalog.Get(code);

    public static AppError Create(string code, string message, string messageAr) =>
        new(code, message, messageAr);
}
