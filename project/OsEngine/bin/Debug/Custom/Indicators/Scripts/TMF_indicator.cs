using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;


public class TMF_indicator : Aindicator
{
    //Twiggs Money Flow (TMF)
    //https://www.marketvolume.com/technicalanalysis/twiggsmoneyflow.asp
    //https://ru.tradingview.com/script/81mNibdq-Twiggs-Money-Flow/
    //https://www.incrediblecharts.com/indicators/twiggs_money_flow.php
    //https://www.mql5.com/ru/code/21687

    private IndicatorParameterInt _EMAPeriod;
    //  private IndicatorParameterInt _SmoothTMF;

    private IndicatorDataSeries Tmf;
    //  private IndicatorDataSeries TmfSignal;
    private IndicatorDataSeries Range;
    private IndicatorDataSeries RangeV;
    private IndicatorDataSeries wwmaVolume;

    public override void OnStateChange(IndicatorState state)
    {
        if (state == IndicatorState.Configure)
        {

            _EMAPeriod = CreateParameterInt("EMA Period", 21);
            //    _SmoothTMF = CreateParameterInt("Smooth TMA", 50);

            Range = CreateSeries("Range", Color.DodgerBlue, IndicatorChartPaintType.Line, false);
            RangeV = CreateSeries("wwma Range", Color.DodgerBlue, IndicatorChartPaintType.Line, false);
            wwmaVolume = CreateSeries("wwma Volume", Color.DodgerBlue, IndicatorChartPaintType.Line, false);
            Tmf = CreateSeries("TMF", Color.DodgerBlue, IndicatorChartPaintType.Line, true);
            //  TmfSignal = CreateSeries("wwma TMF", Color.Red, IndicatorChartPaintType.Line, true);
            Save();
            Reload();
        }
    }

    public decimal GetWWMAVolume(IndicatorDataSeries SeriesData, List<Candle> candles, int index, int _period)
    {
        decimal result = 0;
        decimal alpha = 0;

        if (index == _period)
        {
            decimal lastMoving = 0;

            for (int i = index - _period + 1; i < index + 1; i++)
            {
                lastMoving += candles[i].Volume;
            }
            lastMoving = lastMoving / _period;
            result = lastMoving;
        }
        else if (index > _period)
        {
            decimal wwmaLast = SeriesData.Values[index - 1];
            alpha = (decimal)1 / _period;
            result = alpha * candles[index].Volume + (1 - alpha) * wwmaLast;
        }
        return Math.Round(result, 8);
    }

    public decimal GetWWMA(IndicatorDataSeries ValuesData, IndicatorDataSeries SeriesData, int index, int _period)
    {
        decimal result = 0;
        decimal alpha = 0;

        if (index == _period)
        {
            decimal lastMoving = 0;
            for (int i = index - _period + 1; i < index + 1; i++)
            {
                lastMoving += SeriesData.Values[i];
            }
            lastMoving = lastMoving / _period;
            result = lastMoving;
        }
        else if (index > _period)
        {
            decimal wwmaLast = ValuesData.Values[index - 1];
            alpha = (decimal)1 / _period;
            result = alpha * SeriesData.Values[index] + (1 - alpha) * wwmaLast;
        }
        return Math.Round(result, 8);
    }

    public override void OnProcess(List<Candle> candles, int index)
    {
        if (index < 2)
        {
            return;
        }

        decimal LL = Math.Min(candles[index].Low, candles[index - 1].Close);
        decimal HH = Math.Max(candles[index].High, candles[index - 1].Close);

        Range.Values[index] = candles[index].Volume * ((candles[index].Close - LL) - (HH - candles[index].Close)) / (HH != LL ? (HH - LL) : 9999999);

        RangeV.Values[index] = GetWWMA(RangeV, Range, index, _EMAPeriod.ValueInt);

        wwmaVolume.Values[index] = GetWWMAVolume(wwmaVolume, candles, index, _EMAPeriod.ValueInt);

        Tmf.Values[index] = wwmaVolume.Values[index] != 0 ? RangeV.Values[index] / wwmaVolume.Values[index] : 0;

        //  TmfSignal.Values[index] = GetWWMA(TmfSignal, Tmf, index, _SmoothTMF.ValueInt);
    }
}
