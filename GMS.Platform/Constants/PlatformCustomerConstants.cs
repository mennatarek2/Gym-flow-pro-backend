namespace GMS.Platform.Constants;

/// <summary>
/// Commercial customer plane — separate from SaaS <see cref="SubscriptionConstants"/> and from
/// Local licensing crypto. A Customer is the gym/business; a LocalLicense is only a technical
/// entitlement that may point at a Customer/Contract.
/// </summary>
public static class PlatformCustomerStatuses
{
    public const string Prospect = "prospect";
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Churned = "churned";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Prospect, Active, Inactive, Churned
    };
}

public static class PlatformContactMethods
{
    public const string Phone = "phone";
    public const string WhatsApp = "whatsapp";
    public const string Email = "email";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Phone, WhatsApp, Email
    };
}

public static class PlatformContractStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Draft, Pending, Active, Completed, Expired, Cancelled
    };

    private static readonly Dictionary<string, HashSet<string>> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [Draft] = new(StringComparer.OrdinalIgnoreCase) { Pending, Cancelled },
        [Pending] = new(StringComparer.OrdinalIgnoreCase) { Active, Cancelled },
        [Active] = new(StringComparer.OrdinalIgnoreCase) { Completed, Expired, Cancelled },
        [Completed] = new(StringComparer.OrdinalIgnoreCase),
        [Expired] = new(StringComparer.OrdinalIgnoreCase),
        [Cancelled] = new(StringComparer.OrdinalIgnoreCase),
    };

    public static bool CanTransition(string from, string to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);
}

public static class PlatformContractPaymentStatuses
{
    public const string Unpaid = "unpaid";
    public const string Partial = "partial";
    public const string Paid = "paid";

    public static string FromAmounts(decimal total, decimal paid)
    {
        if (paid <= 0) return Unpaid;
        if (paid >= total) return Paid;
        return Partial;
    }
}

public static class PlatformCatalogProductTypes
{
    public const string Software = "software";
    public const string Hardware = "hardware";
    public const string Service = "service";
    public const string Consumable = "consumable";
    public const string Support = "support";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Software, Hardware, Service, Consumable, Support
    };
}

public static class PlatformPaymentMethods
{
    public const string Cash = "cash";
    public const string BankTransfer = "bank_transfer";
    public const string Other = "other";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Cash, BankTransfer, Other
    };
}

public static class PlatformSupportTicketStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string WaitingCustomer = "waiting_customer";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Open, InProgress, WaitingCustomer, Resolved, Closed
    };

    /// <summary>
    /// open → in_progress → waiting_customer ⇄ in_progress → resolved → closed.
    /// Closed is terminal. Same-status is a no-op at the service, not a graph edge.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [Open] = new(StringComparer.OrdinalIgnoreCase) { InProgress },
        [InProgress] = new(StringComparer.OrdinalIgnoreCase) { WaitingCustomer, Resolved },
        [WaitingCustomer] = new(StringComparer.OrdinalIgnoreCase) { InProgress },
        [Resolved] = new(StringComparer.OrdinalIgnoreCase) { Closed },
        [Closed] = new(StringComparer.OrdinalIgnoreCase),
    };

    public static bool CanTransition(string from, string to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);
}

public static class PlatformSupportTicketPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Critical = "critical";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Low, Normal, High, Critical
    };
}

public static class PlatformOwnerAccountStatuses
{
    public const string Unknown = "unknown";
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string ResetRequested = "reset_requested";
}

public static class LocalSalesContractLanguages
{
    public const string En = "en";
    public const string Ar = "ar";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase) { En, Ar };

    public static string Normalize(string? language) =>
        string.Equals(language?.Trim(), Ar, StringComparison.OrdinalIgnoreCase) ? Ar : En;
}

public static class LocalSalesContractDocumentStatuses
{
    public const string Issued = "issued";
}

