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
    public abstract class OperatorCondition : OrderCondition
    {
        protected abstract string Value { get; set; }
        public bool IsMore { get; set; }

        private const string header = " is ";

        public override string ToString() => header + (IsMore ? ">= " : "<= ") + Value;

        public override bool Equals(object obj)
        {
            if (!(obj is OperatorCondition other))
                return false;

            return base.Equals(obj)
                && Value.Equals(other.Value, System.StringComparison.Ordinal)
                && IsMore == other.IsMore;
        }

        public override int GetHashCode() => base.GetHashCode() + Value.GetHashCode() + IsMore.GetHashCode();

        protected override bool TryParse(string cond)
        {
            if (!cond.StartsWith(header))
                return false;

            try
            {
                cond = cond.Replace(header, "");

                if (!cond.StartsWith(">=") && !cond.StartsWith("<="))
                    return false;

                IsMore = cond.StartsWith(">=");

                if (base.TryParse(cond.Substring(cond.LastIndexOf(" "))))
                    cond = cond.Substring(0, cond.LastIndexOf(" "));

                Value = cond.Substring(3);
            }
            catch
            {
                return false;
            }

            return true;
        }

        public override void Deserialize(IDecoder inStream)
        {
            base.Deserialize(inStream);

            IsMore = inStream.ReadBoolFromInt();
            Value = inStream.ReadString();
        }

        public override void Serialize(System.IO.BinaryWriter outStream)
        {
            base.Serialize(outStream);
            outStream.AddParameter(IsMore);
            outStream.AddParameter(Value);
        }
    }
}
