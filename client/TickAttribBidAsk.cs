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
     * @class TickAttribBidAsk
     * @brief Tick attributes that describes additional information for bid/ask price ticks
     * @sa EWrapper::tickByTickBidAsk, EWrapper::historicalTicksBidAsk
     */
    public class TickAttribBidAsk
    {
        /**
         * @brief Used with real time tick-by-tick. Indicates if bid is lower than day's lowest low. 
         */
        public bool BidPastLow { get; set; }

        /**
         * @brief Used with real time tick-by-tick. Indicates if ask is higher than day's highest ask. 
         */
        public bool AskPastHigh { get; set; }

        /**
         * @brief Returns string to display. 
         */
        public override string ToString() => (BidPastLow ? "bidPastLow " : "") +
                                             (AskPastHigh ? "askPastHigh " : "");
    }
}
