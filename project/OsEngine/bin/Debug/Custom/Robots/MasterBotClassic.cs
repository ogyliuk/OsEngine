using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;

namespace OsEngine.Robots.FoundBots
{
    [Bot("MasterBotClassic")]
    public class MasterBotClassic : BotPanel
    {

        #region общий портфель

        public static decimal StaticPortfolioValue
        {
            get { return _staticPortfolioValue; }
            set
            {
                _staticPortfolioValue = value;
                if (StaticPortfolioChangedEvent != null)
                {
                    StaticPortfolioChangedEvent();
                }
            }
        }

        private static decimal _staticPortfolioValue;

        private static bool _staticPortfolioLoaded;

        private static void LoadStaticPortfolio()
        {
            if (_staticPortfolioLoaded)
            {
                return;
            }
            _staticPortfolioLoaded = true;


            if (!File.Exists(@"Engine\AdminCapacityStaticPartSave.txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\AdminCapacityStaticPartSave.txt"))
                {
                    StaticPortfolioValue = reader.ReadLine().ToDecimal();

                    reader.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }

        }

        private static void SaveStaticPortfolio()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\AdminCapacityStaticPartSave.txt", false)
                )
                {
                    writer.WriteLine(StaticPortfolioValue);
                    writer.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public static event Action StaticPortfolioChangedEvent;

        #endregion

        #region сервисный код, настройки

        public MasterBotClassic(string name, StartProgram startProgram) : base(name, startProgram)
        {
            LoadStaticPortfolio();

            Regime = CreateParameter("Regime", "Off", new[]
            {
                "Off", "On"
            });

            TimeStart = CreateParameterTimeOfDay("Начало торгов", 0, 0, 0, 0);
            TimeEnd = CreateParameterTimeOfDay("Конец торгов", 24, 0, 0, 0);

            List<string> strategies = BotFactory.GetScriptsNamesStrategy();
            CurrentStrategy = CreateParameter("Встроенная стратегия", "None", strategies.ToArray());

            VolumeOnPosition = CreateParameter("Объём входа", 10, 1.0m, 50, 4);

            VolumeRegime = CreateParameter("Тип объёма", "Валюта контракта", new[]
{
                "Кол-во контрактов", "Валюта контракта", "% от Общего объёма портфеля"
            });

            AllPortfolioValue = CreateParameter("Общий объём портфеля", 5000m, 1, 50000, 100);

            if (StaticPortfolioValue != 0)
            {
                AllPortfolioValue.ValueDecimal = StaticPortfolioValue;
            }
            else if (StaticPortfolioValue == 0)
            {
                StaticPortfolioValue = AllPortfolioValue.ValueDecimal;
            }

            AllPortfolioValue.ValueChange += () =>
            {
                if (StaticPortfolioValue == AllPortfolioValue.ValueDecimal)
                {
                    return;
                }
                StaticPortfolioValue = AllPortfolioValue.ValueDecimal;
                SaveStaticPortfolio();
            };

            StaticPortfolioChangedEvent += () =>
            {
                if (AllPortfolioValue.ValueDecimal == StaticPortfolioValue)
                {
                    return;
                }
                AllPortfolioValue.ValueDecimal = StaticPortfolioValue;
            };

            VolumeDecimals = CreateParameter("Знаков в объёме после запятой", 2, 1, 50, 4);

            MaxPositionDuplicateCount = CreateParameter("Макс. кол-во позиций для дублирования", 1, 1, 15, 1);

            ShowSlaveChartDialog = CreateParameterButton("Окно встроенной стратегии");
            ShowSlaveChartDialog.UserClickOnButtonEvent += ShowSlaveChartDialog_UserClickOnButtonEvent;

            ShowSlaveParameters = CreateParameterButton("Параметры встроенной стратегии");
            ShowSlaveParameters.UserClickOnButtonEvent += ShowSlaveParameters_UserClickOnButtonEvent;

            SlippageInter = CreateParameter("Проскальзывание вход %", 0.05m, 1, 50, 4);
            SlippageExit = CreateParameter("Проскальзывание выход %", 0.15m, 1, 50, 4);
            SlippageSecondInter = CreateParameter("Проскальзывание вход 2 %", 0.15m, 1, 50, 4);
            SlippageSecondExit = CreateParameter("Проскальзывание выход 2 %", 0.25m, 1, 50, 4);
            OpenOrderLifeTime = CreateParameter("Время жизни ордера", 60, 1, 50, 4);
            MaxOrderExecutionDeviation = CreateParameter("Макс отклонение для повторного исполнения %", 0.05m, 1m, 50, 4);
            InterTime = CreateParameter("Время на попытки повторного входа сек", 600, 1, 5000, 4);
            ShiftToStopOpdersValue = CreateParameter("Отступ для входов и стопов %", 0.01m, 1, 50, 4);

            CreateSlave();

            ParametrsChangeByUser += ParametrsChange;

            Thread worker = new Thread(Worker);
            worker.Start();

            Thread support = new Thread(PositionSupportThread);
            support.IsBackground = true;
            support.Start();

            LoadPositionReports();
        }

        private void ShowSlaveParameters_UserClickOnButtonEvent()
        {
            if (_slave == null)
            {
                MessageBox.Show("Slave Bot is not created");
                return;
            }
            _slave.ShowParametrDialog();
        }

        private void ParametrsChange()
        {
            CreateSlave();
        }

        public override string GetNameStrategyType()
        {
            return "MasterBotClassic";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }

        public StrategyParameterString Regime;

        public StrategyParameterString CurrentStrategy;

        public StrategyParameterDecimal VolumeOnPosition;

        public StrategyParameterString VolumeRegime;

        public StrategyParameterInt VolumeDecimals;

        public StrategyParameterInt MaxPositionDuplicateCount;

        public StrategyParameterButton ShowSlaveParameters;

        public StrategyParameterButton ShowSlaveChartDialog;

        // сопровождение позиции

        public StrategyParameterTimeOfDay TimeStart;

        public StrategyParameterTimeOfDay TimeEnd;

        public StrategyParameterDecimal SlippageInter;

        public StrategyParameterDecimal SlippageExit;

        public StrategyParameterDecimal SlippageSecondInter;

        public StrategyParameterDecimal SlippageSecondExit;

        public StrategyParameterInt InterTime;

        public StrategyParameterDecimal MaxOrderExecutionDeviation;

        public StrategyParameterInt OpenOrderLifeTime;

        public StrategyParameterDecimal AllPortfolioValue;

        public StrategyParameterDecimal ShiftToStopOpdersValue;

        #endregion

        #region сабСтратегия

        private void CreateSlave()
        {
            if (_slave != null)
            {
                string strategyName = _slave.GetType().Name;
                if (strategyName == CurrentStrategy.ValueString)
                {
                    return;
                }
            }

            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            string name = CurrentStrategy.ValueString;

            _slave = BotFactory.GetStrategyForName(
                CurrentStrategy.ValueString, "Slave" + NameStrategyUniq,
                StartProgram.IsOsTrader, true);

            RebuildTabs();
        }

        private void RebuildTabs()
        {
            ClearTabs();

            for (int i = 0; _slave.TabsSimple != null && i < _slave.TabsSimple.Count; i++)
            {
                TabCreate(BotTabType.Simple);
                TabsSimple[TabsSimple.Count - 1].CandleFinishedEvent += AdminCapacityCreator_CandleFinishedEvent;
                TabsSimple[TabsSimple.Count - 1].NewTickEvent += AdminCapacityCreator_NewTickEvent;
                TabsSimple[TabsSimple.Count - 1].PositionBuyAtStopActivateEvent += AdminCapacityCreator_PositionBuyAtStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionSellAtStopActivateEvent += AdminCapacityCreator_PositionSellAtStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionStopActivateEvent += AdminCapacityCreator_PositionStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionProfitActivateEvent += AdminCapacityCreator_PositionProfitActivateEvent;

                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.DoubleExitIsOn = false;
                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.SecondToCloseIsOn = false;
                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.SecondToOpenIsOn = false;
            }

            for (int i = 0; _slave.TabsIndex != null && i < _slave.TabsIndex.Count; i++)
            {
                TabCreate(BotTabType.Index);
            }

            for (int i = 0; _slave.TabsCluster != null && i < _slave.TabsCluster.Count; i++)
            {
                TabCreate(BotTabType.Cluster);
            }
        }

        private BotPanel _slave;

        private void ShowSlaveChartDialog_UserClickOnButtonEvent()
        {
            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            if (TabsSimple == null ||
                TabsSimple.Count == 0
                || TabsSimple[0].IsConnected == false)
            {
                return;
            }

            DateTime timeStartWaiting = DateTime.Now;

            BotPanel bot = TestRobot(CurrentStrategy.ValueString, TabsSimple, _slave.Parameters);

            TabsSimple[0].SetNewLogMessage("Test bot Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

            if (bot != null)
            {
                CloseParameterDialog();
                _slave.CloseParameterDialog();
                bot.ShowChartDialog();
            }
        }

        #endregion

        #region работа встроенного оптимизатора

        private OptimizerDataStorage _storage;

        private BotPanel _bot;

        private bool _lastTestIsOver = true;

        private string _botStarterLocker = "botsLocker";

        private BotPanel TestRobot(string strategyName, List<BotTabSimple> tabs, List<IIStrategyParameter> parametrs)
        {
            try
            {
                lock (_botStarterLocker)
                {
                    if (_lastTestIsOver == false)
                    {
                        return null;
                    }
                    _lastTestIsOver = false;
                }

                DateTime timeStartStorageCreate = DateTime.Now;

                _storage = CreateNewStorage(tabs);

                //TabsSimple[0].SetNewLogMessage("StorageCreate " + (DateTime.Now - timeStartStorageCreate).ToString(), LogMessageType.System);

                DateTime timeStartWaiting = DateTime.Now;

                while (_storage.Securities == null)
                {
                    Thread.Sleep(10);

                    if (timeStartWaiting.AddSeconds(5) < DateTime.Now)
                    {
                        _lastTestIsOver = true;
                        return null;
                    }
                }

                //TabsSimple[0].SetNewLogMessage("WaitingSecurities" + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                timeStartWaiting = DateTime.Now;

                OptimizerServer server = CreateNewServer(_storage, _storage.TimeStart, _storage.TimeEnd.AddHours(2));


                //TabsSimple[0].SetNewLogMessage("Server creation Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                timeStartWaiting = DateTime.Now;

                _bot = CreateNewBot(strategyName, parametrs, server, StartProgram.IsTester);
                while (_bot.IsConnected == false)
                {
                    Thread.Sleep(5);

                    if (timeStartWaiting.AddSeconds(20) < DateTime.Now)
                    {
                        _lastTestIsOver = true;
                        return null;
                    }
                }

                if (_bot._chartUi != null)
                {
                    _bot.StopPaint();
                }

                //TabsSimple[0].SetNewLogMessage("Bot creation Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                bool testIsOver = false;
                server.TestingEndEvent += delegate (int i) { testIsOver = true; };
                server.TestingStart();


                timeStartWaiting = DateTime.Now;
                while (testIsOver == false)
                {
                    Thread.Sleep(5);
                    if (timeStartWaiting.AddSeconds(20) < DateTime.Now)
                    {
                        _lastTestIsOver = true;
                        return null;
                    }
                }
                Thread.Sleep(500);
                //TabsSimple[0].SetNewLogMessage("Testing Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);
                _lastTestIsOver = true;
                return _bot;
            }
            catch (Exception e)
            {
                _lastTestIsOver = true;
                TabsSimple[0].SetNewLogMessage(e.ToString(), LogMessageType.Error);
                return null;
            }
        }

        private string _pathToBotData;

        private static bool _tempDirAlreadyClear = false;

        private string GetDataPath()
        {
            if (_pathToBotData != null)
            {
                return _pathToBotData;
            }

            string folder = "Data\\";

            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }
            folder += "AdminCapacityTemp\\";
            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }

            if (_tempDirAlreadyClear == false)
            {
                _tempDirAlreadyClear = true;

                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {
                    // ignore
                }

                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch
                {
                    // ignore
                }
            }

            folder += NumberGen.GetNumberDeal(TabsSimple[0].StartProgram) + "\\";

            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }

