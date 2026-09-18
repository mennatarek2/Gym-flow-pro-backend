namespace GMS.Application.DTOs.AccessCards;

/// <summary>Access card inventory row DTO.</summary>
public class AccessCardDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? MemberId { get; set; }
    public string? MemberNumber { get; set; }
    public string? MemberName { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? LostAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? BatchId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AccessCardInventoryDto
{
    public int Total { get; set; }
    public int Available { get; set; }
    public int Assigned { get; set; }
    public int Lost { get; set; }
    public int Damaged { get; set; }
    public int Blocked { get; set; }

    /// <summary>True when Available count is below the soft threshold (default 10).</summary>
    public bool LowStock { get; set; }
}

public class BulkCreateAccessCardsRequest
{
    /// <summary>Code prefix (default CARD). Codes become {Prefix}-{seq:D4}.</summary>
    public string Prefix { get; set; } = "CARD";

    /// <summary>Starting sequence number (inclusive). Default 1.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>How many cards to create (1–500).</summary>
    public int Quantity { get; set; }
}

public class BulkCreateAccessCardsResult
{
    public string BatchId { get; set; } = string.Empty;
    public int Created { get; set; }
    public List<string> Codes { get; set; } = new();
}

public class AssignAccessCardRequest
{
    public Guid MemberId { get; set; }

    /// <summary>Optional — assign by card id when known.</summary>
    public Guid? CardId { get; set; }

    /// <summary>Optional — assign by scanned code (Available card).</summary>
    public string? Code { get; set; }
}

public class MarkAccessCardRequest
{
    public string? Reason { get; set; }
}

public class ReplaceAccessCardRequest
{
    public Guid MemberId { get; set; }

    /// <summary>New Available card id, or omit and supply <see cref="NewCode"/>.</summary>
    public Guid? NewCardId { get; set; }
    public string? NewCode { get; set; }

    /// <summary>Lost (default) or Damaged for the old Assigned card.</summary>
    public string OldStatus { get; set; } = "Lost";
    public string? Reason { get; set; }
}

public class UnassignAccessCardRequest
{
    public string? Reason { get; set; }
}
