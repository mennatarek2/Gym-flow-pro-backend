namespace GMS.Application.DTOs.Analytics;

/// <summary>
/// Attendance heatmap data: 7 days × 24 hours.
/// [0][0] = Monday 00:00-01:00
/// [6][23] = Sunday 23:00-00:00
/// </summary>
public class AttendanceHeatmapDto
{
    public int[][] Data { get; set; } = new int[7][]; // 7 days, 24 hours each

    public AttendanceHeatmapDto()
    {
        // Initialize 7x24 matrix with zeros
        for (int i = 0; i < 7; i++)
        {
            Data[i] = new int[24];
            for (int j = 0; j < 24; j++)
            {
                Data[i][j] = 0;
            }
        }
    }
}
