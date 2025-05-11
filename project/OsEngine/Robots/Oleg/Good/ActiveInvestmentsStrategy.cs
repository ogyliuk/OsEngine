using OsEngine.Charts.CandleChart.Elements;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("ActiveInvestmentsStrategy")]
    public class ActiveInvestmentsStrategy : BotPanel
    {
        private BotTabSimple _bot;

        private decimal BALANCE_USDT;
        private Position currentPosition;
        private DateTime lastBalanceDate;
        private decimal lastBalancePrice;
        private decimal legBTC = 0;
        private decimal legUSDT = 0;
        private int rebalanceCount = 0;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal RebalancePriceDistancePrecent;

        public ActiveInvestmentsStrategy(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            RebalancePriceDistancePrecent = CreateParameter("Rebalance Price Distance %", 5m, 5m, 100m, 1m, "Robot parameters");

            _bot.CandleFinishedEvent += event_CandleClosed;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened;
            _bot.PositionClosingSuccesEvent += event_PositionClosed;

            OlegUtils.LogSeparationLine();
        }

        public override string GetNameStrategyType()
        {
            return "ActiveInvestmentsStrategy";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_CandleClosed(List<Candle> candles)
        {
            if (Regime.ValueString == "On" && candles != null)
            {
                decimal price = candles.Last().Close;
                DateTime date = candles.Last().TimeStart;

                // 1st BALANCE
                if (legBTC == 0 && legUSDT == 0)
                {
                    this.BALANCE_USDT = _bot.Portfolio.ValueCurrent;
                    this.legUSDT = BALANCE_USDT / 2;
                    this.legBTC = BALANCE_USDT / 2 / price;
                    // _bot.BuyAtLimit(legBTC, price);
                    currentPosition = _bot.BuyAtMarket(legBTC);
                    this.lastBalancePrice = price;
                    this.lastBalanceDate = date;
                    return;
                }

                decimal priceMovePercent = Math.Abs((price * 100 / lastBalancePrice) - 100);
                if (priceMovePercent >= RebalancePriceDistancePrecent.ValueDecimal)
                {
                    PriceMove priceMove = GetPriceMove(lastBalancePrice, price);
                    decimal priceMoveUsdt = Math.Abs(lastBalancePrice - price);
                    int daysPastFromLastRebalance = (date - lastBalanceDate).Days;

                    OlegUtils.Log("After {0} days {1} move = {2}% ({3}$) |::::::::::| {4}$ ===> {5}$",
                        daysPastFromLastRebalance,
                        priceMove,
                        Math.Round(priceMovePercent, 2),
                        Math.Round(priceMoveUsdt, 0), 
                        Math.Round(lastBalancePrice, 0), 
                        Math.Round(price, 0));

                    decimal newLegUSDT = legUSDT;
                    decimal newLegBTC = legBTC;
                    if (priceMove == PriceMove.UP)
                    {
                        // Close LONG succuss - Sell BTC
                        decimal neededUsdtToGet = (legBTC * price - legUSDT) / 2;
                        decimal neededBtcToSell = neededUsdtToGet / price;
                        newLegUSDT = legUSDT + neededUsdtToGet;
                        newLegBTC = legBTC - neededBtcToSell;
                        //_bot.CloseAtLimit(currentPosition, price, currentPosition.OpenVolume);
                        _bot.CloseAtMarket(currentPosition, currentPosition.OpenVolume);
                        // _bot.BuyAtLimit(newLegBTC, price);
                        currentPosition = _bot.BuyAtMarket(newLegBTC);
                    }
                    else if (priceMove == PriceMove.DOWN)
                    {
                        // Close SHORT succuss - Buy BTC
                        decimal neededBtcToBuy = (legUSDT / price - legBTC) / 2;
                        decimal neededUsdtToSpend = neededBtcToBuy * price;
                        newLegUSDT = legUSDT - neededUsdtToSpend;
                        newLegBTC = legBTC + neededBtcToBuy;
                        // _bot.CloseAtLimit(currentPosition, price, currentPosition.OpenVolume);
                        _bot.CloseAtMarket(currentPosition, currentPosition.OpenVolume);
                        // _bot.BuyAtLimit(newLegBTC, price);
                        currentPosition = _bot.BuyAtMarket(newLegBTC);
                    }

                    rebalanceCount++;
                    decimal totalUSDT = legUSDT + legBTC * price;

                    OlegUtils.Log("USDT {0}$ ---> {1}$", Math.Round(legUSDT, 0), Math.Round(newLegUSDT, 0));
                    OlegUtils.Log("BTC {0} ---> {1}", Math.Round(legBTC, 2), Math.Round(newLegBTC, 2));
                    OlegUtils.Log("#{0} Total USDT = {1}$", rebalanceCount, Math.Round(totalUSDT, 0));
                    OlegUtils.Log("---------------------------------------------------------------------------");

                    legUSDT = newLegUSDT;
                    legBTC = newLegBTC;

                    this.lastBalancePrice = price;
                    this.lastBalanceDate = date;
                }
            }
        }

        private void event_PositionOpened(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                // currentPosition = p;
            }
        }

        private void event_PositionClosed(Position p)
        {
            if (p.State == PositionStateType.Done)
            {
            }
        }

        private static PriceMove GetPriceMove(decimal lastBalancePrice, decimal currentPrice)
        {
            return currentPrice > lastBalancePrice ? PriceMove.UP : PriceMove.DOWN;
        }

        private enum PriceMove
        {
            UP,
            DOWN
        }
    }
}
