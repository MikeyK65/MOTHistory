using Microsoft.Maui.Graphics;
using MOTHistory.Maui.Models;

namespace MOTHistory.Maui.Services;

public class MileageChartDrawable : IDrawable
{
    public List<MotTest> Tests { get; set; } = [];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var points = Tests
            .Where(t => t.OdometerMiles > 0)
            .OrderBy(t => t.TestDate)
            .ToList();

        if (points.Count < 2) return;

        const float padLeft = 48f;
        const float padRight = 12f;
        const float padTop = 10f;
        const float padBottom = 28f;

        float chartLeft = padLeft;
        float chartRight = dirtyRect.Width - padRight;
        float chartTop = padTop;
        float chartBottom = dirtyRect.Height - padBottom;
        float chartW = chartRight - chartLeft;
        float chartH = chartBottom - chartTop;

        var minDate = points.First().TestDate;
        var maxDate = points.Last().TestDate;
        double totalDays = Math.Max(1, (maxDate - minDate).TotalDays);

        double minMiles = points.Min(t => t.OdometerMiles);
        double maxMiles = points.Max(t => t.OdometerMiles);
        double mileRange = Math.Max(1, maxMiles - minMiles);

        float ToX(DateTime date) =>
            chartLeft + (float)((date - minDate).TotalDays / totalDays * chartW);

        float ToY(double miles) =>
            chartBottom - (float)((miles - minMiles) / mileRange * chartH);

        // Horizontal grid lines
        canvas.StrokeColor = Color.FromArgb("#E5E5EA");
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 4; i++)
        {
            float y = chartTop + chartH * i / 4;
            canvas.DrawLine(chartLeft, y, chartRight, y);

            double mileMark = maxMiles - mileRange * i / 4;
            canvas.FontSize = 9;
            canvas.FontColor = Color.FromArgb("#6E6E73");
            canvas.DrawString(FormatMileage(mileMark), 0, y - 7, padLeft - 4, 14,
                HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        // Line
        var path = new PathF();
        for (int i = 0; i < points.Count; i++)
        {
            float x = ToX(points[i].TestDate);
            float y = ToY(points[i].OdometerMiles);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        canvas.StrokeColor = Color.FromArgb("#007AFF");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);

        // Dots + year labels
        canvas.FillColor = Color.FromArgb("#007AFF");
        canvas.FontSize = 10;
        canvas.FontColor = Color.FromArgb("#6E6E73");
        int? lastLabelYear = null;
        foreach (var t in points)
        {
            float x = ToX(t.TestDate);
            float y = ToY(t.OdometerMiles);
            canvas.FillEllipse(x - 4, y - 4, 8, 8);

            int year = t.TestDate.Year;
            if (year != lastLabelYear)
            {
                canvas.DrawString(year.ToString(), x - 16, chartBottom + 4, 32, 14,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
                lastLabelYear = year;
            }
        }
    }

    private static string FormatMileage(double miles)
    {
        if (miles >= 1_000_000) return $"{miles / 1_000_000:F1}M";
        if (miles >= 1_000) return $"{miles / 1_000:F0}k";
        return $"{miles:F0}";
    }
}
