namespace GMS.Application.DTOs.Invitation;

/// <summary>
/// Member App: Invite a Friend. Name and phone required. National ID and notes optional.
/// <c>guestName</c>/<c>guestPhoneNumber</c> accepted as aliases. <c>visitDate</c> is ignored.
/// </summary>
public class SendInvitationRequest
{
    public string? Name { get; set; }
    public string? GuestName { get; set; }

    public string? PhoneNumber { get; set; }
    public string? GuestPhoneNumber { get; set; }

    /// <summary>Optional. 14-digit Egyptian National ID when the member knows it.</summary>
    public string? NationalId { get; set; }

    public string? Notes { get; set; }

    /// <summary>Ignored. Kept so old clients that still send visitDate do not 400 on model binding.</summary>
    public DateOnly? VisitDate { get; set; }

    public string ResolvedName =>
        FirstNonEmpty(Name, GuestName);

    public string ResolvedPhone =>
        FirstNonEmpty(PhoneNumber, GuestPhoneNumber);

    private static string FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a)) return a.Trim();
        if (!string.IsNullOrWhiteSpace(b)) return b.Trim();
        return string.Empty;
    }
}
