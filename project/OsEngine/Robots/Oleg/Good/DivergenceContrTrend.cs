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

        private IndicatorDataSeries ZigZagHighPeaks { get { return this._zz.DataSeries[2]; } }
        private IndicatorDataSeries ZigZagLowPeaks { get { return this._zz.DataSeries[3]; } }

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

            decimal currentCandleHighPeakPrice = GetCurrentCandleHighPeakPrice();
            decimal currentCandleLowPeakPrice = GetCurrentCandleLowPeakPrice();
            bool currentCandleHighPeakCandidate = currentCandleHighPeakPrice > 0;
            bool currentCandleLowPeakCandidate = currentCandleLowPeakPrice > 0;
            if (!currentCandleHighPeakCandidate && !currentCandleLowPeakCandidate)
            {
                return;
            }

            decimal slippage = 0;
            if (_tab.PositionsOpenAll.Count == 0)
            {
                decimal lastCandleClosePrice = candles[candles.Count - 1].Close;
                slippage = Slippage.ValueDecimal * lastCandleClosePrice / 100;

                // LONG
                if (currentCandleHighPeakCandidate && !BuySignalIsFiltered(candles))
                {
                    int lastCompletedHighPeakIndex = GetLastCompletedHighPeakIndex();
                    if (lastCompletedHighPeakIndex != -1)
                    {
                        decimal lastCompletedHighPeakPrice = ZigZagHighPeaks.Values[lastCompletedHighPeakIndex];
                        bool higherHigh = currentCandleHighPeakPrice > lastCompletedHighPeakPrice;
                        if (higherHigh)
                        {
                            _tab.BuyAtStop(GetVolume(), lastCandleClosePrice + slippage, lastCandleClosePrice, StopActivateType.HigherOrEqual, 1);
                        }
                    }
                }

                // SHORT
                if (currentCandleLowPeakCandidate && !SellSignalIsFiltered(candles))
                {
                    int lastCompletedLowPeakIndex = GetLastCompletedLowPeakIndex();
                    if (lastCompletedLowPeakIndex != -1)
                    {
                        decimal lastCompletedLowPeakPrice = ZigZagLowPeaks.Values[lastCompletedLowPeakIndex];
                        bool lowerLow = currentCandleLowPeakPrice < lastCompletedLowPeakPrice;
                        if (lowerLow)
                        {
                            _tab.SellAtStop(GetVolume(), lastCandleClosePrice - slippage, lastCandleClosePrice, StopActivateType.LowerOrEqyal, 1);
                        }
                    }
                }
            }
            else
            {
                foreach (Position position in _tab.PositionsOpenAll)
                {
                    _tab.BuyAtStopCancel();
                    _tab.SellAtStopCancel();

                    if (position.State == PositionStateType.Open)
                    {
                        decimal lastSmaPrice = _smaFilter.DataSeries[0].Last;
                        slippage = Slippage.ValueDecimal * lastSmaPrice / 100;
                        if (position.Direction == Side.Buy)
                        {
                            _tab.CloseAtTrailingStop(position, lastSmaPrice, lastSmaPrice - slippage);
                        }
                        else if (position.Direction == Side.Sell)
                        {
                            _tab.CloseAtTrailingStop(position, lastSmaPrice, lastSmaPrice + slippage);
                        }
                    }
                }
            }
        }

        private decimal GetCurrentCandleHighPeakPrice()
        {
            return ZigZagHighPeaks.Last;
        }

        private decimal GetCurrentCandleLowPeakPrice()
        {
            return ZigZagLowPeaks.Last;
        }

        private int GetLastCompletedHighPeakIndex()
        {
            return GetSecondPositiveElementIndexFromTale(ZigZagHighPeaks.Values);
        }

        private int GetLastCompletedLowPeakIndex()
        {
            return GetSecondPositiveElementIndexFromTale(ZigZagLowPeaks.Values);
        }

        private int GetSecondPositiveElementIndexFromTale(List<decimal> elements)
        {
            int secondPositiveElementIndexFromTale = -1;
            if (elements != null && elements.Count > 0)
            {
                bool firstPositiveElementIndexFromTheTaleFound = false;
                for (int i = elements.Count - 1; i >= 0; i--)
                {
                    if (elements[i] > 0)
                    {
                        if (firstPositiveElementIndexFromTheTaleFound)
                        {
                            secondPositiveElementIndexFromTale = i;
                            break;
                        }
                        else
                        {
                            firstPositiveElementIndexFromTheTaleFound = true;
                        }
                    }
                }
            }
            return secondPositiveElementIndexFromTale;
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
            decimal lastSmaPrice = _smaFilter.DataSeries[0].Last;
            decimal lastCandleClosePrice = candles[candles.Count - 1].Close;

            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyShort" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
            }

            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastCandleClosePrice < lastSmaPrice)
                {
                    return true;
                }
            }

            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSmaPrice = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSmaPrice < prevSmaPrice)
                {
                    return true;
                }
            }

            return false;
        }

        private bool SellSignalIsFiltered(List<Candle> candles)
        {
            decimal lastSmaPrice = _smaFilter.DataSeries[0].Last;
            decimal lastCandleClosePrice = candles[candles.Count - 1].Close;

            if (Regime.ValueString == "Off" ||
                Regime.ValueString == "OnlyLong" ||
                Regime.ValueString == "OnlyClosePosition")
            {
                return true;
            }

            if (SmaPositionFilterIsOn.ValueBool)
            {
                if (lastCandleClosePrice > lastSmaPrice)
                {
                    return true;
                }
            }

            if (SmaSlopeFilterIsOn.ValueBool)
            {
                decimal prevSmaPrice = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];
                if (lastSmaPrice > prevSmaPrice)
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
