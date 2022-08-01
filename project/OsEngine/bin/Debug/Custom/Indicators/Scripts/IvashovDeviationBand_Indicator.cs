using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    public class IvashovDeviationBand_Indicator : Aindicator
    {
        private IndicatorDataSeries _seriesIvashov;
        private IndicatorParameterInt _lengthMa;
        private IndicatorParameterInt _lengthAvg;

        private IndicatorParameterDecimal _deviationMult;
        private IndicatorParameterInt _seriesCount;

        private Aindicator _sma;
        private IndicatorParameterInt _centralLengthAvg;

        private IndicatorDataSeries _centralMovingSeries;
        private List<IndicatorDataSeries> _upSeries;
        private List<IndicatorDataSeries> _downSeries;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _lengthMa = CreateParameterInt("Averaging period of deviation lines", 100);
                _lengthAvg = CreateParameterInt("Repeated averaging period of deviation lines", 100);
                _seriesIvashov = CreateSeries("Ivashov", Color.AliceBlue, IndicatorChartPaintType.Line, false);

                _deviationMult = CreateParameterDecimal("RainBow Deviation Mult", 0.2m);
                _centralLengthAvg = CreateParameterInt("RainBow Central Ma Length", 100);
                _seriesCount = CreateParameterInt("Band pairs count", 5);

                _sma = IndicatorsFactory.CreateIndicatorByName("Sma", Name + "Sma", false);
                ((IndicatorParameterInt)_sma.Parameters[0]).Bind(_centralLengthAvg);
                ProcessIndicator("SSMA", _sma);

                _centralMovingSeries = CreateSeries("Central Line", Color.AliceBlue, IndicatorChartPaintType.Line, true);

                _upSeries = new List<IndicatorDataSeries>();

                for (int i = 0; i < _seriesCount.ValueInt; i++)
                {
                    IndicatorDataSeries upSeries
                        = CreateSeries("Up Series " + (i + 1), Color.GreenYellow, IndicatorChartPaintType.Line, true);
                    _upSeries.Add(upSeries);
                }

                _downSeries = new List<IndicatorDataSeries>();

                for (int i = 0; i < _seriesCount.ValueInt; i++)
                {
                    IndicatorDataSeries downSeries
                        = CreateSeries("Down Series " + (i + 1), Color.OrangeRed, IndicatorChartPaintType.Line, true);
                    _downSeries.Add(downSeries);
                }
            }
            else if (state == IndicatorState.Dispose)
            {
                if (averagelist != null)
                {
                    averagelist.Clear();
                }
                if (movinglist != null)
                {
                    movinglist.Clear();
                }
                if (range != null)
                {
                    range.Clear();
                }
            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            if (index == 0)
            {
                while (DataSeries.Count >= 3)
                {
                    DataSeries.RemoveAt(DataSeries.Count - 1);
                }

                for (int i = 0; i < _upSeries.Count; i++)
                {
                    _upSeries[i].Values.Clear();
                    _downSeries[i].Values.Clear();
                }

                _upSeries.Clear();

                for (int i = 0; i < _seriesCount.ValueInt; i++)
                {
                    IndicatorDataSeries upSeries
                        = CreateSeries("Up Series " + (i + 1), Color.GreenYellow, IndicatorChartPaintType.Line, true);
                    _upSeries.Add(upSeries);
                    upSeries.Values.Add(0);
                }
                _downSeries.Clear();
                for (int i = 0; i < _seriesCount.ValueInt; i++)
                {
                    IndicatorDataSeries downSeries
                        = CreateSeries("Down Series " + (i + 1), Color.OrangeRed, IndicatorChartPaintType.Line, true);
                    _downSeries.Add(downSeries);
                    downSeries.Values.Add(0);
                }
            }

            _seriesIvashov.Values[index] = GetValue(candles, index);
            _centralMovingSeries.Values[index] = _sma.DataSeries[0].Values[index];

            if (_centralMovingSeries.Values[index] == 0)
            {
                return;
            }

            for (int i = 0; i < _upSeries.Count; i++)
            {
                _upSeries[i].Values[index]
                    = _centralMovingSeries.Values[index]
                      + (_seriesIvashov.Values[index] * (_deviationMult.ValueDecimal)) * (i + 1);
            }
            for (int i = 0; i < _downSeries.Count; i++)
            {
                _downSeries[i].Values[index]
                    = _centralMovingSeries.Values[index]
                      - (_seriesIvashov.Values[index] * (_deviationMult.ValueDecimal)) * (i + 1);
            }
        }

        private decimal GetValue(List<Candle> candles, int index)
        {
            if (index < 2)
            {
                if (averagelist != null)
                {
                    averagelist.Clear();
                }
                if (movinglist != null)
                {
                    movinglist.Clear();
                }
                if (range != null)
                {
                    range.Clear();
                }
            }

            while (index >= movinglist.Count)
            {
                movinglist.Add(CandlesMA(candles, index));
            }
            while (index >= range.Count)
            {
                range.Add(GetRange(candles, movinglist, index));
            }
            while (index >= averagelist.Count)
            {
                averagelist.Add(GetAvg(range, index));
            }

            if (index < _lengthAvg.ValueInt ||
                index < _lengthMa.ValueInt ||
                movinglist[index] == 0)
            {
                return 0;
            }
            return averagelist[index];
        }

        private decimal CandlesMA(List<Candle> candles, int index)
        {
            if (_lengthMa.ValueInt > index)
            {
                return 0;
            }
            return candles.Summ(index - _lengthMa.ValueInt, index, "Close") / _lengthMa.ValueInt;
        }

        private decimal GetRange(List<Candle> candles, List<decimal> moving, int index)
        {
            if (moving[index] == 0)
            {
                return 0;
            }
            return Math.Abs(moving[index] - candles[index].Close);
        }

        private decimal GetAvg(List<decimal> list, int index)
        {
            decimal value = 0;
            if (index >= _lengthAvg.ValueInt)
            {

                decimal var = 0;
                for (int i = index - _lengthAvg.ValueInt + 1; i < index + 1; i++)
                {
                    var += list[i];
                }
                var = var / _lengthAvg.ValueInt;
                value = var;
            }
            return Math.Round(value, 8);

        }

        private List<decimal> range = new List<decimal>();
        private List<decimal> movinglist = new List<decimal>();
        private List<decimal> averagelist = new List<decimal>();

    }
}
