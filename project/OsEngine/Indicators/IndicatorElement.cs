using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System;

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
            Type = type;
            FullReloadOnNewCandle = fullReloadOnNewCandle;

            // Создаём копию коллекции БЕЗ lock, так как lock здесь бесполезен
            // Проблема в том, что исходная коллекция может изменяться извне
            if (valuesToChart == null)
            {
                ValuesToChart = new List<object>();
                return;
            }

            try
            {
                if (valuesToChart is List<object> listObj)
                {
                    // Создаём копию списка
                    ValuesToChart = new List<object>(listObj);
                }
                else if (valuesToChart is IEnumerable<object> enumObj)
                {
                    ValuesToChart = new List<object>(enumObj);
                }
                else
                {
                    ValuesToChart = Enumerable.Cast<object>(valuesToChart).ToList();
                }
            }
            catch (System.InvalidOperationException)
            {
                while (true)
                {
                    // Если коллекция изменилась во время перечисления, пробуем ещё раз
                    try
                    {
                        System.Threading.Thread.Sleep(10);
                        if (valuesToChart is IEnumerable<object> enumObj)
                        {
                            ValuesToChart = new List<object>(enumObj);
                        }
                        else
                        {
                            ValuesToChart = Enumerable.Cast<object>(valuesToChart).ToList();
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error of working with IndicatorElement: " + ex.Message);
                    }
                }
            }
        }
    }
}