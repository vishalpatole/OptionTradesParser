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
     * @class Bar
     * @brief The historical data bar's description.
     * @sa EClient, EWrapper
     */
    public class Bar
    {
        public Bar(string time, double open, double high, double low, double close, decimal volume, int count, decimal wap)
        {
            Time = time;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            WAP = wap;
            Count = count;
        }

        /**
         * @brief The bar's date and time (either as a yyyymmss hh:mm:ss formatted string or as system time according to the request). Time zone is the TWS time zone chosen on login. 
         */
        public string Time { get; private set; }

        /**
         * @brief The bar's open price
         */
        public double Open { get; private set; }

        /**
         * @brief The bar's high price
         */
        public double High { get; private set; }

        /**
         * @brief The bar's low price
         */
        public double Low { get; private set; }

        /**
         * @brief The bar's close price
         */
        public double Close { get; private set; }

        /**
         * @brief The bar's traded volume if available (only available for TRADES) 
         */
        public decimal Volume { get; private set; }

        /**
         * @brief The bar's Weighted Average Price (only available for TRADES) 
         */
        public decimal WAP { get; private set; }

        /**
         * @brief The number of trades during the bar's timespan (only available for TRADES) 
         */
        public int Count { get; private set; }
    }
}
