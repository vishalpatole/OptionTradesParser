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

using System.Collections.Generic;

namespace IBApi
{
    /**
     * @class OrderCancel
     */
    public class OrderCancel
    {
        public static string EMPTY_STR = "";

        /**
         * @brief Used by brokers and advisors when manually entering, modifying or cancelling orders at the direction of a client.
         * <i>Only used when allocating orders to specific groups or accounts. Excluding "All" group.</i>
         */
        public string ManualOrderCancelTime { get; set; }

        /**
         * @brief This is a regulartory attribute that applies to all US Commodity (Futures) Exchanges, provided to allow client to comply with CFTC Tag 50 Rules
         */
        public string ExtOperator { get; set; }

        /**
         * @brief Manual Order Indicator
         */
        public int ManualOrderIndicator { get; set; }

        public OrderCancel()
        {
            ManualOrderCancelTime = EMPTY_STR;
            ExtOperator = EMPTY_STR;
            ManualOrderIndicator = int.MaxValue;
        }

        public override bool Equals(object p_other)
        {
            if (this == p_other)
                return true;

            if (!(p_other is OrderCancel l_theOther))
                return false;

            if (ManualOrderIndicator != l_theOther.ManualOrderIndicator)
            {
                return false;
            }
            if (
                Util.StringCompare(ManualOrderCancelTime, l_theOther.ManualOrderCancelTime) != 0 ||
                Util.StringCompare(ExtOperator, l_theOther.ExtOperator) != 0)
            {
                return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            var hashCode = 1040337091;
            hashCode *= -1521134295 + EqualityComparer<string>.Default.GetHashCode(ManualOrderCancelTime);
            hashCode *= -1521134295 + EqualityComparer<string>.Default.GetHashCode(ExtOperator);
            hashCode *= -1521134295 + ManualOrderIndicator.GetHashCode();

            return hashCode;
        }
    }
}
