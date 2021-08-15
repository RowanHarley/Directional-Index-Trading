// Copyright QUANTOWER LLC. © 2017-2021. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using TradingPlatform.BusinessLayer;

namespace Directional_Index_Trading
{
    /// <summary>
    /// A blank strategy. Add your code, compile it and run via Strategy Runner panel in the assigned trading terminal.
    /// Information about API you can find here: http://api.quantower.com
    /// Code samples: https://github.com/Quantower/Examples
    /// </summary>
    public class Directional_Index_Trading : Strategy
    {
        
        #region Parameters

        [InputParameter("Micro Symbol", 10)]
        public Symbol symbol1;

        /*[InputParameter("Mini Symbol")]
        public Symbol symbol2;*/

        [InputParameter("Account", 20)]
        public Account account;

        [InputParameter("Limit Placement against previous candle low", 30)]
        public int x = 0;


        // Displays Input Parameter as input field (or checkbox if value type is bolean).
        [InputParameter("DMI Indicator Period", 0, 1, 999, 1, 0)]
        public int Period2 = 14;

        [InputParameter("Daily Loss Limit %", 0, 0, 100, 0.1, 1)]
        public double LimMax = 10;

        // Displays Input Parameter as dropdown list.
        [InputParameter("Type of Moving Average", 1, variants: new object[] {
            "Simple", MaMode.SMA,
            "Exponential", MaMode.EMA,
            "Modified", MaMode.SMMA,
            "Linear Weighted", MaMode.LWMA}
        )]
        public MaMode MAType = MaMode.SMA;
        [InputParameter("How does risk change on losing trade? ", variants: new object[] {
            "Risk Increases", -1,
            "Risk Decreases", 1
        })]
        public int RiskDir = 1;

        [InputParameter("Short Only?")]
        public bool ShortOnly = false;

        [InputParameter("Reward ratio", 0, 1, 100, .5, 1)]
        public double y = 6;

        [InputParameter("Random Number for candle length? ")]
        public bool RandomTime = false;

        [InputParameter("Risk Percent per Trade", 0, 1, 100, .01, 2)]
        public double maxRisk = 2.25;

        [InputParameter("TIF", 5)]
        public TimeInForce time = TimeInForce.GTT;

        [InputParameter("Bars before cancel", 0, 1, 100)]
        public double MaxBars = 3;

        [InputParameter("ATR Indicator Dividor", 0, 0.01, 100)]
        public double z = 1;

        [InputParameter("Candle Time if not Random", 0, 1, 180)]
        public int CandleTime;

        [InputParameter("Choice of Lower close method", variants: new object[] {
            "Start Stop Loss X points above order price", 0,
            "Trailing Stop Loss", 1,
            "Breakeven with TP", 2,
            "Let Winners run", 3}
        )]
        public int ChoiceofStop;

        [InputParameter("Points above Order Price")]
        public double PtsAbove = 0;
        /*[InputParameter("Minutes before opening new trade")]
        public double minutesbeforenew = 20;*/

        [InputParameter("Maintenance Margin")]
        public double maintm = 900;

        [InputParameter("Day Trade Margin")]
        public double daym = 50;

        [InputParameter("PnL per Tick")]
        public double pricePerTick = 0.5;
        [InputParameter("Minimum Tick SL")]
        public int minTicks = 4;

