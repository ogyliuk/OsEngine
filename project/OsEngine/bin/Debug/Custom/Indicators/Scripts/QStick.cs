/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.Indicators;
using System;
using System.Collections.Generic;
using System.Drawing;

//https://doc.stocksharp.ru/topics/IndicatorQStick.html


class QStick : Aindicator
{/// <summary>
 ///period for which the indicator is calculated
 /// период за который рассчитывается индикатор
 /// </summary>
    private IndicatorParameterInt _lenght;
    /// <summary>
    /// indicator data series
    /// серия данных индикатора
    /// </summary>
    private IndicatorDataSeries _series;
    /// <summary>
    /// Moving Average Type
    /// Тип скользящей средней
    /// </summary>
    private IndicatorParameterString _typeMA;

    /// <summary>
    /// initialization
    /// </summary>
    /// <param name="state">Indicator Configure Настройка индикатора</param>   
    public override void OnStateChange(IndicatorState state)
    {
        if (state == IndicatorState.Configure)
        {
            _lenght = CreateParameterInt("Length", 14);
            _typeMA = CreateParameterStringCollection("Type MA", "SMA", new List<string> { "SMA", "EMA" });
            _series = CreateSeries("QStick", Color.Red, IndicatorChartPaintType.Line, true);
        }
    }
    /// <summary>
    /// an iterator method to fill the indicator 
    /// Метод итератор для заполнения индикатора
    /// </summary>
    /// <param name="candles">collection candles коллекция свечей</param>
    /// <param name="index">index to use in the collection of candles индекс для использования в коллекции свечей</param>
    public override void OnProcess(List<Candle> candles, int index)
    {
        if (_typeMA.ValueString == "SMA")
            CaclQstickForSMA(candles, index);
        else
            CalcQstickForEMA(candles, index);
    }
    /// <summary>
    /// Calculate the Qstick For EMA value Вычисляем значение Qstick сглаженой Ema
    /// </summary>
    /// <param name="candles">collection candles коллекция свечей</param>
    /// <param name="index">index to use in the collection of candles индекс для использования в коллекции свечей</param>
    private void CalcQstickForEMA(List<Candle> candles, int index)
    {
        decimal result = 0;

        if (index == _lenght.ValueInt)
        {
            decimal lastMoving = 0;

            for (int i = index - _lenght.ValueInt + 1; i < index + 1; i++)
            {
                lastMoving += candles[i].Close - candles[i].Open;
            }
            lastMoving = lastMoving / _lenght.ValueInt;
            result = lastMoving;
        }
        else if (index > _lenght.ValueInt)
        {
            decimal a = Math.Round(2.0m / (_lenght.ValueInt + 1), 8);
            decimal emaLast = _series.Values[index - 1];
            decimal p = candles[index].Close - candles[index].Open;
            result = emaLast + (a * (p - emaLast));
        }
        _series.Values[index] = Math.Round(result, 8);
    }
    /// <summary>
    /// Calculate the Qstick For Sma value Вычисляем значение Qstick сглаженой Sma
    /// </summary>
    /// <param name="candles">collection candles коллекция свечей</param>
    /// <param name="index">index to use in the collection of candles индекс для использования в коллекции свечей</param>
    public void CaclQstickForSMA(List<Candle> candles, int index)
    {
        if (_lenght.ValueInt > index)
        {
            _series.Values[index] = 0;
            return;
        }
        string typeClose = "Close";
        string typeOpen = "Open";

        decimal temp = candles.Summ(index - _lenght.ValueInt, index, typeClose) - candles.Summ(index - _lenght.ValueInt, index, typeOpen);

        _series.Values[index] = temp / _lenght.ValueInt;
    }
}
