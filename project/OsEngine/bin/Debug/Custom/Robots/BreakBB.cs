using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;


[Bot("BreakBB")]
public class BreakBB : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _bbc_long;
    StrategyParameterInt _length_long;
    StrategyParameterString _TwoPoint_long;

    public Aindicator _bbc_short;
    StrategyParameterInt _length_short;
    StrategyParameterString _TwoPoint_short;

    public Aindicator _sma_long;
    StrategyParameterInt _periodSma_long;

    public Aindicator _sma_short;
    StrategyParameterInt _periodSma_short;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakBB(string name, StartProgram startProgram) : base(name, startProgram)
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

        _TwoPoint_long = CreateParameter("Two points channel long", "Max", new[] { "Max", "Min" }, "Robot parameters");
        _TwoPoint_short = CreateParameter("Two points channel short", "Max", new[] { "Max", "Min" }, "Robot parameters");

        _periodSma_long = CreateParameter("SMA period long", 100, 50, 400, 10, "Robot parameters");
        _periodSma_short = CreateParameter("SMA period short", 100, 50, 400, 10, "Robot parameters");
        _length_long = CreateParameter("Length long", 100, 50, 200, 20, "Robot parameters");
        _length_short = CreateParameter("Length short", 100, 50, 200, 20, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");

        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _bbc_long = IndicatorsFactory.CreateIndicatorByName(nameClass: "BBchannel_indicator", name: name + "BBchannelLine_long", canDelete: false);
        _bbc_long = (Aindicator)_tab.CreateCandleIndicator(_bbc_long, nameArea: "Prime");
        _bbc_long.ParametersDigit[0].Value = _length_long.ValueInt;
        ((IndicatorParameterString)_bbc_long.Parameters[0]).ValueString = _TwoPoint_long.ValueString;
        _bbc_long.Save();

        _bbc_short = IndicatorsFactory.CreateIndicatorByName(nameClass: "BBchannel_indicator", name: name + "BBchannelLine_short", canDelete: false);
        _bbc_short = (Aindicator)_tab.CreateCandleIndicator(_bbc_short, nameArea: "Prime");
        _bbc_short.ParametersDigit[0].Value = _length_long.ValueInt;
        ((IndicatorParameterString)_bbc_short.Parameters[0]).ValueString = _TwoPoint_short.ValueString;
        _bbc_short.Save();


        _sma_long = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Long", canDelete: false);
        _sma_long = (Aindicator)_tab.CreateCandleIndicator(_sma_long, nameArea: "Prime");
        _sma_long.ParametersDigit[0].Value = _periodSma_long.ValueInt;
        _sma_long.Save();

        _sma_short = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Short", canDelete: false);
        _sma_short = (Aindicator)_tab.CreateCandleIndicator(_sma_short, nameArea: "Prime");
        _sma_short.ParametersDigit[0].Value = _periodSma_short.ValueInt;
        _sma_short.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += LRegBot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        LRegBot_ParametrsChangeByUser();

    }

    private void LRegBot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_bbc_long.ParametersDigit[0].Value != _length_long.ValueInt
            || ((IndicatorParameterString)_bbc_long.Parameters[0]).ValueString != _TwoPoint_long.ValueString)
        {
            _bbc_long.ParametersDigit[0].Value = _length_long.ValueInt;
            ((IndicatorParameterString)_bbc_long.Parameters[0]).ValueString = _TwoPoint_long.ValueString;
            _bbc_long.Reload();
            _bbc_long.Save();
        }

        if (_bbc_short.ParametersDigit[0].Value != _length_short.ValueInt
            || ((IndicatorParameterString)_bbc_short.Parameters[0]).ValueString != _TwoPoint_short.ValueString)
        {
            _bbc_short.ParametersDigit[0].Value = _length_short.ValueInt;
            ((IndicatorParameterString)_bbc_short.Parameters[0]).ValueString = _TwoPoint_short.ValueString;
            _bbc_short.Reload();
            _bbc_short.Save();
        }

        if (_sma_long.ParametersDigit[0].Value != _periodSma_long.ValueInt)
        {
            _sma_long.ParametersDigit[0].Value = _periodSma_long.ValueInt;
            _sma_long.Reload();
            _sma_long.Save();
        }
        if (_sma_short.ParametersDigit[0].Value != _periodSma_short.ValueInt)
        {
            _sma_short.ParametersDigit[0].Value = _periodSma_short.ValueInt;
            _sma_short.Reload();
            _sma_short.Save();
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
        return "BreakBB";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off") { return; }
        if (_tab.CandlesAll == null) { return; }

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        bool long_ready = false;
        bool short_ready = false;
        if (_length_long.ValueInt < candles.Count && _periodSma_long.ValueInt < candles.Count
            && _bbc_long.DataSeries[0] != null
            && _bbc_long.DataSeries[1] != null)
        {
            long_ready = true;
        }
        if (_length_short.ValueInt < candles.Count && _periodSma_short.ValueInt < candles.Count
            && _bbc_short.DataSeries[0] != null
            && _bbc_short.DataSeries[1] != null)
        {
            short_ready = true;
        }

        if (!long_ready && !short_ready)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;
        decimal lastCandle = candles[candles.Count - 1].Close;

        decimal bb_up_long = 0;
        decimal bb_down_long = 0;
        decimal lastMaFilter_long = 0;
        if (long_ready)
        {
            bb_up_long = _bbc_long.DataSeries[0].Last;
            bb_down_long = _bbc_long.DataSeries[1].Last;
            lastMaFilter_long = _sma_long.DataSeries[0].Last;
        }

        decimal bb_up_short = 0;
        decimal bb_down_short = 0;
        decimal lastMaFilter_short = 0;
        decimal _slippage = 0;
        if (short_ready)
        {
            bb_up_short = _bbc_short.DataSeries[0].Last;
            bb_down_short = _bbc_short.DataSeries[1].Last;
            lastMaFilter_short = _sma_short.DataSeries[0].Last;
        }

        if (bb_down_long <= 0) bb_down_long = lastMaFilter_long;
        if (bb_up_long <= 0) bb_up_long = lastMaFilter_long;
        if (bb_down_short <= 0) bb_down_short = lastMaFilter_short;
        if (bb_up_short <= 0) bb_up_short = lastMaFilter_short;

        if (positions.Count <= 1)
        {// enter logic
            bool have_short_pos = false;
            bool have_long_pos = false;
            if (positions.Count == 1)
            {
                if (positions[0].Direction == Side.Buy)
                {
                    have_long_pos = true;
                }
                else if (positions[0].Direction == Side.Sell)
                {
                    have_short_pos = true;
                }
            }
            if (bb_up_long > lastMaFilter_long
                    && lastCandle <= bb_up_long
                    && long_ready
                    && !have_long_pos)
            {
                _slippage = Slippage.ValueDecimal * bb_up_long / 100;
                if (!BuySignalIsFiltered(candles))
                    _tab.BuyAtStop(GetVolume(), bb_up_long + _slippage, bb_up_long, StopActivateType.HigherOrEqual, 1);

            }
            if (bb_down_short < lastMaFilter_short
                    && lastCandle >= bb_down_short
                    && short_ready
                    && !have_short_pos)
            {
                _slippage = Slippage.ValueDecimal * bb_down_short / 100;
                if (!SellSignalIsFiltered(candles))
                    _tab.SellAtStop(GetVolume(), bb_down_short - _slippage, bb_down_short, StopActivateType.LowerOrEqyal, 1);
            }

            if (lastCandle < lastMaFilter_long || BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (lastCandle > lastMaFilter_short || SellSignalIsFiltered(candles))
            {
                _tab.SellAtStopCancel();
            }
        }

        if (positions.Count > 0)
        {//exit logic
            _tab.BuyAtStopCancel();
            _tab.SellAtStopCancel();
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State != PositionStateType.Open)
                {
                    continue;
                }
                if (positions[i].State == PositionStateType.ClosingFail)
                {
                    _tab.CloseAtMarket(positions[i], positions[i].OpenVolume);
                }

                decimal stop_level = 0;

                if (positions[i].Direction == Side.Buy)
                {// logic to close long position
                    stop_level = bb_down_long < lastMaFilter_long ? bb_down_long : lastMaFilter_long;

                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                }
                else if (positions[i].Direction == Side.Sell)
                {//logic to close short position
                    stop_level = bb_up_short > lastMaFilter_short && bb_up_short > 0 ? bb_up_short : lastMaFilter_short;

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