public static class LocalSalesContractTermsDefaults
{
    public const string En =
        """
        This contract records the Customer's purchase of HyMotion Local from HyMotion. It is not a membership contract for gym members.
        Lifetime means the license has no expiry date in the product. It is a one-time Local purchase, not a monthly Cloud subscription.
        The license may be used on the number of computers shown on page 1. The usual limit is one computer.
        The license binds to the computer that activates it. The same key cannot be activated on a second gym or a second computer while the first computer is still active.
        Moving the license to a replacement computer (PC failure, lost or damaged machine) requires HyMotion Operations to authorize a transfer. The Customer cannot do this alone. HyMotion does not automatically issue a new key.
        Selling or giving the license to another gym is not supported. Activation on another gym's computer is rejected while this license is still active elsewhere.
        Owner password recovery on the gym PC is a separate HyMotion support process. It is not a new license and not a transfer.
        Support, training, hardware, and updates are included only if they appear as purchased items on page 1. The Lifetime license line by itself does not create a support SLA in the current product.
        The Customer is responsible for backing up gym data on the gym computer.
        Paid and outstanding amounts on this paper are the amounts recorded when the contract was issued. A later payment does not rewrite this issued copy.
        HyMotion records payments internally (cash, bank transfer, or other). That record is not a bank-verified receipt by itself.
        The current product has no commercial refund or credit-note function. Do not treat this paper as a refund promise.
        Cancelling the commercial sale in HyMotion does not by itself revoke the license, deactivate the installation, or reverse payments. License revoke is a separate HyMotion Admin action.
        This document is not a tax invoice and not a Cloud SaaS invoice.
        """;

    public const string Ar =
        """
        يسجّل هذا العقد شراء العميل لـ HyMotion Local من HyMotion. وهو ليس عقد عضوية لأعضاء النادي.
        مدى الحياة يعني أن الترخيص لا يملك تاريخ انتهاء في المنتج. الشراء مرة واحدة للمحلي، وليس اشتراكًا سحابيًا شهريًا.
        يُستخدم الترخيص على عدد الأجهزة الموضح في الصفحة 1. الحد المعتاد جهاز واحد.
        الترخيص يرتبط بالجهاز الذي فعّله. لا يمكن تفعيل نفس المفتاح على نادي آخر أو جهاز ثانٍ بينما الجهاز الأول ما زال نشطًا.
        نقل الترخيص إلى جهاز بديل (عطل أو فقد أو تلف) يتطلب تفويض عمليات HyMotion. العميل لا يفعل ذلك وحده. HyMotion لا تصدر مفتاحًا جديدًا تلقائيًا.
        بيع الترخيص أو إعطاؤه لنادٍ آخر غير مدعوم. التفعيل على جهاز نادٍ آخر يُرفض طالما هذا الترخيص نشط في مكان آخر.
        استعادة كلمة سر المالك على جهاز النادي عملية دعم منفصلة. ليست ترخيصًا جديدًا وليست نقلًا.
        الدعم والتدريب والأجهزة والتحديثات تُدرج فقط إذا ظهرت كبنود مشتراة في الصفحة 1. بند ترخيص مدى الحياة وحده لا ينشئ اتفاقية دعم في المنتج الحالي.
        العميل مسؤول عن نسخ بيانات النادي احتياطيًا على جهاز النادي.
        المدفوع والمتبقي على هذه الورقة هما المبلغان المسجّلان عند الإصدار. دفعة لاحقة لا تعيد كتابة هذه النسخة.
        تسجّل HyMotion الدفعات داخليًا (كاش أو تحويل بنكي أو أخرى). هذا السجل ليس إيصال بنك متحقَّق بذاته.
        المنتج الحالي لا يملك وظيفة استرداد تجاري أو إشعار دائن. لا تُفهم هذه الورقة وعدًا بالاسترداد.
        إلغاء سجل البيع التجاري في HyMotion لا يسحب الترخيص ولا يوقف التثبيت ولا يعكس الدفعات من تلقاء نفسه. سحب الترخيص إجراء إداري منفصل.
        هذه الوثيقة ليست فاتورة ضريبية وليست فاتورة اشتراك سحابي.
        """;
}

public static class DeskFeedbackCategories
{
    public const string Feature = "feature";
    public const string Problem = "problem";
    public const string General = "general";
    public const string Other = "other";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Feature, Problem, General, Other
    };
}

public static class DeskFeedbackStatuses
{
    public const string New = "new";
    public const string UnderReview = "under_review";
    public const string Planned = "planned";
    public const string Resolved = "resolved";
    public const string Declined = "declined";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        New, UnderReview, Planned, Resolved, Declined
    };
}
