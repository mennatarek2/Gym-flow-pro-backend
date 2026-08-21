namespace GMS.Application.DTOs.Activities;

public class ActivityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string Kind { get; set; } = "";
    public string? SystemKey { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool BookingRequired { get; set; }
    public int? DefaultCapacity { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public decimal? DropInPrice { get; set; }
    public bool VisibleToMembers { get; set; }
}

public class CreateActivityRequest
{
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string Kind { get; set; } = "class";
    public int? DefaultCapacity { get; set; }
    public int? DefaultDurationMinutes { get; set; }
    public decimal? DropInPrice { get; set; }
    public bool BookingRequired { get; set; } = true;
    public bool VisibleToMembers { get; set; } = true;
}

public class UpdateActivityRequest : CreateActivityRequest
{
    public bool? IsActive { get; set; }
}
