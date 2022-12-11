using System.Collections.Generic;

namespace OsEngine.Indicators
{
    /// <summary>
    /// allows to have multiple paiting elements for one indicator
    /// позволяет иметь несколько графических элементов в одно и том же индикаторе
    /// </summary>
    public interface IMultiElementIndicator : IIndicator
    {
        /// <summary>
        /// list of elements which needs to be painted on the chart
        /// список элементов, которые должны быть отрисованы на чарте
        /// </summary>
        List<IndicatorElement> Elements { get; }
    }
}
