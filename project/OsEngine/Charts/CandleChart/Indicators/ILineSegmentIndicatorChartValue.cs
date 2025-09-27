using System.Drawing;
using System.Windows.Forms.DataVisualization.Charting;

namespace OsEngine.Charts.CandleChart.Indicators
{
    public interface ILineSegmentIndicatorChartValue : IIndicatorChartValue
    {
        public Color Color { get; }
        public int LineWidth { get; }
        public bool LabelEnabled { get; }
        public ChartDashStyle LineStyle { get; set; }
        public DataPoint PointFrom { get; }
        public DataPoint PointTo { get; set; }
    }
}
