namespace GMS.Platform.Helpers;

/// <summary>
/// Shared validation for Ops+ cloud-link (Customer.TenantId → dbo.tenants).
/// Existence + not deleted is required; uniqueness is enforced separately in the service.
/// </summary>
public static class CloudTenantLinkRules
{
    public const string NotFoundMessage = "Cloud gym not found.";
    public const string AlreadyLinkedMessage = "That Cloud gym is already linked to another customer.";

    public static void EnsureCanLink(bool tenantExistsNotDeleted, bool alreadyLinkedToOtherCustomer)
    {
        if (!tenantExistsNotDeleted)
            throw new ArgumentException(NotFoundMessage);
        if (alreadyLinkedToOtherCustomer)
            throw new ArgumentException(AlreadyLinkedMessage);
    }
}
