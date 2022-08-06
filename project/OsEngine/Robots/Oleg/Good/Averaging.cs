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
            const decimal FEE_PERCENTS = 0.04m;
            const decimal MIN_PROFIT_PERCENTS = 0.08m;
            const decimal AVERAGING_THRESHOLD_PERCENTS = 0.08m;

            if (Regime.ValueString == "Off" || _tab.CandlesAll == null || _tab.CandlesAll.Count < 2)
            {
                return;
            }

            Candle candle = candles.Last();
            Candle previousCandle = candles[candles.Count - 2];
            bool candleColorSwitched = candle.IsUp != previousCandle.IsUp;

            if (candleColorSwitched)
            {
                List<Position> positionsLong = GetPositions_LONG();
                List<Position> positionsShort = GetPositions_SHORT();

                if (candle.IsUp)
                {
                    if (positionsShort.Count == 1)
                    {
                        Position position = positionsShort.First();
                        decimal volume = position.OpenVolume;
                        decimal entryPrice = position.EntryPrice;
                        decimal revenue = CalcPositionRevenue_SHORT(volume, entryPrice, candle.Close, FEE_PERCENTS);
                        if (revenue > 0)
                        {
                            decimal minTakeProfitPrice = entryPrice * (100 - MIN_PROFIT_PERCENTS) / 100;
                            decimal neededRevenue = CalcPositionRevenue_SHORT(volume, entryPrice, minTakeProfitPrice, FEE_PERCENTS);
                            if (revenue >= neededRevenue)
                            {
                                Console.WriteLine("Closing SHORT");
                                _tab.CloseAtMarket(position, volume);
                            }
                        }
                    }

                    if (positionsLong.Any())
                    {
                        // If best BUY pos in enough loss
                        // [
                        //    averaging (volume - sum of two prev BUY poses)
                        //    +
                        //    set TP for all BUY poses except of the first one to the price of ZERO LOSS + WANTED PROFIT
                        // ]
                    }
                    else
                    {
                        Console.WriteLine("Opening LONG");
                        _tab.BuyAtMarket(GetVolume());
                    }
                }
                else if (candle.IsDown)
                {
                    if (positionsLong.Count == 1)
                    {
                        Position position = positionsLong.First();
                        decimal volume = position.OpenVolume;
                        decimal entryPrice = position.EntryPrice;
                        decimal revenue = CalcPositionRevenue_LONG(volume, entryPrice, candle.Close, FEE_PERCENTS);
                        if (revenue > 0)
                        {
                            decimal minTakeProfitPrice = entryPrice * (100 + MIN_PROFIT_PERCENTS) / 100;
                            decimal neededRevenue = CalcPositionRevenue_LONG(volume, entryPrice, minTakeProfitPrice, FEE_PERCENTS);
                            if (revenue >= neededRevenue)
                            {
                                Console.WriteLine("Closing LONG");
                                _tab.CloseAtMarket(position, volume);
                            }
                        }
                    }

                    if (positionsShort.Any())
                    {
                        // If best SELL pos in enough loss
                        // [
                        //    averaging (volume - sum of two prev SELL poses)
                        //    +
                        //    set TP for all SELL poses except of the first one to the price of ZERO LOSS + WANTED PROFIT
                        // ]
                    }
                    else
                    {
                        Console.WriteLine("Opening SHORT");
                        _tab.SellAtMarket(GetVolume());
                    }
                }
            }
        }

        private List<Position> GetPositions_LONG()
        {
            if (_tab.PositionsOpenAll != null)
            {
                return _tab.PositionsOpenAll.Where(p => p.Direction == Side.Buy && p.State == PositionStateType.Open).ToList();
            }
            return new List<Position>();
        }

        private List<Position> GetPositions_SHORT()
        {
            if (_tab.PositionsOpenAll != null)
            {
                return _tab.PositionsOpenAll.Where(p => p.Direction == Side.Sell && p.State == PositionStateType.Open).ToList();
            }
            return new List<Position>();
        }

        private void _tab_PositionOpenEventHandler(Position position)
        {
            if (position != null && position.State == PositionStateType.Open)
            {
                // _tab.CloseAtLimit(position, takeProfitPrice, position.OpenVolume);
                // _tab.CloseAtStop(position, stopLossPrice, stopLossPrice);
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

        public decimal CalcPositionRevenue_LONG(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents)
        {
            decimal fee;
            return CalcPositionRevenue_LONG(quantity, priceEntry, priceClose, feeInPercents, out fee);
        }

        public decimal CalcPositionRevenue_SHORT(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents)
        {
            decimal fee;
            return CalcPositionRevenue_SHORT(quantity, priceEntry, priceClose, feeInPercents, out fee);
        }

        public decimal CalcPositionRevenue_LONG(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents, out decimal fee)
        {
            decimal feeMoneySpent;
            decimal feeMoneyGot;
            decimal moneySpent = CalcTradeMoney(quantity, priceEntry, feeInPercents, Side.Buy, out feeMoneySpent);
            decimal moneyGot = CalcTradeMoney(quantity, priceClose, feeInPercents, Side.Sell, out feeMoneyGot);
            fee = feeMoneySpent + feeMoneyGot;
            return moneyGot - moneySpent;
        }

        public decimal CalcPositionRevenue_SHORT(decimal quantity, decimal priceEntry, decimal priceClose, decimal feeInPercents, out decimal fee)
        {
            decimal feeMoneyGot;
            decimal feeMoneySpent;
            decimal moneyGot = CalcTradeMoney(quantity, priceEntry, feeInPercents, Side.Sell, out feeMoneyGot);
            decimal moneySpent = CalcTradeMoney(quantity, priceClose, feeInPercents, Side.Buy, out feeMoneySpent);
            fee = feeMoneyGot + feeMoneySpent;
            return moneyGot - moneySpent;
        }

        protected decimal CalcTradeMoney(decimal quantity, decimal price, decimal feeInPercents, Side side, out decimal fee)
        {
            decimal moneyNoFee = quantity * price;
            fee = moneyNoFee / 100 * feeInPercents;
            return side == Side.Buy ? moneyNoFee + fee : moneyNoFee - fee;
        }
    }
}
