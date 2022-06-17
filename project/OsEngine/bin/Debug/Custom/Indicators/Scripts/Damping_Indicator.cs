using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    class Damping_Indicator : Aindicator
    {
        private IndicatorParameterInt _length;
        private IndicatorDataSeries _series;
        private Aindicator _smaHigh;
        private Aindicator _smaLow;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _length = CreateParameterInt("Period", 14);

                _series = CreateSeries("Damping", Color.DodgerBlue, IndicatorChartPaintType.Line, true);

                _smaHigh = IndicatorsFactory.CreateIndicatorByName("Sma", Name + "SmaHigh", false);
                ((IndicatorParameterString)_smaHigh.Parameters[1]).ValueString = "High";
                ((IndicatorParameterInt)_smaHigh.Parameters[0]).Bind(_length);
                ProcessIndicator("High", _smaHigh);

                _smaLow = IndicatorsFactory.CreateIndicatorByName("Sma", Name + "SmaLow", false);
                ((IndicatorParameterString)_smaLow.Parameters[1]).ValueString = "Low";
                ((IndicatorParameterInt)_smaLow.Parameters[0]).Bind(_length);
                ProcessIndicator("Low", _smaLow);


            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            if (index - _length.ValueInt * 2 + 2 < 0)
            {
                return;
            }

            decimal smaH1 = _smaHigh.DataSeries[0].Values[index];
            decimal smaL1 = _smaLow.DataSeries[0].Values[index];
            decimal smaH2 = _smaHigh.DataSeries[0].Values[index - _length.ValueInt];
            decimal smaL2 = _smaLow.DataSeries[0].Values[index - _length.ValueInt];

            if (smaH2 - smaL2 != 0)
            {
                decimal damping = (smaH1 - smaL1) / (smaH2 - smaL2);
                _series.Values[index] = Math.Round(damping, 4);
            }

        }
    }
}