        private HistoricalData _historicalSecData;
        public Indicator indicatorDMI;
        public Indicator indicatorATR;
        
        
        private TradingOperationResult _operationResult;
        private DateTime MarketClose = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 21, 0, 0);
        private DateTime MarketOpen = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 22, 0, 0);
        private DateTime LowMovementBegin;
        private DateTime LowMovementFinish;
        private bool aboveBE = false;
        private double totalPos = 0;
        private double sharpe;
        private double longP = 0;
        private double ShortP = 0;
        private bool hasSetTime = false;
        private int len;
        private double stdDev;
        public double OrderAmount;
        private double CurrOrderPrice;
        public double prevHigh;
        public double prevHigh2;
        public double prevLow;
        public double prevLow2;
        public double slPrice = 0;
        private int lossStreak = 0;
        public int numBars;
        public double startAccVal;
        private int rndNum;
        public double trailPrice;
        private double BEPtsAbove;
        private double CurrMaxRisk;
        private DateTime TimeAfterPos;
        private int Tradesat0Risk;
        #endregion Parameters

        public Order currentSL;
        public Directional_Index_Trading()
            : base()
        {
            // Defines indicator's group, name and description.
            this.Name = "Buy/Sell on DMI";
            this.Description = "This strategy buys and sells when DMI flattens";
        }

        protected override void OnCreated()
        {
            base.OnCreated();

            this.indicatorDMI = Core.Indicators.BuiltIn.DMI(Period2, MAType);
            this.indicatorATR = Core.Indicators.BuiltIn.ATR(Period2, MAType);
        }

        protected override void OnRun()
        {
            CurrMaxRisk = maxRisk;
            if (symbol1 == null || account == null || symbol1.ConnectionId != account.ConnectionId)
            {
                Log("Incorrect input parameters... symbol1 or Account are not specified or they have diffent connectionID.", StrategyLoggingLevel.Error);
                return;
            }
            account.NettingType = NettingType.OnePosition;
            
            var vendorName = Core.Connections.Connected.FirstOrDefault(c => c.Id == symbol1.ConnectionId);
            var isLimitSupported = Core.GetOrderType(OrderType.Limit, symbol1.ConnectionId) != null;
            startAccVal = account.Balance;
            Log("Account Beginning Value: " + startAccVal);

            if (!isLimitSupported && vendorName != null)
            {
                Log($"The '{vendorName}' doesn't support '{OrderType.Limit}' order type.", StrategyLoggingLevel.Error);
                return;
            }
            Random rnd = new Random();
            rndNum = RandomTime == true ? rnd.Next(1, 180) : CandleTime;
            _historicalSecData = symbol1.GetHistory( new HistoryAggregationHeikenAshi(HeikenAshiSource.Second, rndNum), HistoryType.Last ,DateTime.Now.AddMinutes(-30));
            
            _historicalSecData.AddIndicator(indicatorDMI);
            _historicalSecData.AddIndicator(indicatorATR);

            symbol1.NewQuote += this.OnQuote;

            Core.TradeAdded += UpdateTime;
            trailPrice = slPrice; //Math.Ceiling(indicatorATR.GetValue(1)/3);
            
            _historicalSecData.NewHistoryItem += this.historicalData_NewHistoryItem;
            Log("Strategy has began running");
            /*var timer = new System.Threading.Timer((e) =>
            {
                UpdateTime();
            }, null, TimeSpan.Zero, TimeSpan.FromHours(8));*/
        }

        private void UpdateTime(Trade pos)
        {
            var symbLast = symbol1.LastDateTime.FromSelectedTimeZoneToUtc();
            LowMovementBegin = new DateTime(symbLast.Year, symbLast.Month, symbLast.Day, 20, 30, 0);
            LowMovementFinish = LowMovementBegin.Add(new TimeSpan(11, 15, 0));
            ChangeLStreak(pos);
        }


        private void historicalData_NewHistoryItem(object sender, HistoryEventArgs e)
        {
            numBars++;
            if (account.Balance <= ((100-LimMax)/100) * startAccVal)
            {
                Log("Account Value exceeds daily loss limit");
                Stop();
            }

            StrategyProcess();

        }

        private void StrategyProcess()
        {
            

            if (Core.Positions.Length == 0 && Core.Orders.Length > 0)
            {
                if (numBars >= MaxBars)
                {
                    foreach (Order o in Core.Orders) { 
                        o.Cancel();
                    }
                    numBars = 0;
                }
            }

            if (Math.Round(indicatorDMI.GetValue(1, 0), 5) == Math.Round(indicatorDMI.GetValue(2, 0), 5) && Math.Round(indicatorDMI.GetValue(1, 1), 5) == Math.Round(indicatorDMI.GetValue(2, 1), 5))
            {
                prevHigh = ((HistoryItemBar)_historicalSecData[1]).High;
                prevHigh2 = ((HistoryItemBar)_historicalSecData[2]).High;
                prevLow = ((HistoryItemBar)_historicalSecData[1]).Low;
                prevLow2 = ((HistoryItemBar)_historicalSecData[2]).Low;
                if (Core.Instance.Positions.Length != 0)
                {
                    if (Core.Positions.Length > 1)
                    {
                        Log("Error: Too many positions opened", StrategyLoggingLevel.Error);
                        Stop();
                    }
                    
                   var posTimeOpen = DateTime.Now.Subtract(Core.Instance.Positions[0].OpenTime);
                    
                    if (posTimeOpen.TotalSeconds > 185 && Math.Round(indicatorDMI.GetValue(1, 0), 5) == Math.Round(indicatorDMI.GetValue(2, 0), 5) && Math.Round(indicatorDMI.GetValue(1, 1), 5) == Math.Round(indicatorDMI.GetValue(2, 1), 5))
                    {
                        TradingOperationResult result = Core.Instance.ClosePosition(new ClosePositionRequestParameters()
                        {
                            Position = Core.Instance.Positions[0],
                            CloseQuantity = Core.Instance.Positions[0].Quantity
                        });

                        //TimeAfterPos = symbol1.LastDateTime.AddMinutes(minutesbeforenew);

                        if (result.Status == TradingOperationResultStatus.Success)
                        {

                            foreach(Order o in Core.Orders)
                            {
                                o.Cancel();
                            }
                            Log($"{result.Status}. Position was closed & Orders have been Cancelled", StrategyLoggingLevel.Trading);

                            /*if(!ShortOnly && side == Side.Buy)
                            {
                                //CreateLimitOrder(Side.Sell, true, pos.Quantity);
                                CreateLimitOrder(Side.Sell, false);
                            }
                            else if(!ShortOnly)
                            {
                                //CreateLimitOrder(Side.Buy, true, pos.Quantity);
                                CreateLimitOrder(Side.Buy, false);
                            }*/
                        }
                    }
                }
                
                if (prevHigh2 > prevHigh && (((HistoryItemBar)_historicalSecData[1]).Close - ((HistoryItemBar)_historicalSecData[1]).Open) > 0 && (((HistoryItemBar)_historicalSecData[2]).Close - ((HistoryItemBar)_historicalSecData[2]).Open > 0))
                {
                    Log("Beginning Sell");
                    if (symbol1.LastDateTime.FromSelectedTimeZoneToUtc() > LowMovementBegin && symbol1.LastDateTime.FromSelectedTimeZoneToUtc() < LowMovementFinish)
                    {
                        Log("No trades will be executed as time within Low movement boundaries", StrategyLoggingLevel.Error);

                    }
                    else
                    {
                        CreateLimitOrder(Side.Sell, false);
                    }
                    
                }
                else if (prevLow2 < prevLow && (((HistoryItemBar)_historicalSecData[1]).Close - ((HistoryItemBar)_historicalSecData[1]).Open) < 0 && (((HistoryItemBar)_historicalSecData[2]).Close - ((HistoryItemBar)_historicalSecData[2]).Open < 0))
                {
                    Log("Beginning Buy");
                    if (symbol1.LastDateTime.FromSelectedTimeZoneToUtc() > LowMovementBegin && symbol1.LastDateTime.FromSelectedTimeZoneToUtc() < LowMovementFinish)
                    {
                        Log("No trades will be executed as time within Low movement boundaries", StrategyLoggingLevel.Error);

                    }
                    else
                    {
                        CreateLimitOrder(Side.Buy, false);
                    }
                    
                } else if (prevHigh == prevHigh2 && (((HistoryItemBar)_historicalSecData[1]).Close - ((HistoryItemBar)_historicalSecData[1]).Open) > 0 && (((HistoryItemBar)_historicalSecData[2]).Close - ((HistoryItemBar)_historicalSecData[2]).Open > 0))
                {
                    CreateLimitOrder(Side.Buy, false);
                } else if (prevLow2 == prevLow && (((HistoryItemBar)_historicalSecData[1]).Close - ((HistoryItemBar)_historicalSecData[1]).Open) < 0 && (((HistoryItemBar)_historicalSecData[2]).Close - ((HistoryItemBar)_historicalSecData[2]).Open < 0))
                {
                    CreateLimitOrder(Side.Sell, false);
                }
            }
        }

        public void CreateLimitOrder(Side side, bool isReversal, double Amm = 0)
        {
            

            if (Tradesat0Risk > 3)
            {
                CurrMaxRisk = maxRisk;
                Tradesat0Risk = 0;
                lossStreak = 0;
            }
            else if(lossStreak > 5|| Tradesat0Risk <= 3 && CurrMaxRisk <= 0)
            {
                Tradesat0Risk++;
                Log("Trades stopped. Not enough 0 risk trades", StrategyLoggingLevel.Error);
                return;
            }

            if (symbol1.LastDateTime.FromSelectedTimeZoneToUtc() > LowMovementBegin && symbol1.LastDateTime.FromSelectedTimeZoneToUtc() < LowMovementFinish)
            {
                Log("Low Movement area, no trade was executed", StrategyLoggingLevel.Trading);
                return;
            }
            
            
            /* else if (symbol1.LastDateTime < TimeAfterPos)
            {
                Log("Too Early to open another trade");
                return;
            }*/
            if (_operationResult != null)
            {
                if (Core.GetPositionById(_operationResult.OrderId, symbol1.ConnectionId) != null)
                    return;

                var order = Core.Orders.FirstOrDefault(o => o.ConnectionId == symbol1.ConnectionId && o.Id == _operationResult.OrderId);
                if (order != null)
                {
                    order.Cancel();
                    Log("Order was canceled.", StrategyLoggingLevel.Trading);
                }
            }
            var sign = (side == Side.Buy) ? -1 : 1;
            
            //var orderPrice = (side == Side.Buy) ? symbol1.Bid : symbol1.Ask;
            double orderPrice;
            orderPrice = (side == Side.Buy) ? symbol1.Bid : symbol1.Ask;
            //orderPrice = Math.Round(((side == Side.Buy ? ((HistoryItemBar)_historicalSecData[1]).Low : ((HistoryItemBar)_historicalSecData[1]).High) + sign * x) * symbol1.TickSize, MidpointRounding.ToEven)/symbol1.TickSize;
            
            Log("Order Price: " + orderPrice);

            var lowOrder = orderPrice - prevLow2 + (x * symbol1.TickSize);
            var highOrder = prevHigh2 - orderPrice + (x * symbol1.TickSize);

            slPrice = (side == Side.Buy) ? (new[] { /*lowOrder*/minTicks*symbol1.TickSize, indicatorATR.GetValue(1)/z}.Max()) : (new[] { /*highOrder*/ minTicks*symbol1.TickSize, indicatorATR.GetValue(1)/z}.Max());
            slPrice = Math.Round(slPrice * symbol1.TickSize)/symbol1.TickSize;

            Log("SL Price: " + slPrice);
            //var tpPrice = orderPrice - sign * TP * symbol1.TickSize;
            var Amount = Math.Ceiling((CurrMaxRisk * account.Balance) / (100 * slPrice * pricePerTick));
            Log(account.Balance.ToString());

            if(Amount == 0)
            {
                Log("Trading failed. 0 Amount size");
                Stop();
            }
            var StopL = SlTpHolder.CreateSL(slPrice, PriceMeasurement.Offset);
            var TakeP = SlTpHolder.CreateTP(slPrice < 20 ? Math.Ceiling(y * slPrice) : Math.Ceiling(2 * slPrice), PriceMeasurement.Offset);

            Log("Amount: " + Amount);/*
            if((side == Side.Sell && orderPrice < symbol1.Last) || (side == Side.Buy && orderPrice > symbol1.Last))
            {*/
                _operationResult = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Account = this.account,
                    Symbol = this.symbol1,
                    Side = side,
                    Price = orderPrice,
                    Quantity = Amount,
                    TimeInForce = TimeInForce.GTT,
                    ExpirationTime = DateTime.Now.AddMinutes(MaxBars * _historicalSecData.Period.Duration.TotalMinutes),
                    StopLoss = StopL,
                    TakeProfit = TakeP,
                    OrderTypeId = OrderType.Limit
                });
            /*} else
            {
                _operationResult = Core.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Account = account,
                    Symbol = symbol1,
                    Side = side,
                    Quantity = Amount,
                    StopLoss = StopL,
                    TakeProfit = TakeP,
                    OrderTypeId = OrderType.Market
                });
                if (_operationResult.Status == TradingOperationResultStatus.Success)
                    orderPrice = Core.Positions[0].OpenPrice;
                else
                    Log("Error while opening market position. " + _operationResult.Message, StrategyLoggingLevel.Error);
            }*/
            

            trailPrice = Math.Round(orderPrice + sign * slPrice, MidpointRounding.ToEven);
            BEPtsAbove = orderPrice + PtsAbove;
            //PtsAbove = slPrice + 10 * symbol1.TickSize;
            numBars = 0;
            var formattedSide = string.Empty;
            if (side == Side.Buy)
            {
                formattedSide = "Long";
            }
            else
            {
                formattedSide = "Short";
            }
            if (_operationResult.Status == TradingOperationResultStatus.Success)
                Log($"{_operationResult.Message}. {formattedSide} order was placed @ {orderPrice}.", StrategyLoggingLevel.Trading);
            if(_operationResult.Status == TradingOperationResultStatus.Failure)
            {
                Log($"{_operationResult.Message}. {formattedSide} order failed to be placed @ {orderPrice}.", StrategyLoggingLevel.Error);
            }
        }
        private void OnQuote(Symbol instrument, Quote quote)
        {
            if(ChoiceofStop == 3)
            {
                return;
            }
            if (Core.Positions.Length == 0)
            {
                return;
            }
            if (ChoiceofStop == 2 && symbol1.Last >= BEPtsAbove && aboveBE == false)
            {
                trailPrice = Core.Positions[0].OpenPrice + 3;
                aboveBE = true;
            }
            if(Core.Positions.Length > 1)
            {
                Log("Current Positions greater than 1! Count: " + Core.Positions.Length, StrategyLoggingLevel.Error);
                Stop();
            }
            foreach (Position pos in Core.Positions)
            {
                var sign = (pos.Side == Side.Buy) ? -1 : 1;
                if ((((ChoiceofStop == 0 && symbol1.Last >= CurrOrderPrice + PtsAbove) || ChoiceofStop == 1) && ((pos.Side == Side.Buy && symbol1.Last <= trailPrice) || (pos.Side == Side.Sell && symbol1.Last >= trailPrice))) || (ChoiceofStop == 2 && aboveBE == true && symbol1.Last <= trailPrice))
                {
                    TradingOperationResult result = Core.Instance.ClosePosition(new ClosePositionRequestParameters()
                    {
                        Position = pos,
                        CloseQuantity = pos.Quantity
                    });


                    if (result.Status == TradingOperationResultStatus.Success)
                        Log($"{result.Status}. Position was closed. See Line 351. Positions Left: " + Core.Positions.Length, StrategyLoggingLevel.Trading);
                    else
                        Log($"{result.Message}. Position could not be closed. See Line 353", StrategyLoggingLevel.Error);

                    //TimeAfterPos = symbol1.LastDateTime.AddMinutes(minutesbeforenew);

                }
                var trail = Math.Round((symbol1.Last + sign * (indicatorATR.GetValue())/z)* symbol1.TickSize, MidpointRounding.ToEven)/symbol1.TickSize;

                if ((pos.Side == Side.Buy && trail > trailPrice) || (pos.Side == Side.Sell && trail < trailPrice))
                {
                    trailPrice = trail;
                    Log("Trail price: " + trailPrice);
                }
                
            }
            foreach (Order o in Core.Orders)
            {
                o.Cancel();
            }
            

        }
        protected override void OnStop()
        {
            if (_historicalSecData == null)
                return;

            _historicalSecData.RemoveIndicator(indicatorDMI);
            _historicalSecData.RemoveIndicator(indicatorATR);

            _historicalSecData.NewHistoryItem -= this.historicalData_NewHistoryItem;
            foreach (Position pos in Core.Positions)
            {
                pos.Close(pos.Quantity);
            }
            foreach (Order order in Core.Orders)
            {
                order.Cancel();
            }
            Log("All Orders closed");
        }
        protected override void OnRemove()
        {
            if (_historicalSecData != null)
                _historicalSecData.Dispose();
        }
        protected override List<StrategyMetric> OnGetMetrics()
        {
            List<StrategyMetric> result = base.OnGetMetrics();


            // An example of adding custom strategy metrics:
            result.Add("Random choice of seconds", rndNum.ToString());

            return result;
        }
        public void ChangeLStreak(Trade pos)
        {
            if (pos.GrossPnl.Value <= 0)
            {
                lossStreak++;
                CurrMaxRisk -= RiskDir * 0.5;
                Log("Max risk changed: " + CurrMaxRisk);
            }
            else
            {
                lossStreak = 0;
                CurrMaxRisk = maxRisk;
            }
        }


    }
    
}