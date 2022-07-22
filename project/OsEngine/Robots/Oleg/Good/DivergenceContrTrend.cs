using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("DivergenceContrTrend")]
    public class DivergenceContrTrend : BotPanel
    {
        private BotTabSimple _tab;
        
        private Aindicator _zz;
        private Aindicator _smaFilter;

        private StrategyParameterInt LengthZZ;
        private StrategyParameterString Regime;
        private StrategyParameterDecimal Slippage;
        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;
        private StrategyParameterInt SmaLengthFilter;
        private StrategyParameterDecimal VolumeOnPosition;
        private StrategyParameterString VolumeRegime;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterBool SmaPositionFilterIsOn;
        private StrategyParameterBool SmaSlopeFilterIsOn;

        public DivergenceContrTrend(string name, StartProgram startProgram) : base(name, startProgram)
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

            LengthZZ = CreateParameter("Length ZZ", 50, 50, 200, 20, "Robot parameters");

            SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
            SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
            SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

            _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
            _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
            _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Save();

            _zz = IndicatorsFactory.CreateIndicatorByName(nameClass: "ZigZagIndicator", name: name + "ZigZag", canDelete: false);
            _zz = (Aindicator)_tab.CreateCandleIndicator(_zz, nameArea: "Prime");
            _zz.ParametersDigit[0].Value = LengthZZ.ValueInt;
            _zz.Save();

            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            ParametrsChangeByUser += DivergenceContrTrend_ParametrsChangeByUserEventHandler;
            DivergenceContrTrend_ParametrsChangeByUserEventHandler();
        }

        private void DivergenceContrTrend_ParametrsChangeByUserEventHandler()
        {
            if (_zz.ParametersDigit[0].Value != LengthZZ.ValueInt)
            {
                _zz.ParametersDigit[0].Value = LengthZZ.ValueInt;
                _zz.Reload();
                _zz.Save();
            }

            if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
            {
                _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
                _smaFilter.Reload();
                _smaFilter.Save();
            }

            if (_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
            {
                _smaFilter.DataSeries[0].IsPaint = SmaPositionFilterIsOn.ValueBool;
            }
        }

        public override string GetNameStrategyType()
        {
            return "DivergenceContrTrend";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }
            if (TimeStart.Value > _tab.TimeServerCurrent || TimeEnd.Value < _tab.TimeServerCurrent)
            {
                CancelStopsAndProfits();
                return;
            }
            if (_tab.CandlesAll == null)
            {
                return;
            }
            if (LengthZZ.ValueInt >= candles.Count)
            {
                return;
            }
            if (SmaLengthFilter.ValueInt >= candles.Count)
            {
                return;
            }

            decimal slippage = 0;
            List<Position> positions = _tab.PositionsOpenAll;
            decimal lastCandleClosePrice = candles[candles.Count - 1].Close;
            decimal lastMaFilter = _smaFilter.DataSeries[0].Last;
            decimal zzChannelUp = _zz.DataSeries[4].Last;
            decimal zzChannelDown = _zz.DataSeries[5].Last;

            // _zz.DataSeries[2].Last; - ZigZag Highs
            // _zz.DataSeries[3].Last; - ZigZag Lows

            if (zzChannelDown <= 0 || zzChannelUp <= 0)
            {
                return;
            }

            if (positions.Count == 0)
            {
                if (zzChannelDown > zzChannelUp)
                {
                    return;
                }

                // LONG
                slippage = Slippage.ValueDecimal * zzChannelUp / 100;
                if (!BuySignalIsFiltered(candles))
                {
                    bool alreadyOutOfChannel = lastCandleClosePrice > zzChannelUp + slippage;
                    if (alreadyOutOfChannel)
                    {
                        return;
                    }
                    _tab.BuyAtStop(GetVolume(), zzChannelUp + slippage, zzChannelUp, StopActivateType.HigherOrEqual, 1);
                }

                // SHORT
                slippage = Slippage.ValueDecimal * zzChannelDown / 100;
                if (!SellSignalIsFiltered(candles))
                {
                    bool alreadyOutOfChannel = lastCandleClosePrice < zzChannelDown - slippage;
                    if (alreadyOutOfChannel)
                    {
                        return;
                    }
                    _tab.SellAtStop(GetVolume(), zzChannelDown - slippage, zzChannelDown, StopActivateType.LowerOrEqyal, 1);
                }
            }
            else
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    _tab.BuyAtStopCancel();
                    _tab.SellAtStopCancel();

                    if (positions[i].State == PositionStateType.Open)
                    {
                        decimal stopLevel = 0;
                        if (positions[i].Direction == Side.Buy)
                        {
                            stopLevel = zzChannelDown > lastMaFilter ? zzChannelDown : lastMaFilter;
                            slippage = Slippage.ValueDecimal * stopLevel / 100;
                            _tab.CloseAtTrailingStop(positions[i], stopLevel, stopLevel - slippage);
                        }
                        else if (positions[i].Direction == Side.Sell)
                        {
                            stopLevel = zzChannelUp < lastMaFilter && zzChannelUp > 0 ? zzChannelUp : lastMaFilter;
                            slippage = Slippage.ValueDecimal * stopLevel / 100;
                            _tab.CloseAtTrailingStop(positions[i], stopLevel, stopLevel + slippage);
                        }
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
            decimal lastSma = _smaFilter.DataSeries[0].Last;
            decimal lastPrice = candles[candles.Count - 1].Close;

            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyShort" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
            }

            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastSma > lastPrice)
                {
                    return true;
                }
            }

            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSma < prevSma)
                {
                    return true;
                }
            }

            return false;
        }

        private bool SellSignalIsFiltered(List<Candle> candles)
        {
            decimal lastSma = _smaFilter.DataSeries[0].Last;
            decimal lastPrice = candles[candles.Count - 1].Close;

            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyLong" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
            }

            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastSma < lastPrice)
                {
                    return true;
                }
            }

            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSma > prevSma)
                {
                    return true;
                }
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
}
