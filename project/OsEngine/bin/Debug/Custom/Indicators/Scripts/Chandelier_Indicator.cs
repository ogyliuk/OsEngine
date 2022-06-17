using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    public class Chandelier_Indicator : Aindicator
    {
        private IndicatorParameterInt _lengthAtr;
        private IndicatorParameterDecimal _мult;

        private IndicatorDataSeries _series;
        private Aindicator _atr;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _lengthAtr = CreateParameterInt("Atr Period", 10);
                _мult = CreateParameterDecimal("Chandelier Mult", 4);
                _series = CreateSeries("Chandelier", Color.GreenYellow, IndicatorChartPaintType.Line, true);

                _atr = IndicatorsFactory.CreateIndicatorByName("ATR", Name + "atr", false);
                ((IndicatorParameterInt)_atr.Parameters[0]).Bind(_lengthAtr);
                ProcessIndicator("Atr", _atr);
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            if (index - _lengthAtr.ValueInt <= 0)
            {
                return;
            }

            decimal maxHigh = 0;

            for (int i = index; i > index - _lengthAtr.ValueInt; i--)
            {
                if (candles[i].High > maxHigh)
                {
                    maxHigh = candles[i].High;
                }
            }

            decimal result = maxHigh - _мult.ValueDecimal * _atr.DataSeries[0].Values[index];

            _series.Values[index] = result;
        }
    }
}