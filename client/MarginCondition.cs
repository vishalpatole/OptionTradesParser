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
    * @class MarginCondition
    * @brief This class represents a condition requiring the margin cushion reaching a given percent to be fulfilled.
    * Orders can be activated or canceled if a set of given conditions is met. A MarginCondition is met whenever the margin penetrates the given percent.
    */
    public class MarginCondition : OperatorCondition
    {
        private const string header = "the margin cushion percent";

        protected override string Value
        {
            get => Percent.ToString(System.Globalization.NumberFormatInfo.InvariantInfo);
            set => Percent = int.Parse(value, System.Globalization.NumberFormatInfo.InvariantInfo);
        }

        public override string ToString() => header + base.ToString();

        /**
        * @brief Margin percent to trigger condition.
        */
        public int Percent { get; set; }

        protected override bool TryParse(string cond)
        {
            if (!cond.StartsWith(header)) return false;

            cond = cond.Replace(header, "");

            return base.TryParse(cond);
        }
    }
}
