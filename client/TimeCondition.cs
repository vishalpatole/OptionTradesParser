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
     * @brief Time condition used in conditional orders to submit or cancel orders at specified time. 
     */
    public class TimeCondition : OperatorCondition
    {
        private const string header = "time";

        protected override string Value
        {
            get => Time;
            set => Time = value;
        }

        public override string ToString() => header + base.ToString();

        /**
         * @brief Time field used in conditional order logic. Valid format: YYYYMMDD HH:MM:SS
         */

        public string Time { get; set; }

        protected override bool TryParse(string cond)
        {
            if (!cond.StartsWith(header))
                return false;

            cond = cond.Replace(header, "");
            return base.TryParse(cond);
        }
    }
}
