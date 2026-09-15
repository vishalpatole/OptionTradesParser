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
     * @class TickAttrib
     * @brief Tick attributes that describes additional information for price ticks
     * @sa EWrapper::tickPrice
     */
    public class TickAttrib
    {
        /**
         * @brief Used with tickPrice callback from reqMktData. Specifies whether the price tick is available for automatic execution (1) or not (0).
         */
        public bool CanAutoExecute { get; set; }

        /**
         * @brief Used with tickPrice to indicate if the bid price is lower than the day's lowest value or the ask price is higher than the highest ask 
         */
        public bool PastLimit { get; set; }

        /**
         * @brief Indicates whether the bid/ask price tick is from pre-open session
         */
        public bool PreOpen { get; set; }

        /**
         * @brief Used with tick-by-tick data to indicate if a trade is classified as 'unreportable' (odd lots, combos, derivative trades, etc)
         */
        public bool Unreported { get; set; }

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
        public override string ToString() => (CanAutoExecute ? "canAutoExecute " : "") +
                                             (PastLimit ? "pastLimit " : "") +
                                             (PreOpen ? "preOpen " : "") +
                                             (Unreported ? "unreported " : "") +
                                             (BidPastLow ? "bidPastLow " : "") +
                                             (AskPastHigh ? "askPastHigh " : "");
    }
}
