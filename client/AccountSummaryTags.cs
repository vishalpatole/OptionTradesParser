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
    /**
     * @class AccountSummaryTags
     * @brief class containing all existing values being reported by EClientSocket::reqAccountSummary
     */
    public class AccountSummaryTags
    {
        public const string AccountType = "AccountType";
        public const string NetLiquidation = "NetLiquidation";
        public const string TotalCashValue = "TotalCashValue";
        public const string SettledCash = "SettledCash";
        public const string AccruedCash = "AccruedCash";
        public const string BuyingPower = "BuyingPower";
        public const string EquityWithLoanValue = "EquityWithLoanValue";
        public const string PreviousDayEquityWithLoanValue = "PreviousDayEquityWithLoanValue";
        public const string GrossPositionValue = "GrossPositionValue";
        public const string ReqTEquity = "ReqTEquity";
        public const string ReqTMargin = "ReqTMargin";
        public const string SMA = "SMA";
        public const string InitMarginReq = "InitMarginReq";
        public const string MaintMarginReq = "MaintMarginReq";
        public const string AvailableFunds = "AvailableFunds";
        public const string ExcessLiquidity = "ExcessLiquidity";
        public const string Cushion = "Cushion";
        public const string FullInitMarginReq = "FullInitMarginReq";
        public const string FullMaintMarginReq = "FullMaintMarginReq";
        public const string FullAvailableFunds = "FullAvailableFunds";
        public const string FullExcessLiquidity = "FullExcessLiquidity";
        public const string LookAheadNextChange = "LookAheadNextChange";
        public const string LookAheadInitMarginReq = "LookAheadInitMarginReq";
        public const string LookAheadMaintMarginReq = "LookAheadMaintMarginReq";
        public const string LookAheadAvailableFunds = "LookAheadAvailableFunds";
        public const string LookAheadExcessLiquidity = "LookAheadExcessLiquidity";
        public const string HighestSeverity = "HighestSeverity";
        public const string DayTradesRemaining = "DayTradesRemaining";
        public const string Leverage = "Leverage";

        public static string GetAllTags() => $"{AccountType},{NetLiquidation},{TotalCashValue},{SettledCash},{AccruedCash},{BuyingPower},{EquityWithLoanValue},{PreviousDayEquityWithLoanValue},{GrossPositionValue},{ReqTEquity},{ReqTMargin},{SMA},{InitMarginReq},{MaintMarginReq},{AvailableFunds},{ExcessLiquidity},{Cushion},{FullInitMarginReq},{FullMaintMarginReq},{FullAvailableFunds},{FullExcessLiquidity},{LookAheadNextChange},{LookAheadInitMarginReq},{LookAheadMaintMarginReq},{LookAheadAvailableFunds},{LookAheadExcessLiquidity},{HighestSeverity},{DayTradesRemaining},{Leverage}";
    }
}
