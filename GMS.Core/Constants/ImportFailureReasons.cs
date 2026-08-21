namespace GMS.Core.Constants;

/// <summary>Machine-readable rejection reasons returned by IImportService, encoded as the leading
/// "CODE|message" segment of a failed Result's Error string (same convention as SaleFailureReasons).</summary>
public static class ImportFailureReasons
{
    public const string BatchNotFound = "BATCH_NOT_FOUND";
    public const string InvalidStatus = "INVALID_STATUS";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string TooManyRows = "TOO_MANY_ROWS";
    public const string UnsupportedFileType = "UNSUPPORTED_FILE_TYPE";
    public const string RollbackWindowExpired = "ROLLBACK_WINDOW_EXPIRED";
}

/// <summary>Per-row validation/error codes stored in ImportRow.ErrorCodes (comma-separated when
/// more than one applies to the same row).</summary>
public static class ImportRowErrorCodes
{
    public const string PhoneInvalid = "PHONE_INVALID";
    public const string PhoneDupFile = "PHONE_DUP_FILE";
    public const string PhoneExists = "PHONE_EXISTS";
    public const string PlanUnmatched = "PLAN_UNMATCHED";
    public const string DateRangeInvalid = "DATE_RANGE_INVALID";
    public const string RetainedHasActivity = "RETAINED_HAS_ACTIVITY";
    public const string RolledBack = "ROLLED_BACK";
}
