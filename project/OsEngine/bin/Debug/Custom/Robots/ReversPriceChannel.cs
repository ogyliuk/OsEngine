using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots;
using System;
using System.Collections.Generic;
using System.Drawing;

[Bot("ReversPriceChannel")]
class ReversPriceChannel : BotPanel
{
    BotTabSimple _tab;

    StrategyParameterString _Regime;

    StrategyParameterDecimal _Slippage;

    StrategyParameterDecimal _VolumeOnPosition;
    StrategyParameterString _VolumeRegime;
    StrategyParameterInt _VolumeDecimals;

    StrategyParameterTimeOfDay _TimeStart;
    StrategyParameterTimeOfDay _TimeEnd;

    StrategyParameterString _OpeningPosTopChannel;

    Aindicator _SmaFilter;
    StrategyParameterBool _SmaPositionFilterIsOn;
    StrategyParameterInt _SmaLengthFilter;
    StrategyParameterBool _SmaSlopeFilterIsOn;

    Aindicator _PriceChannel;
    StrategyParameterInt _PeriodPriceChannelUp;
    StrategyParameterInt _PeriodPriceChannelDown;

    Aindicator _EmaFast;
    StrategyParameterInt _PeriodEmaFast;

    Aindicator _EmaSlow;
    StrategyParameterInt _PeriodEmaSlow;

    StrategyParameterLabel label1;
    StrategyParameterLabel label2;
    StrategyParameterLabel label3;
    StrategyParameterLabel label4;
    StrategyParameterLabel label5;
    StrategyParameterLabel label6;
    StrategyParameterLabel label7;

    decimal lastPrice;
    decimal lastEmaSlow;
    decimal lastEmaFast;
    decimal pcUp;
    decimal pcDown;
    decimal lastHi;
    decimal lastLo;

    public ReversPriceChannel(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        _Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");

        label2 = CreateParameterLabel("label2", "--------", "--------", 10, 5, Color.White, "Base");
        _VolumeRegime = CreateParameter("Volume type", "Contract currency", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
        _VolumeDecimals = CreateParameter("Number of Digits after the decimal point in the volume", 2, 1, 50, 4, "Base");
        _VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");

        label1 = CreateParameterLabel("label1", "--------", "--------", 10, 5, Color.White, "Base");
        _Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

        label3 = CreateParameterLabel("label3", "--------", "--------", 10, 5, Color.White, "Base");
        _TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
        _TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

        label4 = CreateParameterLabel("label4", "--------", "--------", 10, 5, Color.White, "Indicators settings");
        _PeriodPriceChannelUp = CreateParameter("Period Up Price Channel", 55, 1, 50, 4, "Indicators settings");
        _PeriodPriceChannelDown = CreateParameter("Period Down Price Channel", 55, 1, 50, 4, "Indicators settings");        

        label5 = CreateParameterLabel("label5", "--------", "--------", 10, 5, Color.White, "Indicators settings");
        _PeriodEmaSlow = CreateParameter("Period Slow Ema", 350, 1, 50, 4, "Indicators settings");
        _PeriodEmaFast = CreateParameter("Period Fast Ema", 25, 1, 50, 4, "Indicators settings");

        label6 = CreateParameterLabel("label6", "--------", "--------", 10, 5, Color.White, "Base");
        _OpeningPosTopChannel = CreateParameter("Channel top position", "Long", new[] { "Long", "Short" }, "Base");

        _PriceChannel = IndicatorsFactory.CreateIndicatorByName(nameClass: "PriceChannel", name: name + "Price Channel", canDelete: false);
        _PriceChannel = (Aindicator)_tab.CreateCandleIndicator(_PriceChannel, nameArea: "Prime");
        _PriceChannel.DataSeries[0].Color = System.Drawing.Color.Red;
        _PriceChannel.ParametersDigit[0].Value = _PeriodPriceChannelUp.ValueInt;
        _PriceChannel.ParametersDigit[1].Value = _PeriodPriceChannelDown.ValueInt;
        _PriceChannel.Save();

        _EmaFast = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "Ema Fast", canDelete: false);
        _EmaFast = (Aindicator)_tab.CreateCandleIndicator(_EmaFast, nameArea: "Prime");
        _EmaFast.DataSeries[0].Color = System.Drawing.Color.LightGoldenrodYellow;
        _EmaFast.ParametersDigit[0].Value = _PeriodEmaFast.ValueInt;
        _EmaFast.Save();

        _EmaSlow = IndicatorsFactory.CreateIndicatorByName(nameClass: "Ema", name: name + "Ema Slow", canDelete: false);
        _EmaSlow = (Aindicator)_tab.CreateCandleIndicator(_EmaSlow, nameArea: "Prime");
        _EmaSlow.DataSeries[0].Color = System.Drawing.Color.Gold;
        _EmaSlow.ParametersDigit[0].Value = _PeriodEmaSlow.ValueInt;
        _EmaSlow.Save();

        _SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filter parameters");
        _SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filter parameters");
        _SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filter parameters");

        _SmaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _SmaFilter = (Aindicator)_tab.CreateCandleIndicator(_SmaFilter, nameArea: "Prime");
        _SmaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _SmaFilter.ParametersDigit[0].Value = _SmaLengthFilter.ValueInt;
        _SmaFilter.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += Bot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        Bot_ParametrsChangeByUser();

    }

