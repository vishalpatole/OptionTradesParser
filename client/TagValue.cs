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
    * @class TagValue
    * @brief Convenience class to define key-value pairs
    */
    public class TagValue
    {
        public string Tag { get; set; }


        public string Value { get; set; }

        public TagValue() { }

        public TagValue(string p_tag, string p_value)
        {
            Tag = p_tag;
            Value = p_value;
        }

        public override bool Equals(object other)
        {
            if (this == other) return true;
            if (!(other is TagValue l_theOther)) return false;
            if (Util.StringCompare(Tag, l_theOther.Tag) != 0) return false;
            if (Util.StringCompare(Value, l_theOther.Value) != 0) return false;
            return true;
        }

        public override int GetHashCode()
        {
            var hashCode = 221537429;
            hashCode *= -1521134295 + EqualityComparer<string>.Default.GetHashCode(Tag);
            hashCode *= -1521134295 + EqualityComparer<string>.Default.GetHashCode(Value);
            return hashCode;
        }
    }
}
