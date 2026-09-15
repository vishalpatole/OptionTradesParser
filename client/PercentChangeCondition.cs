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

using System.Globalization;

namespace IBApi
{
    /**
     * @brief Used with conditional orders to place or submit an order based on a percentage change of an instrument to the last close price.
     */
    public class PercentChangeCondition : ContractCondition
    {
        protected override string Value
        {
            get => ChangePercent.ToString(NumberFormatInfo.InvariantInfo);
            set => ChangePercent = double.Parse(value, NumberFormatInfo.InvariantInfo);
        }

        public double ChangePercent { get; set; }
    }
}
