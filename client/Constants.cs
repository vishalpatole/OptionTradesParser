/*
 * C# TWS API Client
 *
 * Copyright (C) 2013-2026  Interactive Brokers LLC
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

namespace IBApi
{
    public static class Constants
    {
        public const int ClientVersion = 66; //API v. 9.71
        public const byte EOL = 0;
        public const string BagSecType = "BAG";
        public const string INFINITY_STR = "Infinity";

        public const int FaGroups = 1;
        public const int FaAliases = 3;
        public const int MinVersion = 100;
        public const int MaxVersion = MinServerVer.UNIFIED_VERSION_COND_ORDER_WITH_OVERNIGHT_PARAM;
        public const int MaxMsgSize = 0x00FFFFFF;

        public const int PROTOBUF_MSG_ID = 200;

        public static int GetServerVersionForMessage(OutgoingMessages outgoingMessage)
        {
            switch (outgoingMessage)
            {
                case OutgoingMessages.RequestExecutions:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF;
                case OutgoingMessages.PlaceOrder:
                case OutgoingMessages.CancelOrder:
                case OutgoingMessages.RequestGlobalCancel:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_PLACE_ORDER;
                case OutgoingMessages.RequestAllOpenOrders:
                case OutgoingMessages.RequestAutoOpenOrders:
                case OutgoingMessages.RequestOpenOrders:
                case OutgoingMessages.ReqCompletedOrders:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_COMPLETED_ORDER;
                case OutgoingMessages.RequestContractData:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_CONTRACT_DATA;
                case OutgoingMessages.RequestMarketData:
                case OutgoingMessages.CancelMarketData:
                case OutgoingMessages.RequestMarketDepth:
                case OutgoingMessages.CancelMarketDepth:
                case OutgoingMessages.RequestMarketDataType:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_MARKET_DATA;
                case OutgoingMessages.RequestAccountData:
                case OutgoingMessages.RequestManagedAccounts:
                case OutgoingMessages.RequestPositions:
                case OutgoingMessages.CancelPositions:
                case OutgoingMessages.RequestAccountSummary:
                case OutgoingMessages.CancelAccountSummary:
                case OutgoingMessages.RequestPositionsMulti:
                case OutgoingMessages.CancelPositionsMulti:
                case OutgoingMessages.RequestAccountUpdatesMulti:
                case OutgoingMessages.CancelAccountUpdatesMulti:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_ACCOUNTS_POSITIONS;
                case OutgoingMessages.RequestHistoricalData:
                case OutgoingMessages.CancelHistoricalData:
                case OutgoingMessages.RequestRealTimeBars:
                case OutgoingMessages.CancelRealTimeBars:
                case OutgoingMessages.RequestHeadTimestamp:
                case OutgoingMessages.CancelHeadTimestamp:
                case OutgoingMessages.RequestHistogramData:
                case OutgoingMessages.CancelHistogramData:
                case OutgoingMessages.ReqHistoricalTicks:
                case OutgoingMessages.ReqTickByTickData:
                case OutgoingMessages.CancelTickByTickData:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_HISTORICAL_DATA;
                case OutgoingMessages.RequestNewsBulletins:
                case OutgoingMessages.CancelNewsBulletin:
                case OutgoingMessages.RequestNewsArticle:
                case OutgoingMessages.RequestNewsProviders:
                case OutgoingMessages.RequestHistoricalNews:
                case OutgoingMessages.ReqWshMetaData:
                case OutgoingMessages.CancelWshMetaData:
                case OutgoingMessages.ReqWshEventData:
                case OutgoingMessages.CancelWshEventData:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_NEWS_DATA;
                case OutgoingMessages.RequestScannerParameters:
                case OutgoingMessages.RequestScannerSubscription:
                case OutgoingMessages.CancelScannerSubscription:
                case OutgoingMessages.ReqPnL:
                case OutgoingMessages.CancelPnL:
                case OutgoingMessages.ReqPnLSingle:
                case OutgoingMessages.CancelPnLSingle:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_SCAN_DATA;
                case OutgoingMessages.RequestFA:
                case OutgoingMessages.ReplaceFA:
                case OutgoingMessages.ExerciseOptions:
                case OutgoingMessages.ReqCalcImpliedVolat:
                case OutgoingMessages.CancelImpliedVolatility:
                case OutgoingMessages.ReqCalcOptionPrice:
                case OutgoingMessages.CancelOptionPrice:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_REST_MESSAGES_1;
                case OutgoingMessages.RequestSecurityDefinitionOptionalParameters:
                case OutgoingMessages.RequestSoftDollarTiers:
                case OutgoingMessages.RequestFamilyCodes:
                case OutgoingMessages.RequestMatchingSymbols:
                case OutgoingMessages.RequestSmartComponents:
                case OutgoingMessages.RequestMarketRule:
                case OutgoingMessages.ReqUserInfo:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_REST_MESSAGES_2;
                case OutgoingMessages.RequestIds:
                case OutgoingMessages.RequestCurrentTime:
                case OutgoingMessages.RequestCurrentTimeInMillis:
                case OutgoingMessages.StartApi:
                case OutgoingMessages.ChangeServerLog:
                case OutgoingMessages.VerifyRequest:
                case OutgoingMessages.VerifyMessage:
                case OutgoingMessages.QueryDisplayGroups:
                case OutgoingMessages.SubscribeToGroupEvents:
                case OutgoingMessages.UpdateDisplayGroup:
                case OutgoingMessages.UnsubscribeFromGroupEvents:
                case OutgoingMessages.RequestMktDepthExchanges:
                    return MinServerVer.MIN_SERVER_VER_PROTOBUF_REST_MESSAGES_3;
                default:
                    return int.MaxValue; // unknown id, not supported
            }
        }
    }
}
