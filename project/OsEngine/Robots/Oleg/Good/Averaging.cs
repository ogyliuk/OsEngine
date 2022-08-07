using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("Averaging")]
    public class Averaging : BotPanel
    {
        private static readonly decimal FEE_PERCENTS = 0.04m;
        private static readonly decimal MIN_PROFIT_PERCENTS = 0.08m;
        private static readonly decimal AVERAGING_THRESHOLD_PERCENTS = 0.08m;

        private BotTabSimple _tab;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal VolumeFirstEntry;
        private StrategyParameterString VolumeMode;
        private StrategyParameterInt VolumeDecimals;

        private StrategyParameterDecimal MinProfitInPercents;

        public Averaging(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
            VolumeMode = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
            VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
            VolumeFirstEntry = CreateParameter("Volume", 1, 1m, 10, 1, "Base");

            MinProfitInPercents = CreateParameter("Min PROFIT %", 0.1m, 0.1m, 1, 0.05m, "Base");

            _tab.CandleFinishedEvent += _tab_CandleFinishedEventHandler;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpenEventHandler;

            ParametrsChangeByUser += ParametersChangeByUserEventHandler;
            ParametersChangeByUserEventHandler();
        }

        public override string GetNameStrategyType() { return "Averaging"; }

        public override void ShowIndividualSettingsDialog() { }

        private void ParametersChangeByUserEventHandler() { }

        private void _tab_CandleFinishedEventHandler(List<Candle> candles)
        {
            if (Regime.ValueString == "Off" || _tab.CandlesAll == null || _tab.CandlesAll.Count < 2)
            {
                return;
            }

            Candle candle = candles.Last();
            Candle previousCandle = candles[candles.Count - 2];
            bool candleColorSwitched = candle.IsUp != previousCandle.IsUp;
            bool longTradingEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyLong";
            bool shortTradingEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyShort";

            if (candleColorSwitched)
            {
                List<Position> positionsLong = GetPositions_LONG();
                List<Position> positionsShort = GetPositions_SHORT();

                if (candle.IsUp)
                {
                    if (shortTradingEnabled && positionsShort.Count == 1)
                    {
                        Position position = positionsShort.First();
                        decimal volume = position.OpenVolume;
                        decimal entryPrice = position.EntryPrice;
                        decimal profit = CalcPositionRevenue_SHORT(volume, entryPrice, candle.Close, FEE_PERCENTS);
                        if (profit > 0)
                        {
                            decimal moneyIn = CalcTradeMoney(volume, entryPrice, FEE_PERCENTS, Side.Sell);
                            decimal wantedProfit = moneyIn * (100 + MIN_PROFIT_PERCENTS) / 100 - moneyIn;
                            decimal minTakeProfitPrice = CalcPrice_SHORT_TP_TakeWantedProfit(volume, entryPrice, wantedProfit, FEE_PERCENTS);
                            decimal neededProfit = CalcPositionRevenue_SHORT(volume, entryPrice, minTakeProfitPrice, FEE_PERCENTS);
                            if (profit >= neededProfit)
                            {
                                _tab.CloseAtMarket(position, volume);
                            }
                        }
                    }

                    if (longTradingEnabled)
                    {
                        if (positionsLong.Any())
                        {
                            Position lastPosition = positionsLong.Last();
                            decimal currentLoss = CalcPositionRevenue_LONG(lastPosition.OpenVolume, lastPosition.EntryPrice, candle.Close, FEE_PERCENTS);
                            if (currentLoss < 0)
                            {
                                decimal moneyIn = CalcTradeMoney(lastPosition.OpenVolume, lastPosition.EntryPrice, FEE_PERCENTS, Side.Buy);
                                decimal maxAllowedLoss = -(moneyIn * (100 + AVERAGING_THRESHOLD_PERCENTS) / 100 - moneyIn);
                                if (Math.Abs(currentLoss) > Math.Abs(maxAllowedLoss))
                                {
                                    decimal newAveragingPositionVolume = positionsLong.Count == 1 ?
                                        lastPosition.OpenVolume :
                                        lastPosition.OpenVolume + positionsLong[positionsLong.Count - 2].OpenVolume;
                                    _tab.BuyAtMarket(newAveragingPositionVolume);
                                }
                            }
                        }
                        else
                        {
                            _tab.BuyAtMarket(GetVolume());
                        }
                    }
                }
                else if (candle.IsDown)
                {
                    if (longTradingEnabled && positionsLong.Count == 1)
                    {
                        Position position = positionsLong.First();
                        decimal volume = position.OpenVolume;
                        decimal entryPrice = position.EntryPrice;
                        decimal profit = CalcPositionRevenue_LONG(volume, entryPrice, candle.Close, FEE_PERCENTS);
                        if (profit > 0)
                        {
                            decimal moneyIn = CalcTradeMoney(volume, entryPrice, FEE_PERCENTS, Side.Buy);
                            decimal wantedProfit = moneyIn * (100 + MIN_PROFIT_PERCENTS) / 100 - moneyIn;
                            decimal minTakeProfitPrice = CalcPrice_LONG_TP_TakeWantedProfit(volume, entryPrice, wantedProfit, FEE_PERCENTS);
                            decimal neededProfit = CalcPositionRevenue_LONG(volume, entryPrice, minTakeProfitPrice, FEE_PERCENTS);
                            if (profit >= neededProfit)
                            {
                                _tab.CloseAtMarket(position, volume);
                            }
                        }
                    }

                    if (shortTradingEnabled)
                    {
                        if (positionsShort.Any())
                        {
                            Position lastPosition = positionsShort.Last();
                            decimal currentLoss = CalcPositionRevenue_SHORT(lastPosition.OpenVolume, lastPosition.EntryPrice, candle.Close, FEE_PERCENTS);
                            if (currentLoss < 0)
                            {
                                decimal moneyIn = CalcTradeMoney(lastPosition.OpenVolume, lastPosition.EntryPrice, FEE_PERCENTS, Side.Sell);
                                decimal maxAllowedLoss = -(moneyIn * (100 + AVERAGING_THRESHOLD_PERCENTS) / 100 - moneyIn);
                                if (Math.Abs(currentLoss) > Math.Abs(maxAllowedLoss))
                                {
                                    decimal newAveragingPositionVolume = positionsShort.Count == 1 ?
                                        lastPosition.OpenVolume :
                                        lastPosition.OpenVolume + positionsShort[positionsShort.Count - 2].OpenVolume;
                                    _tab.SellAtMarket(newAveragingPositionVolume);
                                }
                            }
                        }
                        else
                        {
                            _tab.SellAtMarket(GetVolume());
                        }
                    }
                }
            }
        }

        private List<Position> GetPositions_LONG()
        {
            if (_tab.PositionsOpenAll != null)
            {
                return _tab.PositionsOpenAll.Where(p => p.Direction == Side.Buy && p.State == PositionStateType.Open).OrderBy(p => p.Number).ToList();
            }
            return new List<Position>();
        }

        private List<Position> GetPositions_SHORT()
        {
            if (_tab.PositionsOpenAll != null)
            {
                return _tab.PositionsOpenAll.Where(p => p.Direction == Side.Sell && p.State == PositionStateType.Open).OrderBy(p => p.Number).ToList();
            }
            return new List<Position>();
        }

        private void _tab_PositionOpenEventHandler(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                List<Position> positions = position.Direction == Side.Buy ? GetPositions_LONG() : GetPositions_SHORT();
                if (positions.Count > 1)
                {
                    decimal totalVolume = positions.Select(p => p.OpenVolume).Sum();
                    decimal avgEntryPrice = positions.Select(p => p.EntryPrice * p.OpenVolume).Sum() / totalVolume;
                    decimal moneyIn = CalcTradeMoney(totalVolume, avgEntryPrice, FEE_PERCENTS, position.Direction);
                    decimal wantedProfit = moneyIn * (100 + MIN_PROFIT_PERCENTS) / 100 - moneyIn;
                    decimal takeProfitPrice = position.Direction == Side.Buy ? 
                        CalcPrice_LONG_TP_TakeWantedProfit(totalVolume, avgEntryPrice, wantedProfit, FEE_PERCENTS) : 
                        CalcPrice_SHORT_TP_TakeWantedProfit(totalVolume, avgEntryPrice, wantedProfit, FEE_PERCENTS);

                    // int firstPositionNumber = positions.First().Number;
                    // List<Position> positionsToAverage = positions.Where(p => p.Number > firstPositionNumber).ToList();

                    foreach (Position positionToAverage in positions)
                    {
                        _tab.CloseAtProfit(positionToAverage, takeProfitPrice, takeProfitPrice);
                    }
                }
            }
        }

        private decimal GetVolume()
        {
            return 1;
            //decimal volume = VolumeFirstEntry.ValueDecimal;

            //if (VolumeMode.ValueString == "Contract currency") // "Валюта контракта"
            //{
            //    decimal contractPrice = TabsSimple[0].PriceBestAsk;
            //    volume = Math.Round(VolumeFirstEntry.ValueDecimal / contractPrice, VolumeDecimals.ValueInt);
            //    return volume;
            //}
            //else if (VolumeMode.ValueString == "Number of contracts")
            //{
            //    return volume;
            //}
            //else //if (VolumeRegime.ValueString == "% of the total portfolio")
            //{
            //    return Math.Round(_tab.Portfolio.ValueCurrent * (volume / 100) / _tab.PriceBestAsk / _tab.Securiti.Lot, VolumeDecimals.ValueInt);
            //}
        }

        private decimal CalcPositionRevenue_LONG(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents)
        {
            decimal fee;
            return CalcPositionRevenue_LONG(quantity, priceEntry, priceClose, feeInPercents, out fee);
        }

        private decimal CalcPositionRevenue_SHORT(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents)
        {
            decimal fee;
            return CalcPositionRevenue_SHORT(quantity, priceEntry, priceClose, feeInPercents, out fee);
        }

        private decimal CalcPositionRevenue_LONG(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents, out decimal fee)
        {
            decimal feeMoneySpent;
            decimal feeMoneyGot;
            decimal moneySpent = CalcTradeMoney(quantity, priceEntry, feeInPercents, Side.Buy, out feeMoneySpent);
            decimal moneyGot = CalcTradeMoney(quantity, priceClose, feeInPercents, Side.Sell, out feeMoneyGot);
            fee = feeMoneySpent + feeMoneyGot;
            return moneyGot - moneySpent;
        }

        private decimal CalcPositionRevenue_SHORT(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents, out decimal fee)
        {
            decimal feeMoneyGot;
            decimal feeMoneySpent;
            decimal moneyGot = CalcTradeMoney(quantity, priceEntry, feeInPercents, Side.Sell, out feeMoneyGot);
            decimal moneySpent = CalcTradeMoney(quantity, priceClose, feeInPercents, Side.Buy, out feeMoneySpent);
            fee = feeMoneyGot + feeMoneySpent;
            return moneyGot - moneySpent;
        }

        private decimal CalcTradeMoney(decimal quantity, decimal price, decimal feeInPercents, Side side)
        {
            decimal fee;
            return CalcTradeMoney(quantity, price, feeInPercents, side, out fee);
        }

        private decimal CalcTradeMoney(decimal quantity, decimal price, decimal feeInPercents, Side side, out decimal fee)
        {
            decimal moneyNoFee = quantity * price;
            fee = moneyNoFee / 100 * feeInPercents;
            return side == Side.Buy ? moneyNoFee + fee : moneyNoFee - fee;
        }

        private decimal CalcPrice_LONG_TP_TakeWantedProfit(decimal quantity, decimal entryPrice, decimal wantedCleanProfit, decimal feeInPercents)
        {
            return (quantity * entryPrice * (100 + feeInPercents) + 100 * wantedCleanProfit) / (quantity * (100 - feeInPercents));
        }

        private decimal CalcPrice_SHORT_TP_TakeWantedProfit(decimal quantity, decimal entryPrice, decimal wantedCleanProfit, decimal feeInPercents)
        {
            return (quantity * entryPrice * (100 - feeInPercents) - 100 * wantedCleanProfit) / (quantity * (100 + feeInPercents));
        }
    }
}
