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
     * @class ScannerSubscription
     * @brief Defines a market scanner request
     */
    public class ScannerSubscription
    {
        /**
         * @brief The number of rows to be returned for the query
         */
        public int NumberOfRows { get; set; } = -1;

        /**
         * @brief The instrument's type for the scan. I.e. STK, FUT.HK, etc.
         */
        public string Instrument { get; set; }

        /**
         * @brief The request's location (STK.US, STK.US.MAJOR, etc). 
         */
        public string LocationCode { get; set; }

        /**
         * @brief Same as TWS Market Scanner's "parameters" field, for example: TOP_PERC_GAIN
         */
        public string ScanCode { get; set; }

        /**
         * @brief Filters out Contracts which price is below this value
         */
        public double AbovePrice { get; set; } = double.MaxValue;

        /**
         * @brief Filters out contracts which price is above this value.
         */
        public double BelowPrice { get; set; } = double.MaxValue;

        /**
         * @brief Filters out Contracts which volume is above this value.
         */
        public int AboveVolume { get; set; } = int.MaxValue;

        /**
         * @brief Filters out Contracts which option volume is above this value.
         */
        public int AverageOptionVolumeAbove { get; set; } = int.MaxValue;

        /**
         * @brief Filters out Contracts which market cap is above this value.
         */
        public double MarketCapAbove { get; set; } = double.MaxValue;

        /**
         * @brief Filters out Contracts which market cap is below this value.
         */
        public double MarketCapBelow { get; set; } = double.MaxValue;

        /**
         * @brief Filters out Contracts which Moody's rating is below this value.
         */
        public string MoodyRatingAbove { get; set; }

        /**
         * @brief Filters out Contracts which Moody's rating is above this value.
         */
        public string MoodyRatingBelow { get; set; }

        /**
         * @brief Filters out Contracts with a S&P rating below this value.
         */
        public string SpRatingAbove { get; set; }

        /**
         * @brief Filters out Contracts with a S&P rating above this value.
         */
        public string SpRatingBelow { get; set; }

        /**
         * @brief Filter out Contracts with a maturity date earlier than this value.
         */
        public string MaturityDateAbove { get; set; }

        /**
         * @brief Filter out Contracts with a maturity date older than this value.
         */
        public string MaturityDateBelow { get; set; }

        /**
         * @brief Filter out Contracts with a coupon rate lower than this value.
         */
        public double CouponRateAbove { get; set; } = double.MaxValue;

        /**
         * @brief Filter out Contracts with a coupon rate higher than this value.
         */
        public double CouponRateBelow { get; set; } = double.MaxValue;

        /**
         * @brief Filters out Convertible bonds
         */
        public bool ExcludeConvertible { get; set; }

        /**
         * @brief For example, a pairing "Annual, true" used on the "top Option Implied Vol % Gainers" scan would return annualized volatilities.
         */
        public string ScannerSettingPairs { get; set; }

        /**
         * @brief -
         *      CORP = Corporation
         *      ADR = American Depositary Receipt
         *      ETF = Exchange Traded Fund
         *      REIT = Real Estate Investment Trust
         *      CEF = Closed End Fund
         */
        public string StockTypeFilter { get; set; }
    }
}
