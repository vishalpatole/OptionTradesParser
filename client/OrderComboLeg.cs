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
     * @class OrderComboLeg
     * @brief Allows to specify a price on an order's leg
     * @sa Order, ComboLeg
     */
    public class OrderComboLeg
    {
        /**
         * @brief The order's leg's price
         */
        public double Price { get; set; }

        public OrderComboLeg() => Price = double.MaxValue;

        public OrderComboLeg(double p_price) => Price = p_price;

        public override bool Equals(object other)
        {
            if (!(other is OrderComboLeg theOther))
            {
                return false;
            }

            if (this == other)
            {
                return true;
            }

            return Price == theOther.Price;
        }

        public override int GetHashCode() => -814345894 + Price.GetHashCode();
    }
}
