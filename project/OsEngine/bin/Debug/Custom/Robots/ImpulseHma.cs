using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using System.Collections.Generic;
using System.Drawing;
using System;
using OsEngine.OsTrader.Panels.Attributes;


[Bot("ImpulseHma")]
public class ImpulseHma : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _Sma;
    public StrategyParameterInt _periodSma;

    public Aindicator _hma;
    public StrategyParameterInt _periodHma;

    public Aindicator _hma2;
    public StrategyParameterInt _periodHma2;

    public Aindicator _atr;
    public StrategyParameterInt _periodAtr;
    public StrategyParameterDecimal _multiplerAtr;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public ImpulseHma(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
        VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
        VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
        VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");
        Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

        TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
        TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

        _periodSma = CreateParameter("SMA period", 500, 100, 1000, 100, "Robot parameters");
        _periodHma = CreateParameter("HMA period", 500, 100, 1000, 100, "Robot parameters");
        _periodHma2 = CreateParameter("HMA2 period", 150, 50, 500, 100, "Robot parameters");
        _periodAtr = CreateParameter("Atr period", 14, 5, 50, 5, "Robot parameters");
        _multiplerAtr = CreateParameter("Atr multipler", 1m, 0.1m, 5.0m, 0.5m, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filters");

        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _Sma = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma", canDelete: false);
        _Sma = (Aindicator)_tab.CreateCandleIndicator(_Sma, nameArea: "Prime");
        _Sma.ParametersDigit[0].Value = _periodSma.ValueInt;
        _Sma.DataSeries[0].Color = Color.Green;
        _Sma.Save();

        _hma = IndicatorsFactory.CreateIndicatorByName("HMA_indicator", name: name + "HMA", canDelete: false);
        _hma = (Aindicator)_tab.CreateCandleIndicator(_hma, nameArea: "Prime");
        _hma.ParametersDigit[0].Value = _periodHma.ValueInt;
        _hma.DataSeries[0].Color = Color.Red;
        _hma.Save();

        _hma2 = IndicatorsFactory.CreateIndicatorByName("HMA_indicator", name: name + "HMA2", canDelete: false);
        _hma2 = (Aindicator)_tab.CreateCandleIndicator(_hma2, nameArea: "Prime");
        _hma2.ParametersDigit[0].Value = _periodHma2.ValueInt;
        _hma2.DataSeries[0].Color = Color.Blue;
        _hma2.Save();

        _atr = IndicatorsFactory.CreateIndicatorByName(nameClass: "ATR", name: name + "ATR", canDelete: false);
        _atr = (Aindicator)_tab.CreateCandleIndicator(_atr, nameArea: "New1");
        _atr.ParametersDigit[0].Value = _periodAtr.ValueInt;
        _atr.Save();

        StopOrActivateIndicators();
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        ParametrsChangeByUser += LRegBot_ParametrsChangeByUser;
        LRegBot_ParametrsChangeByUser();
    }

    private void LRegBot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_Sma.ParametersDigit[0].Value != _periodSma.ValueInt)
        {
            _Sma.ParametersDigit[0].Value = _periodSma.ValueInt;
            _Sma.Reload();
            _Sma.Save();
        }

        if (_hma.ParametersDigit[0].Value != _periodHma.ValueInt)
        {
            _hma.ParametersDigit[0].Value = _periodHma.ValueInt;
            _hma.Reload();
            _hma.Save();
        }

        if (_hma2.ParametersDigit[0].Value != _periodHma2.ValueInt)
        {
            _hma2.ParametersDigit[0].Value = _periodHma2.ValueInt;
            _hma2.Reload();
            _hma2.Save();
        }

        if (_atr.ParametersDigit[0].Value != _periodAtr.ValueInt)
        {
            _atr.ParametersDigit[0].Value = _periodAtr.ValueInt;
            _atr.Reload();
            _atr.Save();
        }

        if (_smaFilter.DataSeries.Count == 0)
        {
            return;
        }
        if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
        {
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Reload();
            _smaFilter.Save();
        }


        if (_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
        {
            if (!SmaPositionFilterIsOn.ValueBool)
            {
                _smaFilter.DataSeries[0].IsPaint = false;
            }
            else
            {
                _smaFilter.DataSeries[0].IsPaint = true;
            }
        }

    }

    private void StopOrActivateIndicators()
    {

        if (SmaPositionFilterIsOn.ValueBool
           != _smaFilter.IsOn && SmaSlopeFilterIsOn.ValueBool
           != _smaFilter.IsOn)
        {
            _smaFilter.IsOn = SmaPositionFilterIsOn.ValueBool;
            _smaFilter.Reload();

            _smaFilter.IsOn = SmaSlopeFilterIsOn.ValueBool;
            _smaFilter.Reload();
        }
    }

    public override string GetNameStrategyType()
    {
        return "ImpulseHma";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off") { return; }

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (_tab.CandlesAll == null) { return; }
        if (_periodSma.ValueInt > candles.Count || _periodAtr.ValueInt > candles.Count) { return; }
        if (_periodHma.ValueInt > candles.Count || _periodHma2.ValueInt > candles.Count) { return; }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;

        DateTime lastCandleDate = candles[candles.Count - 1].TimeStart;
        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal prewPrice = candles[candles.Count - 2].Close;
        decimal lastSma = _Sma.DataSeries[0].Last;
        decimal prewSma = _Sma.DataSeries[0].Values[candles.Count - 2];
        decimal prew2Sma = _Sma.DataSeries[0].Values[candles.Count - 3];
        decimal lastHma = _hma.DataSeries[0].Last;
        decimal prewHma = _hma.DataSeries[0].Values[candles.Count - 2];
        decimal prew2Hma = _hma.DataSeries[0].Values[candles.Count - 3];
        decimal lastFHma = _hma.DataSeries[1].Last;
        decimal lastHma2 = _hma2.DataSeries[0].Last;
        decimal prewHma2 = _hma2.DataSeries[0].Values[candles.Count - 2];
        decimal prew2Hma2 = _hma2.DataSeries[0].Values[candles.Count - 3];
        decimal lastFHma2 = _hma2.DataSeries[1].Last;
        decimal lastAtr = _atr.DataSeries[0].Last;
        decimal _slippage = 0;
        if (positions.Count == 0 && Regime.ValueString != "OnlyClosePosition")
        {// enter logic
            if (!BuySignalIsFiltered(candles))
            {
                _slippage = Slippage.ValueDecimal * (lastHma + lastAtr * _multiplerAtr.ValueDecimal) / 100;
                _tab.BuyAtStop(GetVolume(), (lastHma + lastAtr * _multiplerAtr.ValueDecimal) + _slippage, lastHma + lastAtr * _multiplerAtr.ValueDecimal, StopActivateType.HigherOrEqual, 1);
            }
            if (!SellSignalIsFiltered(candles))
            {
                _slippage = Slippage.ValueDecimal * (lastHma - lastAtr * _multiplerAtr.ValueDecimal) / 100;
                _tab.SellAtStop(GetVolume(), (lastHma - lastAtr * _multiplerAtr.ValueDecimal) - _slippage, lastHma - lastAtr * _multiplerAtr.ValueDecimal, StopActivateType.LowerOrEqyal, 1);
            }

            if (BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (SellSignalIsFiltered(candles))
            {
                _tab.SellAtStopCancel();
            }

            // младшая HMA растет медленней чем старшая HMA
            if (lastPrice < lastSma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter1 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // младшая HMA снижается медленней чем старшая HMA
            if (lastPrice > lastSma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter1 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // младшая HMA растет медленней чем старшая HMA
            if (lastPrice < lastSma && Math.Abs(prewHma - prew2Hma) < Math.Abs(prewHma2 - prew2Hma2))
            {
                //Console.WriteLine("filter2 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // младшая HMA снижается медленней чем старшая HMA
            if (lastPrice > lastSma && Math.Abs(prewHma - prew2Hma) < Math.Abs(prewHma2 - prew2Hma2))
            {
                //Console.WriteLine("filter2 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // SMA снижается набирая скорость
            if (prewSma > lastSma && Math.Abs(prewSma - lastSma) > Math.Abs(prew2Sma - prewSma))
            {
                //Console.WriteLine("filter3 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // SMA растет набирая скорость
            if (prewSma < lastSma && Math.Abs(lastSma - prewSma) > Math.Abs(prewSma - prew2Sma))
            {
                //Console.WriteLine("filter3 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // закрытие ниже быстрой HMA, которая 'растет' хуже медленной HMA 
            if (lastPrice < lastHma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter4 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // закрытие выше быстрой HMA, которая 'растет' хуже медленной HMA 
            if (lastPrice > lastHma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter4 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // младшая HMA ниже SMA и замедляется
            if (lastHma < lastSma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter5 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }

            // младшая HMA выше SMA и замедляется
            if (lastHma > lastSma && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter5 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            //
            if (lastHma < prewHma)
            {
                //Console.WriteLine("filter6 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            if (lastHma > prewHma)
            {
                //Console.WriteLine("filter6 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            if (lastFHma < lastHma)
            {
                //Console.WriteLine("filter7 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            if (lastFHma > lastHma)
            {
                //Console.WriteLine("filter7 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            if (lastHma2 < lastSma)
            {
                //Console.WriteLine("filter8 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            if (lastHma2 > lastSma)
            {
                //Console.WriteLine("filter8 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            if (lastHma2 < prewHma2)
            {
                //Console.WriteLine("filter9 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            if (lastHma2 > prewHma2)
            {
                //Console.WriteLine("filter9 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            if (lastFHma2 < lastHma2)
            {
                //Console.WriteLine("filter10 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            if (lastFHma2 > lastHma2)
            {
                //Console.WriteLine("filter10 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // Младшая HMA ниже старшей HMA и младшая HMA медленней SMA
            if (lastHma < lastHma2 && Math.Abs(lastHma - prewHma) < Math.Abs(lastSma - prewSma))
            {
                //Console.WriteLine("filter11 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // Младшая HMA выше старшей HMA и младшая HMA медленней SMA
            if (lastHma > lastHma2 && Math.Abs(lastHma - prewHma) < Math.Abs(lastSma - prewSma))
            {
                //Console.WriteLine("filter11 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // Младшая HMA ниже старшей HMA и старшая HMA медленней SMA
            if (lastHma < lastHma2 && Math.Abs(lastHma2 - prewHma2) < Math.Abs(lastSma - prewSma))
            {
                //Console.WriteLine("filter12 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // Младшая HMA выше старшей HMA и старшая HMA медленней SMA
            if (lastHma > lastHma2 && Math.Abs(lastHma2 - prewHma2) < Math.Abs(lastSma - prewSma))
            {
                //Console.WriteLine("filter12 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

            // Младшая HMA ниже старшей HMA и младшая HMA медленней старшей HMA
            if (lastHma < lastHma2 && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter13 " + lastCandleDate);
                _tab.BuyAtStopCancel();
            }
            // Младшая HMA выше старшей HMA и младшая HMA медленней старшей HMA
            if (lastHma > lastHma2 && Math.Abs(lastHma - prewHma) < Math.Abs(lastHma2 - prewHma2))
            {
                //Console.WriteLine("filter13 " + lastCandleDate);
                _tab.SellAtStopCancel();
            }

        }
        else
        {//exit logic
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.ClosingFail)
                {
                    _tab.CloseAtMarket(positions[i], positions[i].OpenVolume);
                    continue;
                }
                if (positions[i].State != PositionStateType.Open)
                {
                    continue;
                }

                decimal stop_level = 0;

                if (positions[i].Direction == Side.Buy)
                {// logic to close long position

                    stop_level = lastHma < lastHma2 ? lastHma - lastAtr * _multiplerAtr.ValueDecimal : lastHma2 > lastSma ? lastHma2 - lastAtr * _multiplerAtr.ValueDecimal : lastSma;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                }
                else if (positions[i].Direction == Side.Sell)
                {//logic to close short position

                    stop_level = lastHma > lastHma2 ? lastHma + lastAtr * _multiplerAtr.ValueDecimal : lastHma2 < lastSma ? lastHma2 + lastAtr * _multiplerAtr.ValueDecimal : lastSma;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage);
                }
            }
        }
    }

    private void CancelStopsAndProfits()
    {
        List<Position> positions = _tab.PositionsOpenAll;

        for (int i = 0; i < positions.Count; i++)
        {
            Position pos = positions[i];

            pos.StopOrderIsActiv = false;
            pos.ProfitOrderIsActiv = false;
        }

        _tab.BuyAtStopCancel();
        _tab.SellAtStopCancel();
    }

    private bool BuySignalIsFiltered(List<Candle> candles)
    {

        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _smaFilter.DataSeries[0].Last;
        // фильтр для покупок
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyShort" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
            //если режим работы робота не соответсвует направлению позициивозвращаем на верх true
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            if (_smaFilter.DataSeries[0].Last > lastPrice)
            {
                return true;
            }
            // если цена ниже последней сма - возвращаем на верх true
        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];

            if (lastSma < prevSma)
            {
                return true;
            }
            // если последняя сма ниже предыдущей сма - возвращаем на верх true
        }

        return false;
    }

    private bool SellSignalIsFiltered(List<Candle> candles)
    {
        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _smaFilter.DataSeries[0].Last;
        // фильтр для продаж
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyLong" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
            //если режим работы робота не соответсвует направлению позиции
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            if (lastSma < lastPrice)
            {
                return true;
            }
            // если цена выше последней сма - возвращаем на верх true
        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];

            if (lastSma > prevSma)
            {
                return true;
            }
            // если последняя сма выше предыдущей сма - возвращаем на верх true
        }

        return false;
    }

    private decimal GetVolume()
    {
        decimal volume = VolumeOnPosition.ValueDecimal;

        if (VolumeRegime.ValueString == "Contract currency") // "Валюта контракта"
        {
            decimal contractPrice = TabsSimple[0].PriceBestAsk;
            volume = Math.Round(VolumeOnPosition.ValueDecimal / contractPrice, VolumeDecimals.ValueInt);
            return volume;
        }
        else if (VolumeRegime.ValueString == "Number of contracts")
        {
            return volume;
        }
        else //if (VolumeRegime.ValueString == "% of the total portfolio")
        {
            return Math.Round(_tab.Portfolio.ValueCurrent * (volume / 100) / _tab.PriceBestAsk / _tab.Securiti.Lot, VolumeDecimals.ValueInt);
        }
    }
}

