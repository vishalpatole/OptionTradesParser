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

using System.Runtime.InteropServices;

namespace IBApi
{
    /**
     * @class HistoricalTickLast
     * @brief The historical last tick's description. Used when requesting historical tick data with whatToShow = TRADES
     * @sa EClient, EWrapper
     */
    [ComVisible(true)]
    public class HistoricalTickLast
    {
        public HistoricalTickLast() { }

        public HistoricalTickLast(long time, TickAttribLast tickAttribLast, double price, decimal size, string exchange, string specialConditions)
        {
            Time = time;
            TickAttribLast = tickAttribLast;
            Price = price;
            Size = size;
            Exchange = exchange;
            SpecialConditions = specialConditions;
        }

        /**
         * @brief The UNIX timestamp of the historical tick 
         */
        public long Time
        {
            [return: MarshalAs(UnmanagedType.I8)]
            get;
            [param: MarshalAs(UnmanagedType.I8)]
            private set;
        }

        /**
         * @brief Tick attribs of historical last tick
         */
        public TickAttribLast TickAttribLast { get; private set; }

        /**
         * @brief The last price of the historical tick 
         */
        public double Price { get; private set; }

        /**
         * @brief The last size of the historical tick 
         */
        public decimal Size
        {
            [return: MarshalAs(UnmanagedType.I8)]
            get;
            [param: MarshalAs(UnmanagedType.I8)]
            private set;
        }

        /**
         * @brief The source exchange of the historical tick 
         */
        public string Exchange { get; private set; }

        /**
         * @brief The conditions of the historical tick. Refer to Trade Conditions page for more details: https://www.interactivebrokers.com/en/index.php?f=7235
         */
        public string SpecialConditions { get; private set; }
    }
}
