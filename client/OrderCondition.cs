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
using System.IO;
using System.Linq;

namespace IBApi
{
    public enum OrderConditionType
    {
        Price = 1,
        Time = 3,
        Margin = 4,
        Execution = 5,
        Volume = 6,
        PercentChange = 7,
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public abstract class OrderCondition
    {
        public OrderConditionType Type { get; private set; }
        public bool IsConjunctionConnection { get; set; }

        public static OrderCondition Create(OrderConditionType type)
        {
            OrderCondition rval = null;

            switch (type)
            {
                case OrderConditionType.Execution:
                    rval = new ExecutionCondition();
                    break;

                case OrderConditionType.Margin:
                    rval = new MarginCondition();
                    break;

                case OrderConditionType.PercentChange:
                    rval = new PercentChangeCondition();
                    break;

                case OrderConditionType.Price:
                    rval = new PriceCondition();
                    break;

                case OrderConditionType.Time:
                    rval = new TimeCondition();
                    break;

                case OrderConditionType.Volume:
                    rval = new VolumeCondition();
                    break;
            }

            if (rval != null)
                rval.Type = type;

            return rval;
        }

        public virtual void Serialize(BinaryWriter outStream) => outStream.AddParameter(IsConjunctionConnection ? "a" : "o");

        public virtual void Deserialize(IDecoder inStream) => IsConjunctionConnection = inStream.ReadString() == "a";

        protected virtual bool TryParse(string cond)
        {
            IsConjunctionConnection = cond == " and";

            return IsConjunctionConnection || cond == " or";
        }

        public static OrderCondition Parse(string cond)
        {
            var conditions = Enum.GetValues(typeof(OrderConditionType)).OfType<OrderConditionType>().Select(t => Create(t)).ToList();

            return conditions.FirstOrDefault(c => c.TryParse(cond));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is OrderCondition other))
                return false;

            return IsConjunctionConnection == other.IsConjunctionConnection && Type == other.Type;
        }

        public override int GetHashCode() => IsConjunctionConnection.GetHashCode() + Type.GetHashCode();
    }

    internal class StringSuffixParser
    {
        public StringSuffixParser(string str) => Rest = str;

        private string SkipSuffix(string prefix) => Rest.Substring(Rest.IndexOf(prefix) + prefix.Length);

        public string GetNextSuffixedValue(string prefix)
        {
            var rval = Rest.Substring(0, Rest.IndexOf(prefix));
            Rest = SkipSuffix(prefix);

            return rval;
        }

        public string Rest { get; private set; }
    }
}