            _pathToBotData = folder;

            return folder;
        }

        private OptimizerDataStorage CreateNewStorage(List<BotTabSimple> tabs)
        {
            //_sourceDataType == TesterSourceDataType.Folder && !string.IsNullOrWhiteSpace(_pathToFolder)

            string folder = GetDataPath();

            SaveCandlesInFolder(tabs, folder);

            //tabs[0].SetNewLogMessage("Bot save folder. Bot: " + NameStrategyUniq + "Folder: " + folder, LogMessageType.Error);

            /* if (_storage != null &&
                 _storage.PathToFolder == folder &&
                 _storage.TimeEnd == tabs[0].CandlesFinishedOnly[tabs[0].CandlesFinishedOnly.Count - 1].TimeStart)
             {
                 return _storage;
             }*/



            if (_storage != null)
            {
                _storage.TimeEnd = tabs[0].CandlesAll[tabs[0].CandlesAll.Count - 1].TimeStart;
                _storage.TimeStart = tabs[0].CandlesFinishedOnly[0].TimeStart;
                _storage.TimeNow = tabs[0].CandlesFinishedOnly[0].TimeStart;

                if (_storage.Securities == null)
                {
                    _storage.ReloadSecurities(true);
                }
                else
                {
                    _storage.ClearStorages();
                }

                return _storage;
            }

            OptimizerDataStorage Storage = new OptimizerDataStorage(NameStrategyUniq);
            Storage.SourceDataType = TesterSourceDataType.Folder;
            Storage.PathToFolder = folder;
            Storage.TimeEnd = tabs[0].CandlesAll[tabs[0].CandlesAll.Count - 1].TimeStart;
            Storage.TimeStart = tabs[0].CandlesFinishedOnly[0].TimeStart;
            Storage.TimeNow = tabs[0].CandlesFinishedOnly[0].TimeStart;

            Storage.ReloadSecurities(true);

            return Storage;
        }

        private void SaveCandlesInFolder(List<BotTabSimple> tabs, string folder)
        {
            try
            {
                while (Directory.GetFiles(folder) != null &&
                    Directory.GetFiles(folder).Length > 0)
                {
                    string file = Directory.GetFiles(folder)[0];
                    File.Delete(file);
                }

                for (int i = 0; i < tabs.Count; i++)
                {
                    List<Candle> candles = tabs[i].CandlesAll;
                    Candle last = candles[candles.Count - 1];
                    StreamWriter writer = new StreamWriter(folder + tabs[i].Securiti.Name, false);

                    for (int i2 = 0; i2 < candles.Count; i2++)
                    {
                        writer.WriteLine(candles[i2].StringToSave);
                    }
                    writer.Close();
                }
            }
            catch (Exception e)
            {

            }

        }

        List<OptimizerServer> _servers = new List<OptimizerServer>();

        private OptimizerServer CreateNewServer(OptimizerDataStorage storage, DateTime timeCandleStart,
            DateTime timeCandleEnd)
        {
            // 1. Create a new server for optimization. And one thread respectively
            // 1. создаём новый сервер для оптимизации. И один поток соответственно
            OptimizerServer server = ServerMaster.CreateNextOptimizerServer(storage,
                NumberGen.GetNumberDeal(StartProgram), 100000);

            _servers.Add(server);

            server.TestingEndEvent += server_TestingEndEvent;
            server.TypeTesterData = storage.TypeTesterData;


            Security secToStart = storage.Securities[0];

            if (secToStart == null)
            {

            }

            server.GetDataToSecurity(secToStart, TabsSimple[0].Connector.TimeFrame, timeCandleStart,
                timeCandleEnd);


            if (server.Securities.Count == 0)
            {

            }

            return server;
        }

        private object _serverRemoveLocker = new object();

        private void server_TestingEndEvent(int serverNum)
        {
            lock (_serverRemoveLocker)
            {
                for (int i = 0; i < _servers.Count; i++)
                {
                    if (_servers[i].NumberServer == serverNum)
                    {
                        _servers[i].TestingEndEvent -= server_TestingEndEvent;
                        _servers[i].Clear();
                        ServerMaster.RemoveOptimizerServer(_servers[i]);
                        _servers.RemoveAt(i);
                        break;
                    }
                }
            }

            if (_bot._chartUi != null)
            {
                _bot._chartUi.StartPaint();
            }
        }

        private BotPanel _slaveBot;

        private BotPanel CreateNewBot(string botName,
        List<IIStrategyParameter> parametrs,
        OptimizerServer server, StartProgram regime)
        {
            if (_slaveBot == null)
            {
                _slaveBot = BotFactory.GetStrategyForName(CurrentStrategy.ValueString, botName + NumberGen.GetNumberDeal(StartProgram), regime, true);
            }
            else
            {
                _slaveBot.Clear();
                _slaveBot.TabsSimple[0].Connector.ServerUid = server.NumberServer;
                _slaveBot.TabsSimple[0].Connector.ReconnectHard();
                Thread.Sleep(100);
            }

            for (int i = 0; i < parametrs.Count; i++)
            {
                IIStrategyParameter par = parametrs.Find(p => p.Name == parametrs[i].Name);

                if (par == null)
                {
                    par = parametrs[i];
                }

                if (_slaveBot.Parameters[i].Type == StrategyParameterType.Bool)
                {
                    ((StrategyParameterBool)_slaveBot.Parameters[i]).ValueBool = ((StrategyParameterBool)par).ValueBool;
                }
                else if (_slaveBot.Parameters[i].Type == StrategyParameterType.String)
                {
                    ((StrategyParameterString)_slaveBot.Parameters[i]).ValueString = ((StrategyParameterString)par).ValueString;
                }
                else if (_slaveBot.Parameters[i].Type == StrategyParameterType.Int)
                {
                    ((StrategyParameterInt)_slaveBot.Parameters[i]).ValueInt = ((StrategyParameterInt)par).ValueInt;
                }
                else if (_slaveBot.Parameters[i].Type == StrategyParameterType.Decimal)
                {
                    ((StrategyParameterDecimal)_slaveBot.Parameters[i]).ValueDecimal = ((StrategyParameterDecimal)par).ValueDecimal;
                }
            }

            // custom tabs
            // настраиваем вкладки
            for (int i = 0; i < TabsSimple.Count; i++)
            {
                _slaveBot.TabsSimple[i].Connector.ServerType = ServerType.Optimizer;
                _slaveBot.TabsSimple[i].Connector.PortfolioName = server.Portfolios[0].Number;
                _slaveBot.TabsSimple[i].Connector.SecurityName = TabsSimple[i].Connector.SecurityName;
                _slaveBot.TabsSimple[i].Connector.SecurityClass = TabsSimple[i].Connector.SecurityClass;
                _slaveBot.TabsSimple[i].Connector.TimeFrame = TabsSimple[i].Connector.TimeFrame;
                _slaveBot.TabsSimple[i].Connector.ServerUid = server.NumberServer;

                if (server.TypeTesterData == TesterDataType.Candle)
                {
                    _slaveBot.TabsSimple[i].Connector.CandleMarketDataType = CandleMarketDataType.Tick;
                }
                else if (server.TypeTesterData == TesterDataType.MarketDepthAllCandleState ||
                         server.TypeTesterData == TesterDataType.MarketDepthOnlyReadyCandle)
                {
                    _slaveBot.TabsSimple[i].Connector.CandleMarketDataType =
                        CandleMarketDataType.MarketDepth;
                }
            }

            return _slaveBot;
        }

        #endregion

        #region Вход в торговую логику

        private void AdminCapacityCreator_CandleFinishedEvent(List<Candle> candles)
        {
            if (StartProgram == StartProgram.IsOsTrader)
            {
                return;
            }

            //TabsSimple[0].SetNewLogMessage("Candle finished event. last:  " + candles[candles.Count - 1].TimeStart, LogMessageType.System);
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            if (TimeStart.Value > candles[candles.Count - 1].TimeStart ||
                TimeEnd.Value < candles[candles.Count - 1].TimeStart)
            {
                return;
            }

            if (candles.Count < 20)
            {
                return;
            }

            _neadToTestBot = true;
        }

        private bool _neadToTestBot;

        private void Worker()
        {
            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }
            while (true)
            {
                Thread.Sleep(50);

                if (_neadToTestBot == false)
                {
                    CheckCandlesCount();
                    continue;
                }

                _neadToTestBot = false;
                Thread.Sleep(100);
                Logic();
            }
        }

        private void CheckCandlesCount()
        {
            if (TabsSimple == null
                || TabsSimple.Count == 0)
            {
                return;
            }

            List<Candle> allCandles = TabsSimple[0].CandlesAll;

            if (allCandles == null ||
                allCandles.Count == 0)
            {
                return;
            }

            Candle last = allCandles[allCandles.Count - 1];

            if (last == null)
            {
                return;
            }

            if (_timeLastCandle == DateTime.MinValue)
            {
                _timeLastCandle = last.TimeStart;
                return;
            }

            if (_timeLastCandle != last.TimeStart)
            {
                _timeLastCandle = last.TimeStart;
                _neadToTestBot = true;
            }
        }

        private DateTime _timeLastCandle = DateTime.MinValue;

        private void AdminCapacityCreator_NewTickEvent(Trade trade)
        {
            if (StartProgram == StartProgram.IsOsTrader)
            {
                return;
            }

            if (_neadToTestBot == false)
            {
                return;
            }

            _neadToTestBot = false;

            try
            {
                Logic();
                SupportLogic(trade.Time);
            }
            catch (Exception error)
            {
                TabsSimple[0].SetNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void Logic()
        {
            if (Regime.ValueString == "Off")
            {
                return;
            }

            DateTime time = DateTime.Now;

            if (TabsSimple[0].CandlesAll.Count < 50)
            {
                return;
            }

            try
            {
                BotPanel bot = TestRobot(CurrentStrategy.ValueString, TabsSimple, _slave.Parameters);

                if (bot == null)
                {
                    return;
                }

                List<Position> slavePoses = null;

                for (int i = 0; i < bot.TabsSimple.Count; i++)
                {
                    slavePoses = bot.TabsSimple[i].PositionsOpenAll;
                    List<PositionOpenerToStop> slaveStops = bot.TabsSimple[i].PositionOpenerToStopsAll;

                    CopyPositions(slavePoses, TabsSimple[i], bot.TabsSimple[i].PositionsAll);
                    CopyStopOpenier(slaveStops, TabsSimple[i]);
                }

                TimeSpan timeTest = DateTime.Now - time;

                TabsSimple[0].SetNewLogMessage(timeTest.ToString(), LogMessageType.System);

                if (slavePoses.Count != 0)
                {
                    TabsSimple[0].SetNewLogMessage("Slave Pos" + slavePoses[0].TimeOpen + " stop : "
                        + slavePoses[0].StopOrderIsActiv.ToString() + " Price : "
                        + slavePoses[0].StopOrderRedLine, LogMessageType.System);
                }

            }
            catch (Exception error)
            {
                TabsSimple[0].SetNewLogMessage(error.ToString(), LogMessageType.System);
            }
        }

        #endregion

        #region Методы копирование позиций по завершению свечи

        private void CopyPositions(List<Position> slavePosActual, BotTabSimple tab, List<Position> slavePosAll)
        {
            List<Position> myPoses = tab.PositionsOpenAll;

            SaveCompareDataInPositions(slavePosAll, tab.PositionsAll, tab.TimeServerCurrent);

            // тупо копируем т.к. у нас нет позиций

            if (slavePosActual.Count != 0 && myPoses.Count == 0)
            {
                UpdateDepositBalance();
                for (int i = 0; i < slavePosActual.Count; i++)
                {
                    OpenPose(slavePosActual[i], tab);
                }
                return;
            }

            // проверяем чтобы у нас была позиция в нужную сторону

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckGoodWayPoses(slavePosActual, myPoses, tab);
            }

            // проверяем чтобы позиции были в одну сторону у обоих вкладок

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckWrongWayPoses(slavePosActual, myPoses, tab);
            }

            // проверяем стопы для позиций

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckStops(slavePosActual, myPoses, tab);
            }

            // закрываем у себя позиции, т.к. у раба нет, а у нас есть

            for (int i = 0; i < slavePosActual.Count; i++)
            {
                if (slavePosActual[i].State == PositionStateType.Closing)
                {
                    slavePosActual.RemoveAt(i);
                    i--;
                }
            }


            if (slavePosActual.Count == 0 && myPoses.Count != 0)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    CloseBadPoses(myPoses[i], tab);
                }
                return;
            }

            // открываем дополнительные позиции

            if (slavePosActual.Count != 0
                && myPoses.Count < slavePosActual.Count
                && myPoses.Count < MaxPositionDuplicateCount.ValueInt)
            {
                CheckDopPoses(slavePosActual, myPoses, tab);
            }
        }

        private void CopyStopOpenier(List<PositionOpenerToStop> stops, BotTabSimple tab)
        {
            tab.BuyAtStopCancel();
            tab.SellAtStopCancel();

            if (stops == null || stops.Count == 0)
            {
                return;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i].Side == Side.Buy)
                {
                    decimal redLine = stops[i].PriceRedLine - stops[i].PriceRedLine * ShiftToStopOpdersValue.ValueDecimal / 100;

                    tab.BuyAtStop(
                        GetBuyVolume(),
                        redLine + (SlippageInter.ValueDecimal * redLine / 100),
                        redLine,
                        stops[i].ActivateType, stops[i].TimeCreate.ToString());
                    tab.SetNewLogMessage(
                        "Buy at stop, активация: " + redLine + " Ордер: "
                        + (redLine + (SlippageInter.ValueDecimal * redLine / 100)),
                        LogMessageType.System);
                }
                if (stops[i].Side == Side.Sell)
                {
                    decimal redLine = stops[i].PriceRedLine + stops[i].PriceRedLine * ShiftToStopOpdersValue.ValueDecimal / 100;

                    tab.SellAtStop(
                        GetSellVolume(),
                        redLine - (SlippageInter.ValueDecimal * redLine / 100),
                        redLine,
                        stops[i].ActivateType, stops[i].TimeCreate.ToString());
                    tab.SetNewLogMessage(
                        "Sell at stop, активация: " + redLine + " Ордер: "
                        + (redLine - (SlippageInter.ValueDecimal * redLine / 100)),
                        LogMessageType.System);
                }
            }
        }

        private bool HaveThisPos(List<Position> poses, Side side)
        {
            for (int i = 0; i < poses.Count; i++)
            {
                if (poses[i].Direction == side)
                {
                    return true;
                }
            }

            return false;
        }

        private void CheckStops(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            decimal stopActivateBuy = 0;
            decimal stopOrderBuy = 0;

            decimal stopActivateSell = 0;
            decimal stopOrderSell = 0;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Buy &&
                    slavePoses[i].StopOrderPrice != 0)
                {
                    stopActivateBuy = slavePoses[i].StopOrderRedLine;
                    stopOrderBuy = slavePoses[i].StopOrderPrice;

                    stopOrderBuy = stopOrderBuy + stopOrderBuy * ShiftToStopOpdersValue.ValueDecimal / 100;
                    stopActivateBuy = stopActivateBuy + stopActivateBuy * ShiftToStopOpdersValue.ValueDecimal / 100;
                }
                if (slavePoses[i].Direction == Side.Sell &&
                    slavePoses[i].StopOrderPrice != 0)
                {
                    stopActivateSell = slavePoses[i].StopOrderRedLine;
                    stopOrderSell = slavePoses[i].StopOrderPrice;

                    stopOrderSell = stopOrderSell - stopOrderSell * ShiftToStopOpdersValue.ValueDecimal / 100;
                    stopActivateSell = stopActivateSell - stopActivateSell * ShiftToStopOpdersValue.ValueDecimal / 100;
                }
            }

            if (stopOrderBuy == 0 &&
                stopOrderSell == 0)
            {
                return;
            }


            for (int i = 0; i < myPoses.Count; i++)
            {
                if (myPoses[i].CloseActiv ||
                    myPoses[i].State != PositionStateType.Open)
                {
                    continue;
                }
                if (myPoses[i].Direction == Side.Buy && stopOrderBuy != 0)
                {
                    TabsSimple[0].CloseAtStop(myPoses[i], stopActivateBuy,
                        stopOrderBuy - (SlippageExit.ValueDecimal * stopOrderBuy / 100));

                }
                if (myPoses[i].Direction == Side.Sell && stopOrderSell != 0)
                {
                    TabsSimple[0].CloseAtStop(myPoses[i], stopActivateSell,
                        stopOrderSell + (SlippageExit.ValueDecimal * stopOrderBuy / 100));
                }
            }
        }

        private void CheckGoodWayPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuySlave = HaveThisPos(slavePoses, Side.Buy);
            bool haveSellSlave = HaveThisPos(slavePoses, Side.Sell);
            bool haveBuyMy = HaveThisPos(myPoses, Side.Buy);
            bool haveSellMy = HaveThisPos(myPoses, Side.Sell);

            if (haveBuySlave && haveBuyMy == false)
            {
                Position pos = slavePoses.Find(p => p.Direction == Side.Buy);

                if (pos != null &&
                    _numsPositionAlreadyUsed.Find(p => p.TimeOpen == pos.TimeOpen) == null &&
                    CanReOpen(pos, tab))
                {
                    _numsPositionAlreadyUsed.Add(pos);

                    Position myPos = tab.BuyAtLimit(GetBuyVolume(),
                        pos.EntryPrice + (SlippageInter.ValueDecimal * pos.EntryPrice / 100), pos.TimeOpen.ToString());
                    _positionsToSupportOpenFirstTime.Add(myPos);
                    TabsSimple[0].SetNewLogMessage("В позиции на сопровождение новый лонг" + pos.Number, LogMessageType.System);
                }
            }
            if (haveSellSlave && haveSellMy == false)
            {
                Position pos = slavePoses.Find(p => p.Direction == Side.Sell);

                if (pos != null &&
                    _numsPositionAlreadyUsed.Find(p => p.TimeOpen == pos.TimeOpen) == null
                    && CanReOpen(pos, tab))
                {
                    _numsPositionAlreadyUsed.Add(pos);
                    Position myPos = tab.SellAtLimit(GetSellVolume(),
                        pos.EntryPrice - (SlippageInter.ValueDecimal * pos.EntryPrice / 100), pos.TimeOpen.ToString());
                    _positionsToSupportOpenFirstTime.Add(myPos);
                    TabsSimple[0].SetNewLogMessage("В позиции на сопровождение новый шорт" + pos.Number, LogMessageType.System);
                }
            }
        }

        /// <summary>
        /// если уже раб развеврнулся, то мы прям по рынку кроемся.
        /// Пофик на всё
        /// </summary>
        private void CheckWrongWayPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuy = false;
            bool haveSell = false;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Sell)
                {
                    haveSell = true;
                }
                if (slavePoses[i].Direction == Side.Buy)
                {
                    haveBuy = true;
                }
            }

            if (haveSell && haveBuy)
            {
                return;
            }

            if (haveBuy)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    if (myPoses[i].Direction == Side.Sell
                        && myPoses[i].State == PositionStateType.Open
                        && myPoses[i].CloseActiv == false
                        && HaveThisPosInArrays(myPoses[i]) == false)
                    {
                        tab.CloseAtMarket(myPoses[i], myPoses[i].OpenVolume);
                    }
                }
            }
            if (haveSell)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    if (myPoses[i].Direction == Side.Buy
                        && myPoses[i].State == PositionStateType.Open
                        && myPoses[i].CloseActiv == false
                        && HaveThisPosInArrays(myPoses[i]) == false)
                    {
                        tab.CloseAtMarket(myPoses[i], myPoses[i].OpenVolume);
                    }
                }
            }
        }

        /// <summary>
        /// если у раба нет позиций - а у нас есть
        /// </summary>
        private void CloseBadPoses(Position pos, BotTabSimple tab)
        {
            if (pos.CloseActiv == true)
            {
                tab.CloseAllOrderToPosition(pos);
                Thread.Sleep(500);
            }

            if (pos.State == PositionStateType.Done)
            {
                return;
            }

            decimal price = 0;

            if (pos.Direction == Side.Buy)
            {
                price = tab.PriceBestAsk + tab.Securiti.PriceStep * 30;
            }
            else if (pos.Direction == Side.Sell)
            {
                price = tab.PriceBestBid - tab.Securiti.PriceStep * 30;
            }

            tab.CloseAtMarket(pos, pos.OpenVolume);

            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == pos.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(pos);
            }
        }

        /// <summary>
        /// открываем дополнительные позиции
        /// </summary>
        private void CheckDopPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuy = false;
            bool haveSell = false;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Sell)
                {
                    haveSell = true;
                }
                if (slavePoses[i].Direction == Side.Buy)
                {
                    haveBuy = true;
                }
            }

            if (haveSell && haveBuy)
            {
                return;
            }

            if (slavePoses.Count == 0 ||
                myPoses.Count == 0)
            {
                return;
            }

            if (slavePoses[0].Direction != myPoses[0].Direction)
            {
                return;
            }

            if (myPoses.Count >= slavePoses.Count)
            {
                return;
            }

            UpdateDepositBalance();
            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (tab.PositionsOpenAll.Count >= MaxPositionDuplicateCount.ValueInt)
                {
                    return;
                }
                OpenPose(slavePoses[i], tab);
            }

        }

        private void OpenPose(Position pos, BotTabSimple tab)
        {
            if (_numsPositionAlreadyUsed.Find(p => p.TimeOpen == pos.TimeOpen) != null)
            {
                return;
            }

            _numsPositionAlreadyUsed.Add(pos);

            if (pos.State == PositionStateType.Open ||
                pos.State == PositionStateType.Opening)
            {
                if (pos.Direction == Side.Buy)
                {
                    decimal price = pos.EntryPrice;

                    if (pos.EntryPrice == 0)
                    {
                        price = pos.OpenOrders[0].Price;
                    }

                    decimal securityPrice = tab.PriceBestAsk;

                    if (price > securityPrice)
                    {
                        decimal lenFromSecPrice = Math.Abs(price - securityPrice) / (price / 100);

                        if (lenFromSecPrice > 2)
                        {
                            price = securityPrice;
                        }
                    }

                    price += (SlippageInter.ValueDecimal * pos.EntryPrice / 100);
                    Position posMy = tab.BuyAtMarket(GetBuyVolume());

                    if (posMy != null)
                    {
                        _positionsToSupportOpenFirstTime.Add(posMy);
                    }
                }
                if (pos.Direction == Side.Sell)
                {
                    decimal price = pos.EntryPrice;

                    if (pos.EntryPrice == 0)
                    {
                        price = pos.OpenOrders[0].Price;
                    }

                    decimal securityPrice = tab.PriceBestBid;

                    if (price < securityPrice)
                    {
                        decimal lenFromSecPrice = Math.Abs(securityPrice - price) / (price / 100);

                        if (lenFromSecPrice > 2)
                        {
                            price = securityPrice;
                        }
                    }

                    price -= (SlippageInter.ValueDecimal * pos.EntryPrice / 100);
                    Position posMy = tab.SellAtMarket(GetSellVolume());
                    if (posMy != null)
                    {
                        _positionsToSupportOpenFirstTime.Add(posMy);
                    }
                }
            }
        }

        List<Position> _numsPositionAlreadyUsed = new List<Position>();

        private decimal GetBuyVolume()
        {
            return GetVolume(Side.Buy);
        }

        private decimal GetSellVolume()
        {
            return GetVolume(Side.Sell);
        }

        private decimal GetVolume(Side side)
        {
            decimal volume = VolumeOnPosition.ValueDecimal;
            // "Кол-во контрактов", "Валюта контракта", "% от Общего объёма портфеля"

            VolumeRegimeType volumeRegimeType = GetVolumeRegimeType();
            if (volumeRegimeType == VolumeRegimeType.CONTRACT_CURRENCY || volumeRegimeType == VolumeRegimeType.PORTFOLIO_PERCENT)
            {
                decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
                decimal slippage = price * SlippageInter.ValueDecimal / 100;
                price = side == Side.Buy ? price + slippage : price - slippage;

                if (volumeRegimeType == VolumeRegimeType.CONTRACT_CURRENCY)
                {
                    volume = Math.Round(volume / price, VolumeDecimals.ValueInt);
                }
                else if (volumeRegimeType == VolumeRegimeType.PORTFOLIO_PERCENT)
                {
                    decimal portfolioPercent = VolumeOnPosition.ValueDecimal;
                    volume = Math.Round(StaticPortfolioValue / 100 * portfolioPercent / price, VolumeDecimals.ValueInt);
                }
            }

            return volume;
        }

        private VolumeRegimeType GetVolumeRegimeType()
        {
            VolumeRegimeType volumeRegimeType = VolumeRegimeType.CONTRACTS_NUMBER;
            if (VolumeRegime.ValueString == "Валюта контракта")
            {
                volumeRegimeType = VolumeRegimeType.CONTRACT_CURRENCY;
            }
            else if (VolumeRegime.ValueString == "% от Общего объёма портфеля")
            {
                volumeRegimeType = VolumeRegimeType.PORTFOLIO_PERCENT;
            }
            return volumeRegimeType;
        }

        #endregion

        #region Сранвивание оригинальных позиций из тестера с тем что есть в реале

        public List<PositionReport> PositionReports = new List<PositionReport>();

        private DateTime _lastTimeSaveReport;

        private void SaveCompareDataInPositions(List<Position> slavePositions, List<Position> realPositions, DateTime time)
        {
            if (slavePositions == null ||
                slavePositions.Count == 0)
            {
                return;
            }

            if (_lastTimeSaveReport.AddSeconds(30) > time)
            {
                return;
            }

            _lastTimeSaveReport = time;

            for (int i = 0; i < slavePositions.Count; i++)
            {
                if (slavePositions[i].State == PositionStateType.OpeningFail ||
                    slavePositions[i].EntryPrice == 0)
                {
                    continue;
                }

                PositionReport report =
                    PositionReports.Find(p => p.TimeOpenSlave == slavePositions[i].TimeOpen);

                if (report == null)
                {
                    report = new PositionReport();
                    report.PosNumSlave = slavePositions[i].Number;
                    report.EnterPriceSlave = slavePositions[i].EntryPrice;
                    report.TimeOpenSlave = slavePositions[i].TimeOpen;
                    report.Side = slavePositions[i].Direction;
                    PositionReports.Add(report);
                }

                if (slavePositions[i].ClosePrice != 0 && report.ExitPriceSlave == 0)
                {
                    report.ExitPriceSlave = slavePositions[i].ClosePrice;
                    report.TimeExitSlave = slavePositions[i].TimeClose;
                    SavePositionReports();
                }
            }

            if (realPositions == null ||
                realPositions.Count == 0)
            {
                return;
            }

            for (int i = 0; i < realPositions.Count; i++)
            {
                Position posReal = realPositions[i];

                if (string.IsNullOrEmpty(posReal.SignalTypeOpen))
                {
                    continue;
                }

                Position posSlave = null;

                try
                {
                    posSlave = slavePositions.Find(p =>
                    p.TimeOpen == Convert.ToDateTime(posReal.SignalTypeOpen));
                }
                catch
                {
                    // ignore
                }

                if (posSlave == null)
                {
                    continue;
                }
                PositionReport report = PositionReports.Find(p =>
                    p.TimeOpenSlave == Convert.ToDateTime(posReal.SignalTypeOpen));

                CheckReport(posReal, posSlave, report);
            }
        }

        private void CheckReport(Position posReal, Position posSlave, PositionReport report)
        {
            // сначала смотрим цену закрытия, если такая есть в слайв позиции


            if (posReal == null ||
                posReal.State == PositionStateType.OpeningFail)
            {
                return;
            }

            if (posReal.State == PositionStateType.Done)
            {
                report.TimeExitReal = posReal.TimeClose;
                report.TimeOpenReal = posReal.TimeOpen;
                report.EnterPriceReal = posReal.EntryPrice;
                report.ExitPriceReal = posReal.ClosePrice;
                report.PosNumReal = posReal.Number;

                SavePositionReports();
            }
        }

        private void SavePositionReports()
        {
            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\" + NameStrategyUniq + @"PositionReports.txt", false)
                )
                {
                    for (int i = 0; i < PositionReports.Count; i++)
                    {
                        writer.WriteLine(PositionReports[i].GetSaveString());
                    }

                    writer.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }

        }

        private void LoadPositionReports()
        {
            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }
            if (!File.Exists(@"Engine\" + NameStrategyUniq + @"PositionReports.txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\" + NameStrategyUniq + @"PositionReports.txt"))
                {

                    while (reader.EndOfStream == false)
                    {
                        PositionReport report = new PositionReport();
                        report.LoadFromString(reader.ReadLine());
                        PositionReports.Add(report);
                    }

                    reader.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public void DeleteCurReports()
        {
            if (File.Exists(@"Engine\" + NameStrategyUniq + @"PositionReports.txt"))
            {
                File.Delete(@"Engine\" + NameStrategyUniq + @"PositionReports.txt");
            }
            PositionReports.Clear();
        }

        #endregion

        #region Дополнительное сопровождение позиции

        /*
                Логика отвечающая за сопровождение позиции.
                1.	Проскальзывание на вход – обычное проскальзывание

                2.	Проскальзывание на выход – обычное проскальзывание

                3.	Отклонение вход – т.к в этом боте мы работаем стопами и знаем, когда нужно войти или выйти из позции.
                Этот параметр, дает возможность чуть ранее войти в позицию.Условно по стопу бота мы должны войти по 100, 
                ставим тут 10 и входим по 90.

                4.	Отклонение выход – аналогично но на выход.

                5.	Секунд на вход – данный параметр, связан с отклонением исполнения и с зашитым параметром перевыставления ордеров.
                    Логика тут следующая. – Получили сигнал от бота, выставили ордер, он ушел в таблицу заявок квика
                    с указанным проскальзыванием.Если не исполнился в течение 3х секунд – сняли.
                    Посмотрели на параметр секунд на вход, если времени с момента первой точки входа(по ТС) прошло меньше чем указано, 
                    то смотрим параметр Отклонение исполнения , если цена ушла меньше от первоначальной точки входа чем указано 
                    в этом параметре, то используем Проскальзывание 2 на вход на выход чтобы войти в позицию.

                6.	Отклонение исполнения – указывается в пунктах.

                7.	Зашитый параметр переставления ордера – ставим заявку на 3 секунды, если не исполнилась снимаем. 
                И далее начинаем работать логика с Секундами на вход и Отклонением на вход.
        */

        private void AdminCapacityCreator_PositionProfitActivateEvent(Position position)
        {
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(position);
                TabsSimple[0].SetNewLogMessage(
                    "Позиция добавлена в сопровождение закрытия 1. Т.к. закрывается по профиту " + position.Number,
                    LogMessageType.System);
            }
        }

        private void AdminCapacityCreator_PositionStopActivateEvent(Position position)
        {
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(position);
                TabsSimple[0].SetNewLogMessage(
                    "Позиция добавлена в сопровождение закрытия 1. Т.к. закрывается по стопу " + position.Number,
                    LogMessageType.System);
            }
        }

        private void AdminCapacityCreator_PositionSellAtStopActivateEvent(Position position)
        {
            _positionsToSupportOpenFirstTime.Add(position);

            TabsSimple[0].SetNewLogMessage(
                "Позиция добавлена в сопровождение открытия 1. SellAtStop " + position.Number,
                LogMessageType.System);
        }

        private void AdminCapacityCreator_PositionBuyAtStopActivateEvent(Position position)
        {
            _positionsToSupportOpenFirstTime.Add(position);
            TabsSimple[0].SetNewLogMessage(
                "Позиция добавлена в сопровождение открытия 1. BuyAtStop " + position.Number,
                LogMessageType.System);
        }

        private List<Position> _positionsToSupportOpenFirstTime = new List<Position>();
        private List<Position> _positionsToSupportOpenSecondTime = new List<Position>();

        private List<Position> _positionsToSupportCloseFirstTime = new List<Position>();
        private List<Position> _positionsToSupportCloseSecondTime = new List<Position>();

        private bool HaveThisPosInArrays(Position position)
        {

            if (_positionsToSupportOpenFirstTime.Find(p => p.Number == position.Number) != null)
            {
                if (position.State == PositionStateType.Open)
                {
                    for (int i = 0; i < _positionsToSupportOpenFirstTime.Count; i++)
                    {
                        if (_positionsToSupportOpenFirstTime[i].Number == position.Number)
                        {
                            _positionsToSupportOpenFirstTime.RemoveAt(i);
                            return false;
                        }
                    }
                }
                return true;
            }
            if (_positionsToSupportOpenSecondTime.Find(p => p.Number == position.Number) != null)
            {
                if (position.State == PositionStateType.Open)
                {
                    for (int i = 0; i < _positionsToSupportOpenSecondTime.Count; i++)
                    {
                        if (_positionsToSupportOpenSecondTime[i].Number == position.Number)
                        {
                            _positionsToSupportOpenSecondTime.RemoveAt(i);
                            return false;
                        }
                    }
                }
                return true;
            }
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) != null)
            {
                return true;
            }
            if (_positionsToSupportCloseSecondTime.Find(p => p.Number == position.Number) != null)
            {
                return true;
            }


            return false;
        }

        private void PositionSupportThread()
        {
            Load(_positionsToSupportOpenFirstTime, "firstOpen");
            Load(_positionsToSupportOpenSecondTime, "secondOpen");
            Load(_positionsToSupportCloseFirstTime, "firstClose");
            Load(_positionsToSupportCloseSecondTime, "secondClose");

            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }

            while (true)
            {
                SupportLogic(DateTime.Now);
                Thread.Sleep(300);
            }
        }

        private DateTime _lastSupportCheckTime;

        private void SupportLogic(DateTime lastTime)
        {
            if (_lastSupportCheckTime.AddSeconds(1) > lastTime)
            {
                return;
            }

            _lastSupportCheckTime = lastTime;

            if (MainWindow.ProccesIsWorked == false)
            {
                return;
            }

            try
            {
                if (IsConnected == false)
                {
                    return;
                }

                if (TabsSimple == null || TabsSimple.Count == 0)
                {
                    return;
                }

                BotTabSimple tab = TabsSimple[0];

                // удаляем из массивов позиции которые были завершены
                // очищаем второй уровень позиций которые дважны не открылись

                TryRemovePosAsDone(_positionsToSupportOpenFirstTime);
                TryRemovePosAsDone(_positionsToSupportOpenSecondTime);
                TryRemovePosAsDone(_positionsToSupportCloseFirstTime);
                TryRemovePosAsDone(_positionsToSupportCloseSecondTime);

                // удаляем из поддержки открывающие позиции если они открылись

                TryRemoveOpeningPositionAsOpen(_positionsToSupportOpenFirstTime);
                TryRemoveOpeningPositionAsOpen(_positionsToSupportOpenSecondTime);

                // открытия сначала отзываем ордера которые отжили своё

                CanselationOrder(_positionsToSupportOpenFirstTime, tab);
                CanselationOrder(_positionsToSupportOpenSecondTime, tab);

                CanselationOrder(_positionsToSupportCloseFirstTime, tab);
                CanselationOrder(_positionsToSupportCloseSecondTime, tab);

                // открытия переоткрываем ордера с первого уровня с новыми проскальзываниями переносим позиции из одного массива в другой

                TryReOpenPositionAndMoveInNextLevel(
                    _positionsToSupportOpenFirstTime, _positionsToSupportOpenSecondTime, tab);

                // Теперь отзываем ордера второго уровня поддержки

                TryRemovePosAsOpeningFail(_positionsToSupportOpenSecondTime);

                // закрытия. Перезакрываем ордера первого уровня

                TryReClosePositionAndMoveInNextLevel(_positionsToSupportCloseFirstTime,
                    _positionsToSupportCloseSecondTime, tab);

                TryRemovePosAsClosingFail(_positionsToSupportCloseSecondTime);

                // теперь закрытия
                // отзываем через N времени из первого массива
                // перезакрываем с проскальзыванием 2. Переносим в массив 2.
                // отзываем через N времени из второго, в третий
                // в третьем закрываем тогда когда у робота нет совсем позиций или противоположная

                SavePosArrays(_positionsToSupportOpenFirstTime, "firstOpen");
                SavePosArrays(_positionsToSupportOpenSecondTime, "secondOpen");
                SavePosArrays(_positionsToSupportCloseFirstTime, "firstClose");
                SavePosArrays(_positionsToSupportCloseSecondTime, "secondClose");
            }
            catch (Exception error)
            {
                TabsSimple[0].SetNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void TryRemovePosAsDone(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.Done)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. Done " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void TryRemovePosAsOpeningFail(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.OpeningFail &&
                    positions[i].OpenActiv == false)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. OpeningFail " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void TryRemovePosAsClosingFail(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.ClosingFail &&
                    positions[i].CloseActiv == false)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. ClosingFail 2 раза " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void CanselationOrder(List<Position> positions, BotTabSimple tab)
        {
            if (positions.Count == 0)
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                Order ord = GetOpenOrder(positions[i]);

                if (ord == null)
                {
                    continue;
                }

                if (_cancelledOrders.Find(o => o.NumberUser == ord.NumberUser) != null)
                {
                    continue;
                }

                if (ord.TimeCreate != DateTime.MinValue &&
                    ord.TimeCreate.AddSeconds(OpenOrderLifeTime.ValueInt) < tab.TimeServerCurrent)
                {
                    tab.CloseAllOrderToPosition(positions[i]);
                    _cancelledOrders.Add(ord);
                }
            }
        }

        private List<Order> _cancelledOrders = new List<Order>();

        private Order GetOpenOrder(Position pos)
        {
            if (pos.OpenActiv)
            {
                return pos.OpenOrders.Find(o => o.State == OrderStateType.Activ);
            }
            if (pos.CloseActiv)
            {
                return pos.CloseOrders.Find(o => o.State == OrderStateType.Activ);
            }

            return null;
        }

        private void TryRemoveOpeningPositionAsOpen(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.Open)
                {
                    Position pos = positions[i];
                    positions.RemoveAt(i);
                    TabsSimple[0].SetNewLogMessage(
                     "Позиция удалена из доп сопровождения, т.к. Open" + pos.Number,
                  LogMessageType.System);
                    i--;
                }
            }
        }

        private void TryReOpenPositionAndMoveInNextLevel(
            List<Position> positionsFirst,
            List<Position> positionsSecond,
            BotTabSimple tab)
        {
            for (int i = 0; i < positionsFirst.Count; i++)
            {

                if (positionsFirst[i].State == PositionStateType.OpeningFail &&
                    positionsFirst[i].OpenActiv == false)
                {
                    Position pos = positionsFirst[i];

                    if (CanReOpen(pos, tab))
                    {
                        TabsSimple[0].SetNewLogMessage(
                            "Позиция перенесена из доп сопровождения 1 в 2, т.к. можно пробовать переоткрывать " + pos.Number,
                            LogMessageType.System);
                        positionsSecond.Add(pos);
                        if (pos.Direction == Side.Buy)
                        {
                            tab.BuyAtLimitToPosition(
                                pos,
                                tab.PriceBestAsk + (SlippageSecondInter.ValueDecimal * tab.PriceBestAsk / 100),
                                pos.OpenOrders[0].Volume);
                        }
                        if (pos.Direction == Side.Sell)
                        {
                            tab.SellAtLimitToPosition(
                                pos,
                                tab.PriceBestBid - (SlippageSecondInter.ValueDecimal * tab.PriceBestBid / 100),
                                pos.OpenOrders[0].Volume);
                        }
                    }
                    else
                    {
                        DateTime firstTime = pos.OpenOrders[0].TimeCreate;

                        if (firstTime.AddSeconds(InterTime.ValueInt) < tab.TimeServerCurrent)
                        {
                            positionsFirst.RemoveAt(i);
                            i--;

                            TabsSimple[0].SetNewLogMessage(
                           "Позиция НЕ перенесена из доп сопровождения 1 в 2, т.к. время на переоткрытие закончилось и мы не приблизились к цене открытия" + pos.Number,
                            LogMessageType.System);
                        }
                    }
                }
            }
        }

        private bool CanReOpen(Position pos, BotTabSimple tab)
        {
            /*
             Получили сигнал от бота, выставили ордер, он ушел в таблицу заявок квика
             с указанным проскальзыванием.Если не исполнился в течение 3х секунд – сняли.
             Посмотрели на параметр секунд на вход, если времени с момента первой точки входа(по ТС) прошло меньше чем указано,
             то смотрим параметр Отклонение исполнения, если цена ушла меньше от первоначальной точки входа чем указано
             в этом параметре, то используем Проскальзывание 2 на вход на выход чтобы войти в позицию.
            */

            if (pos.State == PositionStateType.Opening)
            {
                return true;
            }

            DateTime firstTime = pos.OpenOrders[0].TimeCreate;

            if (firstTime.AddSeconds(InterTime.ValueInt) < tab.TimeServerCurrent)
            {
                return false;
            }

            decimal price = pos.OpenOrders[0].Price;

            if (StartProgram == StartProgram.IsOsTrader)
            {
                if (pos.Direction == Side.Buy &&
                    (Math.Abs(price - tab.PriceBestAsk) / price * 100)
                    > MaxOrderExecutionDeviation.ValueDecimal)

                {
                    return false;
                }

                if (pos.Direction == Side.Sell &&
                    (Math.Abs(price - tab.PriceBestBid) / price * 100)
                    > MaxOrderExecutionDeviation.ValueDecimal)
                {
                    return false;
                }
            }

            return true;
        }

        private void TryReClosePositionAndMoveInNextLevel(
            List<Position> positionsFirst,
            List<Position> positionsSecond,
            BotTabSimple tab)
        {
            for (int i = 0; i < positionsFirst.Count; i++)
            {
                if (positionsFirst[i].State == PositionStateType.ClosingFail &&
                    positionsFirst[i].CloseActiv == false)
                {
                    Position pos = positionsFirst[i];
                    positionsFirst.RemoveAt(i);
                    i--;
                    positionsSecond.Add(pos);

                    TabsSimple[0].SetNewLogMessage(
                        "Позиция перенесена из доп сопровождения закрытия 1 в 2, т.к. ClosingFail. Пробуем перезакрывать " + pos.Number,
                        LogMessageType.System);

                    if (pos.Direction == Side.Buy)
                    {
                        tab.CloseAtLimit(
                            pos,
                            tab.PriceBestBid - (SlippageSecondExit.ValueDecimal * tab.PriceBestBid / 100),
                            pos.OpenVolume);
                    }
                    if (pos.Direction == Side.Sell)
                    {
                        tab.CloseAtLimit(
                            pos,
                            tab.PriceBestAsk + (SlippageSecondExit.ValueDecimal * tab.PriceBestAsk / 100),
                            pos.OpenVolume);
                    }
                }
            }
        }

        private void SavePosArrays(List<Position> positions, string name)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\" + NameStrategyUniq + name + @"SupportPoses.txt", false)
                )
                {
                    for (int i = 0; i < positions.Count; i++)
                    {
                        writer.WriteLine(positions[i].GetStringForSave());
                    }

                    writer.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private void Load(List<Position> positions, string name)
        {
            if (!File.Exists(@"Engine\" + NameStrategyUniq + name + @"SupportPoses.txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\" + NameStrategyUniq + name + @"SupportPoses.txt"))
                {
                    while (reader.EndOfStream == false)
                    {
                        Position pos = new Position();
                        pos.SetDealFromString(reader.ReadLine());
                        positions.Add(pos);
                    }

                    reader.Close();
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        #endregion

        private void UpdateDepositBalance()
        {
            try
            {
                string apiKey;
                string secretKey;
                ReadAccountCredentials(out apiKey, out secretKey);
                Thread.Sleep(1000);
                decimal balance = DepositBalanceReader.ReadDepositBalance(apiKey, secretKey);
                if (balance > 0)
                {
                    StaticPortfolioValue = balance;
                    SaveStaticPortfolio();
                }
            }
            catch { }
        }

        private static void ReadAccountCredentials(out string apiKey, out string secretKey)
        {
            const string FILEPATH = "Engine\\BinanceFuturesParams.txt";
            const string API_KEY_TOKEN = "Публичный ключ";
            const string SECRET_KEY_TOKEN = "Секретный ключ";

            apiKey = null;
            secretKey = null;

            try
            {
                if (File.Exists(FILEPATH))
                {
                    using (StreamReader reader = new StreamReader(FILEPATH))
                    {
                        while (!reader.EndOfStream)
                        {
                            string line = reader.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                if (line.Contains(API_KEY_TOKEN))
                                {
                                    List<string> apiKeyItems = new List<string>(line.Split('^'));
                                    if (apiKeyItems.Any())
                                    {
                                        apiKey = apiKeyItems.Last();
                                    }
                                }
                                if (line.Contains(SECRET_KEY_TOKEN))
                                {
                                    List<string> secretKeyItems = new List<string>(line.Split('^'));
                                    if (secretKeyItems.Any())
                                    {
                                        secretKey = secretKeyItems.Last();
                                    }
                                }
                            }
                            if (apiKey != null && secretKey != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }

    public class PositionReport
    {
        // данные общие

        public decimal DifferenceValueAbsolute
        {
            get
            {
                if (ProfitSlave == 0 &&
                    ProfitReal == 0)
                {
                    return 0;
                }
                return -(ProfitSlave - ProfitReal);
            }
        }

        public decimal DifferenceValuePercent
        {
            get
            {
                if (ProfitPercentSlave == 0 &&
                    ProfitPercentReal == 0)
                {
                    return 0;
                }
                return (ProfitPercentSlave - ProfitPercentReal);
            }
        }

        public Side Side;

        public string GetSaveString()
        {
            string saveStr = "";

            saveStr += Side.ToString() + ";";

            saveStr += PosNumSlave.ToString() + ";";
            saveStr += TimeOpenSlave.ToString() + ";";
            saveStr += EnterPriceSlave.ToString() + ";";
            saveStr += ExitPriceSlave.ToString() + ";";

            saveStr += PosNumReal.ToString() + ";";
            saveStr += TimeOpenReal.ToString() + ";";
            saveStr += EnterPriceReal.ToString() + ";";
            saveStr += ExitPriceReal.ToString() + ";";

            saveStr += TimeExitSlave.ToString() + ";";
            saveStr += TimeExitReal.ToString() + ";";

            return saveStr;
        }

        public void LoadFromString(string str)
        {
            string[] save = str.Split(';');

            Enum.TryParse(save[0], out Side);

            PosNumSlave = Convert.ToInt32(save[1]);
            TimeOpenSlave = Convert.ToDateTime(save[2]);
            EnterPriceSlave = save[3].ToDecimal();
            ExitPriceSlave = save[4].ToDecimal();

            PosNumReal = Convert.ToInt32(save[5]);
            TimeOpenReal = Convert.ToDateTime(save[6]);
            EnterPriceReal = save[7].ToDecimal();
            ExitPriceReal = save[8].ToDecimal();

            TimeExitSlave = Convert.ToDateTime(save[9]);
            TimeExitReal = Convert.ToDateTime(save[10]);
        }

        // данные по рабу

        public int PosNumSlave;

        public DateTime TimeOpenSlave;

        public DateTime TimeExitSlave;

        public decimal EnterPriceSlave;

        public decimal ExitPriceSlave;

        public decimal ProfitSlave
        {
            get
            {
                if (Side == Side.Buy)
                {
                    return ExitPriceSlave - EnterPriceSlave;
                }
                else
                {
                    return EnterPriceSlave - ExitPriceSlave;
                }
            }
        }

        public decimal ProfitPercentSlave
        {
            get
            {
                if (EnterPriceSlave == 0)
                {
                    return 0;
                }

                decimal absolute = ProfitSlave;

                return ProfitSlave / EnterPriceSlave * 100;
            }
        }

        // данные по реальной позиции

        public int PosNumReal;

        public DateTime TimeOpenReal;

        public DateTime TimeExitReal;

        public decimal EnterPriceReal;

        public decimal ExitPriceReal;

        public decimal ProfitReal
        {
            get
            {
                if (Side == Side.Buy)
                {
                    return ExitPriceReal - EnterPriceReal;
                }
                else
                {
                    return EnterPriceReal - ExitPriceReal;
                }
            }
        }

        public decimal ProfitPercentReal
        {
            get
            {
                if (EnterPriceReal == 0)
                {
                    return 0;
                }

                decimal absolute = ProfitReal;

                return ProfitReal / EnterPriceReal * 100;
            }
        }

    }

    internal enum VolumeRegimeType
    {
        CONTRACTS_NUMBER,
        CONTRACT_CURRENCY,
        PORTFOLIO_PERCENT
    }

    internal static class DepositBalanceReader
    {
        public static decimal ReadDepositBalance(string apiKey, string secretKey)
        {
            decimal balanceUSDT = -1m;

            try
            {
                string accountDetailsString = MakeBinanceRequest("GET", BuildAccountUrl(secretKey), apiKey);
                if (!String.IsNullOrWhiteSpace(accountDetailsString))
                {
                    accountDetailsString = accountDetailsString.Replace(" ", String.Empty).ToLower();
                    int usdtBalanceObjectStartIndex = accountDetailsString.IndexOf("\"asset\":\"usdt\"");
                    string accountDetailsStringStartedFromUsdtObject = accountDetailsString.Substring(usdtBalanceObjectStartIndex);
                    string usdtBalanceObjectString = accountDetailsStringStartedFromUsdtObject.Substring(0, accountDetailsStringStartedFromUsdtObject.IndexOf("}"));
                    int balanceKeyStartIndex = usdtBalanceObjectString.IndexOf("crosswalletbalance");
                    string usdtBalanceObjectStringStartedFromBalanceKey = usdtBalanceObjectString.Substring(balanceKeyStartIndex);
                    string balanceKeyValuePairString = usdtBalanceObjectStringStartedFromBalanceKey.Substring(0, usdtBalanceObjectStringStartedFromBalanceKey.IndexOf(","));
                    int semicolonIndex = balanceKeyValuePairString.IndexOf(":");
                    string balanceValueString = balanceKeyValuePairString.Substring(semicolonIndex).Replace(":", String.Empty).Replace("\"", String.Empty);
                    if (!String.IsNullOrWhiteSpace(balanceValueString))
                    {
                        string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                        string symbolToAvoidParsingErrors = decimalSeparator == "." ? "," : ".";
                        balanceValueString = balanceValueString.Replace(symbolToAvoidParsingErrors, decimalSeparator);
                    }
                    balanceUSDT = Convert.ToDecimal(balanceValueString);
                }
            }
            catch { }

            return balanceUSDT;
        }

        private static string BuildAccountUrl(string secretKey)
        {
            string host = "fapi.binance.com";
            string operationUrl = "/fapi/v2/account";
            long timestamp = GetNowUtcTimeAsMillis();
            string queryString = String.Format("recvWindow=55000&timestamp={0}", timestamp.ToString());
            string signature = CreateSignatureHMacSha256(queryString, secretKey);
            return String.Format("https://{0}{1}?{2}&signature={3}", host, operationUrl, queryString, signature);
        }

        private static long GetNowUtcTimeAsMillis()
        {
            DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime nowUtcDate = TimeZone.CurrentTimeZone.ToUniversalTime(DateTime.Now);
            return (long)(nowUtcDate - Jan1St1970).TotalMilliseconds;
        }

        private static string CreateSignatureHMacSha256(string messageToSign, string secretKey)
        {
            string signature = String.Empty;
            try
            {
                ASCIIEncoding encoding = new ASCIIEncoding();
                byte[] keyBytes = encoding.GetBytes(secretKey);
                byte[] messageBytes = encoding.GetBytes(messageToSign);
                using (HMACSHA256 cryptographer = new HMACSHA256(keyBytes))
                {
                    signature = BitConverter.ToString(cryptographer.ComputeHash(messageBytes)).Replace("-", String.Empty).ToLower();
                }
            }
            catch { }
            return signature;
        }

        private static string MakeBinanceRequest(string method, string fullUrl, string apiKey)
        {
            HttpStatusCode? statusCode;
            WebHeaderCollection responseHeaders;
            Dictionary<string, string> headers = new Dictionary<string, string>() { { "X-MBX-APIKEY", apiKey } };
            return MakeRequest(method, fullUrl, headers, null, out statusCode, out responseHeaders);
        }

        private static string MakeRequest(
            string method,
            string fullUrl,
            Dictionary<string, string> headers,
            byte[] content,
            out HttpStatusCode? statusCode,
            out WebHeaderCollection responseHeaders
        )
        {
            statusCode = null;
            responseHeaders = null;
            string responseBody = null;

            try
            {
                HttpWebRequest request = WebRequest.Create(fullUrl) as HttpWebRequest;
                request.Method = method;
                request.Proxy = null;
                if (headers != null && headers.Count > 0)
                {
                    foreach (string headerName in headers.Keys)
                    {
                        request.Headers[headerName] = headers[headerName];
                    }
                }
                if (content != null)
                {
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(content, 0, content.Length);
                    }
                }
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    if (response != null)
                    {
                        statusCode = response.StatusCode;
                        responseHeaders = response.Headers;
                        using (StreamReader responseStreamReader = new StreamReader(response.GetResponseStream()))
                        {
                            responseBody = responseStreamReader.ReadToEnd();
                            responseStreamReader.Close();
                        }
                    }
                    response.Close();
                }
            }
            catch (WebException ex)
            {
                using (HttpWebResponse errorResponse = ex.Response as HttpWebResponse)
                {
                    if (errorResponse != null)
                    {
                        statusCode = errorResponse.StatusCode;
                        using (StreamReader errorResponseStreamReader = new StreamReader(errorResponse.GetResponseStream()))
                        {
                            responseBody = errorResponseStreamReader.ReadToEnd();
                            errorResponseStreamReader.Close();
                        }
                    }
                    else if (ex.Status == WebExceptionStatus.Timeout)
                    {
                        statusCode = HttpStatusCode.RequestTimeout;
                    }

                    if (errorResponse != null)
                    {
                        errorResponse.Close();
                    }
                }
            }
            catch { }

            return responseBody;
        }
    }
}