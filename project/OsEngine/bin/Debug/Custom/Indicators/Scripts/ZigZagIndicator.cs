using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

public class ZigZagIndicator : Aindicator
{
    private IndicatorParameterInt _period;
    private IndicatorDataSeries _seriesAllZigZagLineDots;
    private IndicatorDataSeries _seriesPeakPricesAll;
    private IndicatorDataSeries _seriesPeakPricesHigh;
    private IndicatorDataSeries _seriesPeakPricesLow;

    private int _trendDirection = 0;
    private int _peakCandidateIndex = -1;
    private decimal _peakCandidatePrice = 0;

    public override void OnStateChange(IndicatorState state)
    {
        _period = CreateParameterInt("Length", 14);

        _seriesPeakPricesAll = CreateSeries("ZigZagPeaksAll", Color.CornflowerBlue, IndicatorChartPaintType.Point, false);
        _seriesPeakPricesAll.CanReBuildHistoricalValues = true;

        _seriesAllZigZagLineDots = CreateSeries("ZigZagLineDots", Color.CornflowerBlue, IndicatorChartPaintType.Point, true);
        _seriesAllZigZagLineDots.CanReBuildHistoricalValues = true;

        _seriesPeakPricesHigh = CreateSeries("ZigZagPeaksHigh", Color.GreenYellow, IndicatorChartPaintType.Point, false);
        _seriesPeakPricesHigh.CanReBuildHistoricalValues = true;

        _seriesPeakPricesLow = CreateSeries("ZigZagPeaksLow", Color.Red, IndicatorChartPaintType.Point, false);
        _seriesPeakPricesLow.CanReBuildHistoricalValues = true;
    }

    public override void OnProcess(List<Candle> candles, int index)
    {
        // NOTE: before this method call 0 value has been already set
        // for all 4 data series for this 'index' and we just need to decide
        // whether we want to override this 0 by some another value

        if (index < _period.ValueInt * 2)
        {
            _trendDirection = 0;
            _peakCandidateIndex = -1;
            _peakCandidatePrice = 0;
            return;
        }

        bool upPeakCandidateFound = false;
        bool downPeakCandidateFound = false;
        bool betterUpPeakCandidateFound = false;
        bool betterDownPeakCandidateFound = false;
        bool trendUp = _trendDirection > 0;
        bool trendDown = _trendDirection < 0;
        decimal currentCandleHigh = candles[index].High;
        decimal currentCandleLow = candles[index].Low;

        if (_peakCandidatePrice == 0)
        {
            decimal currentCandleMiddle = currentCandleLow + (currentCandleHigh - currentCandleLow) / 2;
            _peakCandidatePrice = currentCandleMiddle;
        }

        bool isSwingHigh = currentCandleHigh == GetExtremum(candles, _period.ValueInt, "High", index);
        bool isSwingLow = currentCandleLow == GetExtremum(candles, _period.ValueInt, "Low", index);
        if (!isSwingHigh && !isSwingLow)
        {
            return;
        }

        if (isSwingHigh)
        {
            if (trendUp)
            {
                if (currentCandleHigh >= _peakCandidatePrice)
                {
                    _peakCandidatePrice = currentCandleHigh;
                    betterUpPeakCandidateFound = true;
                }
            }
            else
            {
                _trendDirection = 1; // UP
                _peakCandidatePrice = currentCandleHigh;
                upPeakCandidateFound = true;
            }
        }
        else if (isSwingLow)
        {
            if (trendDown)
            {
                if (currentCandleLow <= _peakCandidatePrice)
                {
                    _peakCandidatePrice = currentCandleLow;
                    betterDownPeakCandidateFound = true;
                }
            }
            else
            {
                _trendDirection = -1; // DOWN
                _peakCandidatePrice = currentCandleLow;
                downPeakCandidateFound = true;
            }
        }

        if (upPeakCandidateFound || downPeakCandidateFound || betterUpPeakCandidateFound || betterDownPeakCandidateFound)
        {
            bool alreadyHavePeakCandidate = _peakCandidateIndex >= 0;
            if (alreadyHavePeakCandidate)
            {
                if (betterUpPeakCandidateFound)
                {
                    _seriesPeakPricesAll.Values[_peakCandidateIndex] = 0;
                    _seriesPeakPricesHigh.Values[_peakCandidateIndex] = 0;
                }
                else if (betterDownPeakCandidateFound)
                {
                    _seriesPeakPricesAll.Values[_peakCandidateIndex] = 0;
                    _seriesPeakPricesLow.Values[_peakCandidateIndex] = 0;
                }
            }

            if (upPeakCandidateFound || betterUpPeakCandidateFound)
            {
                _seriesPeakPricesAll.Values[index] = _peakCandidatePrice;
                _seriesPeakPricesHigh.Values[index] = _peakCandidatePrice;
            }
            else if (downPeakCandidateFound || betterDownPeakCandidateFound)
            {
                _seriesPeakPricesAll.Values[index] = _peakCandidatePrice;
                _seriesPeakPricesLow.Values[index] = _peakCandidatePrice;
            }

            _peakCandidateIndex = index;

            if (betterUpPeakCandidateFound || betterDownPeakCandidateFound)
            {
                ReCalcAllZigZagLineDots(_seriesPeakPricesAll.Values, _seriesAllZigZagLineDots.Values);
            }
        }
    }

    private decimal GetExtremum(List<Candle> candles, int period, string pointType, int index)
    {
        try
        {
            List<decimal> values = new List<decimal>();
            for (int i = index; i >= index - period; i--)
            {
                values.Add(candles[i].GetPoint(pointType));
            }
            if (pointType == "High")
            {
                return values.Max();
            }
            if (pointType == "Low")
            {
                return values.Min();
            }
        }
        catch { }

        return 0;
    }

    private void ReCalcAllZigZagLineDots(List<decimal> allPeaksPrices, List<decimal> zigZagLinePointsOfAllCandles)
    {
        decimal previousPeakPrice = 0;
        int previousPeakPriceIndex = 0;

        for (int currentPeakPriceIndex = 0; currentPeakPriceIndex < allPeaksPrices.Count; currentPeakPriceIndex++)
        {
            decimal currentPeakPrice = allPeaksPrices[currentPeakPriceIndex];
            if (currentPeakPrice == 0)
            {
                continue;
            }

            if (previousPeakPrice == 0)
            {
                previousPeakPrice = currentPeakPrice;
                previousPeakPriceIndex = currentPeakPriceIndex;
                continue;
            }

            decimal peakToPeakLinePointPrice = previousPeakPrice;
            bool goToDownPeak = currentPeakPrice < previousPeakPrice;
            decimal priceDistanceBetweenLinePoints = Math.Abs(previousPeakPrice - currentPeakPrice) / (currentPeakPriceIndex - previousPeakPriceIndex);
            for (int j = previousPeakPriceIndex; j < currentPeakPriceIndex; j++)
            {
                zigZagLinePointsOfAllCandles[j] = peakToPeakLinePointPrice;
                peakToPeakLinePointPrice = goToDownPeak ?
                    peakToPeakLinePointPrice - priceDistanceBetweenLinePoints :
                    peakToPeakLinePointPrice + priceDistanceBetweenLinePoints;
            }

            previousPeakPriceIndex = currentPeakPriceIndex;
            previousPeakPrice = currentPeakPrice;
        }
    }
}