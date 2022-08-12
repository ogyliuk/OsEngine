using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using System;

[Bot("BreakAdaptiveBollinger")]
public class BreakAdaptiveBollinger : BotPanel
{
    private BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;

    public StrategyParameterInt Day;
    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public StrategyParameterBool Mode;  

    public Aindicator _bollingerAdaptiveRsi;
    public StrategyParameterInt LookBack;
    public StrategyParameterInt RsiPeriod;

    public Aindicator _moving;
    public StrategyParameterInt MovingPeriod;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakAdaptiveBollinger(string name, StartProgram startProgram)
        : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];
        
        
        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
        VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
        VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
        VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");

        TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
        TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

        Mode = CreateParameter("Use Adaptiv Rsi", true, "Robot parameters");

        Day = CreateParameter("Exit after", 48, 10, 20, 1, "Robot parameters");       

        MovingPeriod = CreateParameter("Sma Period", 150, 5, 250, 10, "Robot parameters");

        RsiPeriod = CreateParameter("Rsi Period", 11, 5, 50, 1, "Robot parameters");
        LookBack = CreateParameter("Nr. of swings", 4, 5, 20, 1, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _bollingerAdaptiveRsi = IndicatorsFactory.CreateIndicatorByName("BollingerAdaptiveRsi_Indicator", name + "Bollinger", false);
        _bollingerAdaptiveRsi = (Aindicator)_tab.CreateCandleIndicator(_bollingerAdaptiveRsi, "alArea");
        _bollingerAdaptiveRsi.ParametersDigit[0].Value = RsiPeriod.ValueInt;
        _bollingerAdaptiveRsi.ParametersDigit[1].Value = LookBack.ValueInt;
        ((IndicatorParameterBool)_bollingerAdaptiveRsi.Parameters[2]).ValueBool = Mode.ValueBool;
        _bollingerAdaptiveRsi.Save();

        _moving = IndicatorsFactory.CreateIndicatorByName("Sma", name + "Sma", false);
        _moving = (Aindicator)_tab.CreateCandleIndicator(_moving, "Prime");
        _moving.ParametersDigit[0].Value = MovingPeriod.ValueInt;
        _moving.Save();

        StopOrActivateIndicators();
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

        ParametrsChangeByUser += AdaptiveRsi_Param_ParametrsChangeByUser;
        AdaptiveRsi_Param_ParametrsChangeByUser();
    }

    private void AdaptiveRsi_Param_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        _bollingerAdaptiveRsi.ParametersDigit[0].Value = RsiPeriod.ValueInt;
        _bollingerAdaptiveRsi.ParametersDigit[1].Value = LookBack.ValueInt;
        ((IndicatorParameterBool)_bollingerAdaptiveRsi.Parameters[2]).ValueBool = Mode.ValueBool;
        _bollingerAdaptiveRsi.Save();
        _bollingerAdaptiveRsi.Reload();
       
        if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
        {
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Reload();
            _smaFilter.Save();
        }

        if(_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
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
        return "BreakAdaptiveBollinger";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (candles.Count < 150)
        {
            return;
        }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        if (_bollingerAdaptiveRsi == null ||
            _bollingerAdaptiveRsi.DataSeries[0] == null ||
            _bollingerAdaptiveRsi.DataSeries[1] == null)
        {
            return;
        }

        if (_bollingerAdaptiveRsi.DataSeries[0].Last == 0 ||
            _bollingerAdaptiveRsi.DataSeries[1].Last == 0)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;

        if (positions.Count == 0)
        {
            TryOpenPosition(candles);
        }
        else
        {
            TryClosePosition(positions[0], candles);
        }
    }

    private bool BuySignalIsFiltered(List<Candle> candles)
    {
        // фильтр для покупок

        decimal lastSma = _smaFilter.DataSeries[0].Last;
        decimal _lastPrice = candles[candles.Count - 1].Close;
        //если режим выкл то возвращаем тру
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyShort" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            // если цена ниже последней сма - возвращаем на верх true

            if (_lastPrice < lastSma)
            {
                return true;
            }

        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            // если последняя сма ниже предыдущей сма - возвращаем на верх true            
            decimal previousSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2]; ///

            if (lastSma < previousSma)
            {
                return true;
            }
        }

        return false;
    }

    private bool SellSignalIsFiltered(List<Candle> candles)
    {
        // фильтр для шорта
        decimal _lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _smaFilter.DataSeries[0].Last;
        //если режим выкл то возвращаем тру
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyLong" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            // если цена выше последней сма - возвращаем на верх true
            
            if (_lastPrice > lastSma)
            {
                return true;
            }

        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            // если последняя сма выше предыдущей сма - возвращаем на верх true
            
            decimal previousSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2]; 

            if (lastSma > previousSma)
            {
                return true;
            }
        }

        return false;
    }

    private void TryOpenPosition(List<Candle> candles)
    {

        List<decimal> upChanel = _bollingerAdaptiveRsi.DataSeries[0].Values;
        List<decimal> downChanel = _bollingerAdaptiveRsi.DataSeries[1].Values;

        if (_moving == null || _moving.DataSeries[0] == null)
        {
            return;
        }

        decimal ma = _moving.DataSeries[0].Last;
        List<decimal> rsi = _bollingerAdaptiveRsi.DataSeries[2].Values;

        if (Mode.ValueBool)
        {
            rsi = _bollingerAdaptiveRsi.DataSeries[3].Values;
        }

        bool uptrend = (candles[candles.Count - 1].Close > (ma) && (rsi[rsi.Count - 1] > 0));
        bool downtrend = (candles[candles.Count - 1].Close < (ma) && (rsi[rsi.Count - 1] > 0));

        if (uptrend & CrossOver(candles.Count - 1, rsi, downChanel))
        {// Buy
            if (BuySignalIsFiltered(candles) == true)
            {
                return;
            }
            _tab.BuyAtMarket(GetVolume());
        }

        if (downtrend & CrossUnder(candles.Count - 1, rsi, upChanel))
        {// Short
            if (SellSignalIsFiltered(candles) == true)
            {
                return;
            }

            _tab.SellAtMarket(GetVolume());
        }
    }

    private void TryClosePosition(Position position, List<Candle> candles)
    {
        int inPos = 0;

        for (int i = candles.Count - 1; i > -1; i--)
        {
            if (candles[i].TimeStart > position.TimeCreate)
            {
                inPos++;
            }
            else
            {
                break;
            }
        }

        if (inPos <= Day.ValueInt - 2)
        {
            return;
        }

        // БАЙ
        if (position.Direction == Side.Sell &&
            position.CloseActiv == false)
        {
            _tab.CloseAtMarket(position, position.OpenVolume);
        }

        // СЕЛЛ
        if (position.Direction == Side.Buy &&
            position.CloseActiv == false)
        {
            _tab.CloseAtMarket(position, position.OpenVolume);
        }
    }

    private bool CrossOver(int index, List<decimal> valuesOne, List<decimal> valuesTwo)
    {
        if (valuesOne[index - 1] <= valuesTwo[index - 1] &&
            valuesOne[index] > valuesTwo[index])
        {
            return true;
        }

        return false;
    }

    private bool CrossUnder(int index, List<decimal> valuesOne, List<decimal> valuesTwo)
    {
        if (valuesOne[index - 1] >= valuesTwo[index - 1] &&
            valuesOne[index] < valuesTwo[index])
        {
            return true;
        }

        return false;
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

