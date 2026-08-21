namespace GMS.Core.Constants;

/// <summary>
/// Machine-readable rejection reasons returned by IZReportService, encoded as the leading
/// "CODE|message" segment of Result.Error (see SaleFailureReasons/ShiftFailureReasons for the
/// same convention).
/// </summary>
public static class ZReportFailureReasons
{
    public const string NotFound = "ZREPORT_NOT_FOUND";
}
