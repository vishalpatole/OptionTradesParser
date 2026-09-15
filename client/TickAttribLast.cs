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
     * @class TickAttribLast
     * @brief Tick attributes that describes additional information for last price ticks
     * @sa EWrapper::tickByTickAllLast, EWrapper::historicalTicksLast
     */
    public class TickAttribLast
    {
        /**
         * @brief Not currently used with trade data; only applies to bid/ask data. 
         */
        public bool PastLimit { get; set; }

        /**
         * @brief Used with tick-by-tick last data or historical ticks last to indicate if a trade is classified as 'unreportable' (odd lots, combos, derivative trades, etc)
        */
        public bool Unreported { get; set; }

        /**
         * @brief Returns string to display. 
         */
        public override string ToString() => (PastLimit ? "pastLimit " : "") +
                                             (Unreported ? "unreported " : "");
    }
}
