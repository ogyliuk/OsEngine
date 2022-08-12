using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Attributes;

[Bot("BreakKeltnerChannels")]
public class BreakKeltnerChannels : BotPanel
{
    public BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _keltnerChannels;
    public StrategyParameterInt KeltnerPeriod;
    public StrategyParameterDecimal AtrMultiplier;

    public Aindicator _sma;
    public StrategyParameterInt SmaPeriod;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakKeltnerChannels(string name, StartProgram startProgram) : base(name, startProgram)
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

        KeltnerPeriod = CreateParameter("Keltner Period", 14, 3, 50, 1, "Robot parameters");
        AtrMultiplier = CreateParameter("ATR  Multiplier", 1, 1, 10, 0.2m, "Robot parameters");
        SmaPeriod = CreateParameter("SMA Period", 100, 100, 400, 10, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _sma = IndicatorsFactory.CreateIndicatorByName("Sma", name + "Sma", false);
        _sma = (Aindicator)_tab.CreateCandleIndicator(_sma, "Prime");
        _sma.ParametersDigit[0].Value = SmaPeriod.ValueInt;
        _sma.Save();

        _keltnerChannels = IndicatorsFactory.CreateIndicatorByName("KeltnerChannels_indicator", name + "KeltnerChannels", false);
        _keltnerChannels = (Aindicator)_tab.CreateCandleIndicator(_keltnerChannels, "Prime");
        _keltnerChannels.ParametersDigit[0].Value = KeltnerPeriod.ValueInt;
        _keltnerChannels.ParametersDigit[3].Value = AtrMultiplier.ValueDecimal;
        _keltnerChannels.Save();

        _sma.ToString();

        StopOrActivateIndicators();
        ParametrsChangeByUser += KeltnerChannelsBot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        KeltnerChannelsBot_ParametrsChangeByUser();
    }

    private void KeltnerChannelsBot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        _keltnerChannels.ParametersDigit[0].Value = KeltnerPeriod.ValueInt;
        _keltnerChannels.ParametersDigit[3].Value = AtrMultiplier.ValueDecimal;
        _keltnerChannels.Reload();
        _keltnerChannels.Save();
        
        _sma.ParametersDigit[0].Value = SmaPeriod.ValueInt;
        _sma.Reload();
        _sma.Save();


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
        return "BreakKeltnerChannels";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off")
        {
            return;
        }

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

        if (_keltnerChannels.DataSeries[0].Values == null || candles.Count < _keltnerChannels.ParametersDigit[0].Value ||
            candles.Count < SmaPeriod.ValueInt)
        {
            return;
        }

        List<Position> openPositions = _tab.PositionsOpenAll;

        if (openPositions != null && openPositions.Count != 0)
        {
            for (int i = 0; i < openPositions.Count; i++)
            {
                LogicClosePosition(candles, openPositions[i]);
            }
        }

        if (openPositions == null || openPositions.Count == 0)
        {
            LogicOpenPosition(candles, openPositions);
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

    private void LogicOpenPosition(List<Candle> candles, List<Position> position)
    {
        decimal _lastPrice = candles[candles.Count - 1].Close;
        decimal _keltnerUpLast = _keltnerChannels.DataSeries[1].Last;
        decimal _keltnerDownLast = _keltnerChannels.DataSeries[2].Last;
        decimal _smaLast = _sma.DataSeries[0].Last;

        decimal _slippage = Slippage.ValueDecimal * _lastPrice / 100;
        if (_lastPrice > _keltnerUpLast && _keltnerUpLast > _smaLast)
        {
            if (!BuySignalIsFiltered(candles))
                _tab.BuyAtLimit(GetVolume(), _lastPrice + _slippage);
        }

        if (_lastPrice < _keltnerDownLast && _keltnerDownLast < _smaLast)
        {
            if (!SellSignalIsFiltered(candles))
                _tab.SellAtLimit(GetVolume(), _lastPrice - _slippage);
        }

    }

    private void LogicClosePosition(List<Candle> candles, Position position)
    {
        decimal _keltnerMiddleLine = _keltnerChannels.DataSeries[3].Last;
        decimal _smaLast = _sma.DataSeries[0].Last;

        if (position.State == PositionStateType.Closing ||
            position.CloseActiv == true ||
            (position.CloseOrders != null && position.CloseOrders.Count > 0))
        {
            return;
        }

        if (position.Direction == Side.Buy)
        {
            decimal activationPrice = _keltnerMiddleLine > _smaLast ? _keltnerMiddleLine : _smaLast;

            decimal _slippage = Slippage.ValueDecimal * activationPrice / 100;
            _tab.CloseAtStop(position, activationPrice, activationPrice - _slippage);
        }

        if (position.Direction == Side.Sell)
        {
            decimal activationPrice = _keltnerMiddleLine < _smaLast ? _keltnerMiddleLine : _smaLast;

            decimal _slippage = Slippage.ValueDecimal * activationPrice / 100;
            _tab.CloseAtStop(position, activationPrice, activationPrice + _slippage);
        }
    }

    // логика

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