    private void StopOrActivateIndicators()
    {
        if (_SmaPositionFilterIsOn.ValueBool
           != _SmaFilter.IsOn && _SmaSlopeFilterIsOn.ValueBool
           != _SmaFilter.IsOn)
        {
            _SmaFilter.IsOn = _SmaPositionFilterIsOn.ValueBool;
            _SmaFilter.Reload();

            _SmaFilter.IsOn = _SmaSlopeFilterIsOn.ValueBool;
            _SmaFilter.Reload();
        }
    }

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {

        if (_TimeStart.Value > _tab.TimeServerCurrent ||
          _TimeEnd.Value < _tab.TimeServerCurrent)
        {
            return;
        }
        if (_SmaLengthFilter.ValueInt >= candles.Count || _PeriodEmaSlow.ValueInt > candles.Count || _PeriodPriceChannelUp.ValueInt > candles.Count)
        {
            return;
        }
        lastPrice = candles[candles.Count - 1].Close;
        lastHi = candles[candles.Count - 1].High;
        lastLo = candles[candles.Count - 1].Low;
        lastEmaSlow = _EmaSlow.DataSeries[0].Last;
        lastEmaFast = _EmaFast.DataSeries[0].Last;
        pcUp = _PriceChannel.DataSeries[0].Last;
        pcDown = _PriceChannel.DataSeries[1].Last;

        List<Position> positions = _tab.PositionsOpenAll;

        for (int i = 0; i < positions.Count; i++)
        {
            ClosePosition(candles, positions[i]);
        }        

        if (positions == null || positions.Count == 0)
        {
            OpenPosotion(candles);
        }
    }
    private void OpenPosotion(List<Candle> candles)
    {       
        decimal slippage = _Slippage.ValueDecimal * lastPrice / 100;

        if (lastEmaFast > lastEmaSlow)
        {
            if (lastPrice > lastEmaSlow && lastPrice > lastEmaFast)
            {
                if (lastHi >= pcUp)
                {         
                    if (_OpeningPosTopChannel.ValueString.Contains("Long"))
                    {
                        if (BuySignalIsFiltered(candles) == true)
                        {
                            return;
                        }
                        _tab.BuyAtLimit(GetVolume(), lastPrice + slippage);
                    }
                    else
                    {
                        if (SellSignalIsFiltered(candles) == true)
                        {
                            return;
                        }
                        _tab.SellAtLimit(GetVolume(), lastPrice - slippage);
                    }
                        
                }
            }
        }
        if (lastEmaFast < lastEmaSlow)
        {
            if (lastPrice < lastEmaSlow && lastPrice < lastEmaFast)

                if (lastLo <= pcDown)
                {
                   
                    if (_OpeningPosTopChannel.ValueString.Contains("Long"))
                    {
                        if (SellSignalIsFiltered(candles) == true)
                        {
                            return;
                        }
                        _tab.SellAtLimit(GetVolume(), lastPrice - slippage);
                    }
                        
                    else
                    {
                        if (BuySignalIsFiltered(candles) == true)
                        {
                            return;
                        }
                        _tab.BuyAtLimit(GetVolume(), lastPrice + slippage);
                    }
                        
                }
        }
        
    }

