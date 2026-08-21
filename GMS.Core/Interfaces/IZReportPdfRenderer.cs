namespace GMS.Core.Interfaces;

using GMS.Core.Models;

/// <summary>
/// Renders Z-Report PDFs from a flat snapshot model (no persistence dependency).
/// </summary>
public interface IZReportPdfRenderer
{
    byte[] Render(ZReportPdfModel model);
    byte[] RenderShiftClosing(ShiftZReportPdfModel model);
}
