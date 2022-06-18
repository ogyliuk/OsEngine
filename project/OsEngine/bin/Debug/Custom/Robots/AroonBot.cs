using OsEngine.Charts.CandleChart.Elements;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;


class AroonBot : BotPanel
{/// <summary>
 /// tab to trade
 /// вкладка для торговли
 /// </summary>
    private BotTabSimple _tab;

    /// <summary>
    /// Macd 
    /// </summary>
    private Aindicator _aroon;

    public StrategyParameterInt _lenghtPeriod;

    //Fields and parameters of drawing lines, separate from the indicator series to save resources
    //Поля и параметры линий прорисовки, отдельно от серий индикатора для экономии ресурсов
    public StrategyParameterDecimal _UpLineParam;
    public StrategyParameterDecimal _DownLineParam;
    public LineHorisontal UpLinePaint;
    public LineHorisontal DownLinePaint;
    //---

    /// <summary>
    ///  Проскальзывание
    /// </summary>
    public StrategyParameterDecimal Slippage;
    /// <summary>
    /// Объем сделки
    /// </summary>
    public StrategyParameterDecimal Volume;
    /// <summary>
    /// Режим работы бота
    /// </summary>
    public StrategyParameterString Regime;


    /// <summary>
    ///Bot constructor executed on initialization Конструктор бота выполняемый при инициализации
    /// </summary>
    /// <param name="name">Name bot Имя бота</param>
    /// <param name="startProgram">the name of the program that launched the class имя программы запустившей класс</param>
    public AroonBot(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
        Volume = CreateParameter("Volume", 1, 1.0m, 50, 4);
        Slippage = CreateParameter("Slippage", 0, 0, 20m, 1);

        _lenghtPeriod = CreateParameter("Period", 14, 14, 100, 10);
        _UpLineParam = CreateParameter("Up Horizontal Line", 70, 50, 100m, 10);
        _DownLineParam = CreateParameter("Down Horizontal Line", 30, 0, 50m, 10);

        _aroon = IndicatorsFactory.CreateIndicatorByName("Aroon", name + "Aroon", false);
        _aroon = (Aindicator)_tab.CreateCandleIndicator(_aroon, "AroonArea");
        _aroon.ParametersDigit[0].Value = _lenghtPeriod.ValueInt;
        _aroon.DataSeries[3].IsPaint = false;
        _aroon.DataSeries[4].IsPaint = false;
        _aroon.Save();

        //drawing horizontal lines on the indicator прорисовка горизонтальных линий на индикаторе
        UpLinePaint = new LineHorisontal("upline", "AroonArea", false)
        {
            Color = Color.Green,
            Value = _UpLineParam.ValueDecimal


        };
        _tab.SetChartElement(UpLinePaint);

        DownLinePaint = new LineHorisontal("downline", "AroonArea", false)
        {
            Color = Color.Yellow,
            Value = _DownLineParam.ValueDecimal

        };
        _tab.SetChartElement(DownLinePaint);

        UpLinePaint.TimeEnd = DateTime.Now;
        DownLinePaint.TimeEnd = DateTime.Now;
        //--

        ParametrsChangeByUser += AroonBot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;


    }
    decimal _lastPrice;
    decimal _lastAroonUpGreen;
    decimal _lastAroonDownRed;
    decimal _slippage;
    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        UpLinePaint.TimeEnd = DateTime.Now;
        DownLinePaint.TimeEnd = DateTime.Now;
        UpLinePaint.Refresh();
        DownLinePaint.Refresh();

        if (Regime.ValueString == "Off")
        {
            return;
        }
        if (_aroon.DataSeries[0].Values == null || candles.Count < _lenghtPeriod.ValueInt)
        {
            return;
        }

        _lastPrice = candles[candles.Count - 1].Close;
        _lastAroonUpGreen = _aroon.DataSeries[0].Last;
        _lastAroonDownRed = _aroon.DataSeries[1].Last;
        _slippage = Slippage.ValueDecimal * _lastPrice / 100;

        List<Position> openPositions = _tab.PositionsOpenAll;

        if (openPositions != null && openPositions.Count != 0)
        {
            for (int i = 0; i < openPositions.Count; i++)
            {
                LogicClosePosition(candles, openPositions[i]);

                UpLinePaint.Refresh();
                DownLinePaint.Refresh();
            }
        }

        if (Regime.ValueString == "OnlyClosePosition")
        {
            return;
        }
        if (openPositions == null || openPositions.Count == 0)
        {
            LogicOpenPosition(candles, openPositions);
        }
    }

    private void LogicOpenPosition(List<Candle> candles, List<Position> openPositions)
    {
        if (_lastAroonUpGreen > UpLinePaint.Value && _lastAroonDownRed < DownLinePaint.Value && Regime.ValueString != "OnlyShort")
        //if (_lastAroonDown > UpLinePaint.Value && _lastAroonUp < DownLinePaint.Value && Regime.ValueString != "OnlyShort")
        {
            _tab.BuyAtLimit(Volume.ValueDecimal, _lastPrice + _slippage);
        }
        if (_lastAroonDownRed > UpLinePaint.Value && _lastAroonUpGreen < DownLinePaint.Value && Regime.ValueString != "OnlyLong")
        //if (_lastAroonUp > UpLinePaint.Value && _lastAroonDown < DownLinePaint.Value && Regime.ValueString != "OnlyLong")
        {
            _tab.SellAtLimit(Volume.ValueDecimal, _lastPrice - _slippage);
        }
    }

    private void LogicClosePosition(List<Candle> candles, Position position)
    {
        if (position.Direction == Side.Buy)
        {
            if (_lastAroonUpGreen <= UpLinePaint.Value)
            //if (_lastAroonDown <= UpLinePaint.Value)
            {
                _tab.CloseAtLimit(position, _lastPrice - _slippage, position.OpenVolume);

                //if (Regime.ValueString != "OnlyLong" && Regime.ValueString != "OnlyClosePosition")
                //{
                //    _tab.SellAtLimit(Volume.ValueDecimal, _lastPrice - _slippage);
                //}
            }
        }

        if (position.Direction == Side.Sell)
        {
            if (_lastAroonDownRed <= UpLinePaint.Value)
            //if (_lastAroonUp <= UpLinePaint.Value)
            {
                _tab.CloseAtLimit(position, _lastPrice + _slippage, position.OpenVolume);

                //if (Regime.ValueString != "OnlyShort" && Regime.ValueString != "OnlyClosePosition")
                //{
                //    _tab.BuyAtLimit(Volume.ValueDecimal, _lastPrice + _slippage);
                //}
            }
        }
    }

    private void AroonBot_ParametrsChangeByUser()
    {
        if (_aroon.ParametersDigit[0].Value != _lenghtPeriod.ValueInt)
        {
            _aroon.ParametersDigit[0].Value = _lenghtPeriod.ValueInt;
            _aroon.Reload();
            _aroon.Save();
        }
        if (UpLinePaint.Value != _UpLineParam.ValueDecimal ||
        DownLinePaint.Value != _DownLineParam.ValueDecimal)
        {
            UpLinePaint.Value = _UpLineParam.ValueDecimal;
            DownLinePaint.Value = _DownLineParam.ValueDecimal;
        }

        UpLinePaint.TimeEnd = DateTime.Now;
        DownLinePaint.TimeEnd = DateTime.Now;
        UpLinePaint.Refresh();
        DownLinePaint.Refresh();
    }

    public override string GetNameStrategyType()
    {
        return "AroonBot";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }
}

