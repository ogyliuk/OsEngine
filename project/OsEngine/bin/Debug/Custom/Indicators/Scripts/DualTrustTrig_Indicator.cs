using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    public class DualTrustTrig_Indicator : Aindicator
    {

        private IndicatorDataSeries _seriesSellLenght;
        private IndicatorDataSeries _seriesBuyLenght;
        private IndicatorDataSeries _seriesSellChannel;
        private IndicatorDataSeries _seriesBuyChannel;

        private IndicatorParameterInt _periodShort; //период для нахждения sellTrig
        private IndicatorParameterInt _periodLong; //период для нахождения buyTrig
        private IndicatorParameterDecimal _k1Long; //период для нахождения buyTrig
        private IndicatorParameterDecimal _k2Short; //период для нахождения sellTrig
        private IndicatorParameterInt _Compress; //период Сжатия


        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _periodShort = CreateParameterInt("MdaySell", 3);
                _periodLong = CreateParameterInt("NdayBuy", 3);
                _k1Long = CreateParameterDecimal("K1Buy", 0.5m); //коэффициент для рассчета диапазона на покупку
                _k2Short = CreateParameterDecimal("K1Sell", 0.5m); //коэффициент для рассчета диапазона на продажу
                _Compress = CreateParameterInt("Коэфф. сжатия", 2); //коэффициент для рассчета диапазона на продажу

                _seriesSellLenght = CreateSeries("Sell Lenght", Color.Green, IndicatorChartPaintType.Column, false);
                _seriesBuyLenght = CreateSeries("Buy Lenght", Color.Red, IndicatorChartPaintType.Column, false);

                _seriesBuyChannel = CreateSeries("Buy Channel", Color.Red, IndicatorChartPaintType.Line, true);
                _seriesSellChannel = CreateSeries("Sell Channel", Color.Green, IndicatorChartPaintType.Line, true);

            }
            else
            {



            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            OldLogic(candles, index);
            // NewLogic(candles, index);
        }

        private int _lastCandlesCount = 0;

        private void NewLogic(List<Candle> candlesOriginal, int index)
        {
            if (index < _periodShort.ValueInt ||
                index < _periodLong.ValueInt)
            {
                _lastCandlesCount = 0;
                return;
            }

            List<Candle> candles = candlesOriginal.GetRange(0, index + 1);

            if (_Compress.ValueInt > 1)
            {
                //candles = CandleConverter.Merge(candles, _Compress.ValueInt);
            }

            if (candles.Count == _lastCandlesCount)
            {
                _seriesSellLenght.Values[index] = _seriesSellLenght.Values[index - 1];
                _seriesBuyLenght.Values[index] = _seriesBuyLenght.Values[index - 1];

                _seriesSellChannel.Values[index] = _seriesSellChannel.Values[index - 1];
                _seriesBuyChannel.Values[index] = _seriesBuyChannel.Values[index - 1];
                return;
            }

            _lastCandlesCount = candles.Count;

            int periodLong = _periodLong.ValueInt;
            decimal k1Long = _k1Long.ValueDecimal;

            decimal hhLongMinusOne = 0;
            decimal hcLongMinusOne = 0;
            decimal llLongMinusOne = decimal.MaxValue;
            decimal lcLongMinusOne = decimal.MaxValue;

            for (int i = candles.Count - 2; i > 0 && i > candles.Count - 2 - periodLong; i--)
            {
                if (candles[i].High > hhLongMinusOne)
                {
                    hhLongMinusOne = candles[i].High;
                }
                if (candles[i].Low < llLongMinusOne)
                {
                    llLongMinusOne = candles[i].Low;
                }
                if (candles[i].Close > hcLongMinusOne)
                {
                    hcLongMinusOne = candles[i].Close;
                }
                if (candles[i].Close < lcLongMinusOne)
                {
                    lcLongMinusOne = candles[i].Close;
                }
            }

            if (hhLongMinusOne == 0 ||
                hcLongMinusOne == 0 ||
                llLongMinusOne == decimal.MaxValue ||
                lcLongMinusOne == decimal.MaxValue)
            {
                return;
            }

            int periodShort = _periodShort.ValueInt;
            decimal k2Short = _k2Short.ValueDecimal;

            decimal hhShortMinusOne = 0;
            decimal hcShortMinusOne = 0;
            decimal llShortMinusOne = decimal.MaxValue;
            decimal lcShortMinusOne = decimal.MaxValue;

            for (int i = candles.Count - 2; i > 0 && i > candles.Count - 2 - periodShort; i--)
            {
                if (candles[i].High > hhShortMinusOne)
                {
                    hhShortMinusOne = candles[i].High;
                }
                if (candles[i].Low < llShortMinusOne)
                {
                    llShortMinusOne = candles[i].Low;
                }
                if (candles[i].Close > hcShortMinusOne)
                {
                    hcShortMinusOne = candles[i].Close;
                }
                if (candles[i].Close < lcShortMinusOne)
                {
                    lcShortMinusOne = candles[i].Close;
                }
            }

            if (hhShortMinusOne == 0 ||
                hcShortMinusOne == 0 ||
                llShortMinusOne == decimal.MaxValue ||
                lcShortMinusOne == decimal.MaxValue)
            {
                return;
            }

            /*if (candles[index].TimeStart.Hour == 13 &&
                candles[index].TimeStart.Minute == 20 &&
                candles[index].TimeStart.Day == 4 &&
                candles[index].TimeStart.Month == 5)
            {

            }*/

            _seriesSellLenght.Values[index] = -1.0m * Math.Max((hhShortMinusOne - lcShortMinusOne) * k2Short, (hcShortMinusOne - llShortMinusOne) * k2Short);
            _seriesBuyLenght.Values[index] = Math.Max((hhLongMinusOne - lcLongMinusOne) * k1Long, (hcLongMinusOne - llLongMinusOne) * k1Long);

            _seriesSellChannel.Values[index] = candles[candles.Count - 1].Open + _seriesSellLenght.Values[index];
            _seriesBuyChannel.Values[index] = candles[candles.Count - 1].Open + _seriesBuyLenght.Values[index];

        }

        private void OldLogic(List<Candle> candles, int index)
        {
            if (index < _periodShort.ValueInt ||
                index < _periodLong.ValueInt)
            {
                return;
            }

            if (index % _Compress.ValueInt != 0)
            {
                _seriesSellLenght.Values[index] = _seriesSellLenght.Values[index - 1];
                _seriesBuyLenght.Values[index] = _seriesBuyLenght.Values[index - 1];

                _seriesSellChannel.Values[index] = _seriesSellChannel.Values[index - 1];
                _seriesBuyChannel.Values[index] = _seriesBuyChannel.Values[index - 1];
                return;
            }

            int periodLong = _periodLong.ValueInt * _Compress.ValueInt;
            decimal k1Long = _k1Long.ValueDecimal;

            decimal hhLongMinusOne = 0;
            decimal hcLongMinusOne = 0;
            decimal llLongMinusOne = decimal.MaxValue;
            decimal lcLongMinusOne = decimal.MaxValue;

            for (int i = index - _Compress.ValueInt; i > 0 && i > index - periodLong - _Compress.ValueInt; i--)
            {
                if (candles[i].High > hhLongMinusOne)
                {
                    hhLongMinusOne = candles[i].High;
                }
                if (candles[i].Low < llLongMinusOne)
                {
                    llLongMinusOne = candles[i].Low;
                }
                if (candles[i].Close > hcLongMinusOne)
                {
                    hcLongMinusOne = candles[i].Close;
                }
                if (candles[i].Close < lcLongMinusOne)
                {
                    lcLongMinusOne = candles[i].Close;
                }
            }

            if (hhLongMinusOne == 0 ||
                hcLongMinusOne == 0 ||
                llLongMinusOne == decimal.MaxValue ||
                lcLongMinusOne == decimal.MaxValue)
            {
                return;
            }

            int periodShort = _periodShort.ValueInt * _Compress.ValueInt;
            decimal k2Short = _k2Short.ValueDecimal;

            decimal hhShortMinusOne = 0;
            decimal hcShortMinusOne = 0;
            decimal llShortMinusOne = decimal.MaxValue;
            decimal lcShortMinusOne = decimal.MaxValue;

            for (int i = index - _Compress.ValueInt; i > 0 && i > index - periodShort - _Compress.ValueInt; i--)
            {
                if (candles[i].High > hhShortMinusOne)
                {
                    hhShortMinusOne = candles[i].High;
                }
                if (candles[i].Low < llShortMinusOne)
                {
                    llShortMinusOne = candles[i].Low;
                }
                if (candles[i].Close > hcShortMinusOne)
                {
                    hcShortMinusOne = candles[i].Close;
                }
                if (candles[i].Close < lcShortMinusOne)
                {
                    lcShortMinusOne = candles[i].Close;
                }
            }

            if (hhShortMinusOne == 0 ||
                hcShortMinusOne == 0 ||
                llShortMinusOne == decimal.MaxValue ||
                lcShortMinusOne == decimal.MaxValue)
            {
                return;
            }

            if (candles[index].TimeStart.Hour == 13 &&
                candles[index].TimeStart.Minute == 20 &&
                candles[index].TimeStart.Day == 4 &&
                candles[index].TimeStart.Month == 5)
            {

            }

            //shortTrig[bar] = -1.0 * Math.Max((hhShort[bar - 1] - lcShort[bar - 1]) * k2Short, (hcShort[bar - 1] - llShort[bar - 1]) * k2Short);
            //longTrig[bar] = Math.Max((hhLong[bar - 1] - lcLong[bar - 1]) * k1Long, (hcLong[bar - 1] - llLong[bar - 1]) * k1Long);

            _seriesSellLenght.Values[index] = -1.0m * Math.Max((hhShortMinusOne - lcShortMinusOne) * k2Short, (hcShortMinusOne - llShortMinusOne) * k2Short);
            _seriesBuyLenght.Values[index] = Math.Max((hhLongMinusOne - lcLongMinusOne) * k1Long, (hcLongMinusOne - llLongMinusOne) * k1Long);

            _seriesSellChannel.Values[index] = candles[index - _Compress.ValueInt + 1].Open + _seriesSellLenght.Values[index];
            _seriesBuyChannel.Values[index] = candles[index - _Compress.ValueInt + 1].Open + _seriesBuyLenght.Values[index];

        }
    }
}