    private void ClosePosition(List<Candle> candles, Position position)
    {
        List<Position> positions = _tab.PositionsOpenAll;


        if (positions == null || positions.Count == 0)
        {
            return;
        }
        if (position.State == PositionStateType.Open ||
            position.State == PositionStateType.ClosingFail)
        {

            if (position.CloseActiv == true ||
            (position.CloseOrders != null && position.CloseOrders.Count > 0))
            {
                return;
            }


            if (_OpeningPosTopChannel.ValueString.Contains("Long"))
            {
                Trend(candles, position);
            }
            else
            ContrTrend(candles, position);
        }
    }

    private void Trend(List<Candle> candles, Position position)
    {
        decimal slippage = _Slippage.ValueDecimal * lastPrice / 100;

        if (position.Direction == Side.Buy)
        {
            if (lastEmaFast < lastEmaSlow)
            {
                if (lastPrice < lastEmaSlow && lastPrice < lastEmaFast)
                {
                    if (lastLo <= pcDown)
                    {
                        _tab.CloseAtLimit(position, lastPrice - slippage, position.OpenVolume);

                        if (!SellSignalIsFiltered(candles))
                        {
                            _tab.SellAtLimit(GetVolume(), lastPrice - slippage);
                        }
                    }
                }
            }
        }
        else if (position.Direction == Side.Sell)
        {
            if (lastEmaFast > lastEmaSlow)
            {
                if (lastPrice > lastEmaSlow && lastPrice > lastEmaFast)
                {
                    if (lastHi >= pcUp)
                    {
                        _tab.CloseAtLimit(position, lastPrice + slippage, position.OpenVolume);

                        if (!BuySignalIsFiltered(candles))
                        {
                            _tab.BuyAtLimit(GetVolume(), lastPrice + slippage);
                        }
                    }
                }

            }
        }
    }
    private void ContrTrend(List<Candle> candles, Position position)
    {
        decimal slippage = _Slippage.ValueDecimal * lastPrice / 100;

        if (position.Direction == Side.Sell)
        {
            if (lastEmaFast < lastEmaSlow)
            {
                if (lastPrice < lastEmaSlow && lastPrice < lastEmaFast)
                {
                    if (lastLo <= pcDown)
                    {
                        _tab.CloseAtLimit(position, lastPrice + slippage, position.OpenVolume);

                        if (!BuySignalIsFiltered(candles))
                        {
                            _tab.BuyAtLimit(GetVolume(), lastPrice + slippage);
                        }
                    }
                }
            }
        }
        else if (position.Direction == Side.Buy)
        {
            if (lastEmaFast > lastEmaSlow)
            {
                if (lastPrice > lastEmaSlow && lastPrice > lastEmaFast)
                {
                    if (lastHi >= pcUp)
                    {
                        _tab.CloseAtLimit(position, lastPrice - slippage, position.OpenVolume);

                        if (!SellSignalIsFiltered(candles))
                        {
                            _tab.SellAtLimit(GetVolume(), lastPrice - slippage);
                        }
                    }
                }

            }
        }
    }
    private void Bot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_PriceChannel.ParametersDigit[0].Value != _PeriodPriceChannelUp.ValueInt ||
            _PriceChannel.ParametersDigit[1].Value != _PeriodPriceChannelDown.ValueInt)
        {
            _PriceChannel.ParametersDigit[0].Value = _PeriodPriceChannelUp.ValueInt;
            _PriceChannel.ParametersDigit[1].Value = _PeriodPriceChannelDown.ValueInt;
            _PriceChannel.Reload();
            _PriceChannel.Save();
        }       

        if (_EmaFast.ParametersDigit[0].Value != _PeriodEmaFast.ValueInt)
        {
            _EmaFast.ParametersDigit[0].Value = _PeriodEmaFast.ValueInt;
            _EmaFast.Reload();
            _EmaFast.Save();
        }

        if (_EmaSlow.ParametersDigit[0].Value != _PeriodEmaSlow.ValueInt)
        {
            _EmaSlow.ParametersDigit[0].Value = _PeriodEmaSlow.ValueInt;
            _EmaSlow.Reload();
            _EmaSlow.Save();
        }

        if (_SmaFilter.ParametersDigit[0].Value != _SmaLengthFilter.ValueInt)
        {
            _SmaFilter.ParametersDigit[0].Value = _SmaLengthFilter.ValueInt;
            _SmaFilter.Reload();
            _SmaFilter.Save();            
        }
      
        if (_SmaFilter.DataSeries != null && _SmaFilter.DataSeries.Count > 0)
        {
            if (!_SmaPositionFilterIsOn.ValueBool)
            {
                _SmaFilter.DataSeries[0].IsPaint = false;
            }
            else
            {
                _SmaFilter.DataSeries[0].IsPaint = true;
            }
        }
    }

    #region GetVolume()
    private decimal GetVolume()
    {
        decimal volume = _VolumeOnPosition.ValueDecimal;


        if (_VolumeRegime.ValueString == "Contract currency") // "Валюта контракта"
        {
            decimal contractPrice = TabsSimple[0].PriceBestAsk;
            volume = Math.Round(_VolumeOnPosition.ValueDecimal / contractPrice, _VolumeDecimals.ValueInt);
            return volume;
        }
        else if (_VolumeRegime.ValueString == "Number of contracts")
        {
            return volume;
        }
        else //if (VolumeRegime.ValueString == "% of the total portfolio")
        {
            return Math.Round(_tab.Portfolio.ValueCurrent * (volume / 100) / _tab.PriceBestAsk / _tab.Securiti.Lot, _VolumeDecimals.ValueInt);
        }
    }
    #endregion

    private bool BuySignalIsFiltered(List<Candle> candles)
    {
        // фильтр для покупок

        decimal lastSma = _SmaFilter.DataSeries[0].Last;
        decimal _lastPrice = candles[candles.Count - 1].Close;
        //если режим выкл то возвращаем тру
        if (_Regime.ValueString == "Off" ||
            _Regime.ValueString == "OnlyShort" ||
            _Regime.ValueString == "OnlyClosePosition")
        {
            return true;
        }

        if (_SmaPositionFilterIsOn.ValueBool)
        {
            // если цена ниже последней сма - возвращаем на верх true

            if (_lastPrice < lastSma)
            {
                return true;
            }

        }
        if (_SmaSlopeFilterIsOn.ValueBool)
        {
            // если последняя сма ниже предыдущей сма - возвращаем на верх true            
            decimal previousSma = _SmaFilter.DataSeries[0].Values[_SmaFilter.DataSeries[0].Values.Count - 2]; ///

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
        decimal lastSma = _SmaFilter.DataSeries[0].Last;
        //если режим выкл то возвращаем тру
        if (_Regime.ValueString == "Off" ||
            _Regime.ValueString == "OnlyLong" ||
            _Regime.ValueString == "OnlyClosePosition")
        {
            return true;
        }

        if (_SmaPositionFilterIsOn.ValueBool)
        {
            // если цена выше последней сма - возвращаем на верх true

            if (_lastPrice > lastSma)
            {
                return true;
            }

        }
        if (_SmaSlopeFilterIsOn.ValueBool)
        {
            // если последняя сма выше предыдущей сма - возвращаем на верх true
            decimal previousSma = _SmaFilter.DataSeries[0].Values[_SmaFilter.DataSeries[0].Values.Count - 2];

            if (lastSma > previousSma)
            {
                return true;
            }

        }
        return false;
    }
    public override string GetNameStrategyType()
    {
        return "ReversPriceChannel";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }
}

