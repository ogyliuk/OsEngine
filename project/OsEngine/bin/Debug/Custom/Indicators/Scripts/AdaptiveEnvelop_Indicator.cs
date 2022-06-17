using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    public class AdaptiveEnvelop_Indicator : Aindicator
    {
        private IndicatorParameterInt _lengthAdx;
        private IndicatorParameterInt _ratio;
        private IndicatorParameterDecimal _deviation;
        private IndicatorParameterInt _smaLength;

        private IndicatorDataSeries _upChannel;
        private IndicatorDataSeries _downChannel;
        private IndicatorDataSeries _seriesX;
        private Aindicator _adx;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _lengthAdx = CreateParameterInt("Adx Period", 10);
                _ratio = CreateParameterInt("Ratio", 100);
                _deviation = CreateParameterDecimal("Deviation", 0.5m);
                _smaLength = CreateParameterInt("Sma Base", 10);

                _upChannel = CreateSeries("Up Channel", Color.DodgerBlue, IndicatorChartPaintType.Line, true);
                _downChannel = CreateSeries("Down Channel", Color.Red, IndicatorChartPaintType.Line, true);
                _seriesX = CreateSeries("X series", Color.WhiteSmoke, IndicatorChartPaintType.Line, false);

                _adx = IndicatorsFactory.CreateIndicatorByName("ADX", Name + "ADX", false);
                ((IndicatorParameterInt)_adx.Parameters[0]).Bind(_lengthAdx);
                ProcessIndicator("Adx", _adx);
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            if (index - _lengthAdx.ValueInt * 2 + 2 < 0)
            {
                return;
            }

            if(_adx.DataSeries[0].Values[index] == 0)
            {
                return;
            }

            _seriesX.Values[index] = Math.Max(Math.Truncate(_ratio.ValueInt / _adx.DataSeries[0].Values[index]), 1);
            int x = (Int32)_seriesX.Values[index];

           decimal sma = GetSma(candles, index, x * _smaLength.ValueInt);

           decimal deviation = _deviation.ValueDecimal + _deviation.ValueDecimal * x / 20;

           _upChannel.Values[index] = sma + sma * deviation / 100;
            _downChannel.Values[index] = sma - sma * deviation / 100;
        }

        private decimal GetSma(List<Candle> candles, int index, int length)
        {
            if (length == 0 || length == 1 )
            {
                return candles[index].Close;
            }

            decimal value = 0;

            int realLength = 0;

            for (int i = index; i > 0 && i > index - length; i--)
            {
                realLength++;
                value += candles[i].Close;
            }

            return value / realLength;
        }
    }
}