using System.Collections.Generic;
using System.Drawing;

namespace OsEngine.Indicators
{
    public class IndicatorElement
    {
        public Color Color { get; private set; }
        public List<decimal> ValuesToChart { get; private set; }
        public IndicatorChartPaintType Type { get; private set; }

        public IndicatorElement(Color color, List<decimal> valuesToChart, IndicatorChartPaintType type)
        {
            Color = color;
            ValuesToChart = valuesToChart;
            Type = type;
        }
    }
}
