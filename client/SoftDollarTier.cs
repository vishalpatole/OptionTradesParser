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
     * @class SoftDollarTier
     * @brief A container for storing Soft Dollar Tier information
     */
    public class SoftDollarTier
    {
        /**
         * @brief The name of the Soft Dollar Tier
         */
        public string Name { get; set; }

        /**
         * @brief The value of the Soft Dollar Tier
         */
        public string Value { get; set; }

        /**
         * @brief The display name of the Soft Dollar Tier
         */
        public string DisplayName { get; set; }

        public SoftDollarTier(string name, string value, string displayName)
        {
            Name = name;
            Value = value;
            DisplayName = displayName;
        }

        public SoftDollarTier() : this(null, null, null) { }

        public override bool Equals(object obj)
        {
            var b = obj as SoftDollarTier;

            if (Equals(b, null)) return false;

            return string.Equals(Name, b.Name, System.StringComparison.OrdinalIgnoreCase) && string.Equals(Value, b.Value, System.StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode() => (Name ?? "").GetHashCode() + (Value ?? "").GetHashCode();

        public static bool operator ==(SoftDollarTier left, SoftDollarTier right) => left.Equals(right);

        public static bool operator !=(SoftDollarTier left, SoftDollarTier right) => !left.Equals(right);

        public override string ToString() => DisplayName;
    }
}
