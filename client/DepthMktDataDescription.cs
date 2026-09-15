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
     * @class DepthMktDataDescription
     * @brief A class for storing depth market data description
     */
    public class DepthMktDataDescription
    {
        /**
         * @brief The exchange name
         */
        public string Exchange { get; set; }

        /**
         * @brief The security type
         */
        public string SecType { get; set; }

        /**
         * @brief The listing exchange name
         */
        public string ListingExch { get; set; }

        /**
         * @brief The service data type
         */
        public string ServiceDataType { get; set; }

        /**
         * @brief The aggregated group
         */
        public int AggGroup { get; set; }

        public DepthMktDataDescription() { }

        public DepthMktDataDescription(string exchange, string secType, string listingExch, string serviceDataType, int aggGroup)
        {
            Exchange = exchange;
            SecType = secType;
            ListingExch = listingExch;
            ServiceDataType = serviceDataType;
            AggGroup = aggGroup;
        }
    }
}
