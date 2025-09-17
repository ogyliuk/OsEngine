using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace OsEngine.Indicators
{
    public class IndicatorElement
    {
        public Color Color { get; }
        public List<object> ValuesToChart { get; }
        public IndicatorChartPaintType Type { get; }
        public bool FullReloadOnNewCandle { get; }

        public IndicatorElement(Color color, IEnumerable valuesToChart, IndicatorChartPaintType type, bool fullReloadOnNewCandle = false)
        {
            Color = color;
            IEnumerable<object> temp;
            if (valuesToChart is List<object>)
            {
                temp = (List<object>)valuesToChart;
            }
            else if (valuesToChart is IEnumerable<object>)
            {
                temp = ((IEnumerable<object>)valuesToChart).ToList();
            }
            else
            {
                temp = Enumerable.Cast<object>(valuesToChart).ToList();
            }
            ValuesToChart = temp.ToList();
            Type = type;
            FullReloadOnNewCandle = fullReloadOnNewCandle;
        }
    }
}
