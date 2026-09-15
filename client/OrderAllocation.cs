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

using System;

namespace IBApi
{
    /**
     * @class OrderAllocation
     * @brief allocation of order
     * @sa OrderState
     */
    public class OrderAllocation
    {
        /**
         * @brief allocation account
         */
        public string Account { get; set; }

        /**
         * @brief position
         */
        public decimal Position { get; set; }

        /**
         * @brief desired position
         */
        public decimal PositionDesired { get; set; }

        /**
         * @brief position after
         */
        public decimal PositionAfter { get; set; }

        /**
         * @brief desired allocation quantity
         */
        public decimal DesiredAllocQty { get; set; }

        /**
         * @brief allowed allocation quantity
         */
        public decimal AllowedAllocQty { get; set; }

        /**
         * @brief is monetary
         */
        public bool IsMonetary { get; set; }

        public OrderAllocation()
        {
            Account = "";
            Position = decimal.MaxValue;
            PositionDesired = decimal.MaxValue;
            PositionAfter = decimal.MaxValue;
            DesiredAllocQty = decimal.MaxValue;
            AllowedAllocQty = decimal.MaxValue;
            IsMonetary = false;
        }

        public override bool Equals(object other)
        {
            if (!(other is OrderAllocation theOther))
            {
                return false;
            }

            if (this == other)
            {
                return true;
            }

            return Account == theOther.Account;
        }

        public override int GetHashCode() => -814345894 + Account.GetHashCode();
    }
}
