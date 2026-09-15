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

using System.IO;

namespace IBApi
{
    /**
     * @class ExecutionCondition
     * @brief This class represents a condition requiring a specific execution event to be fulfilled.
     * Orders can be activated or canceled if a set of given conditions is met. An ExecutionCondition is met whenever a trade occurs on a certain product at the given exchange.
     */
    public class ExecutionCondition : OrderCondition
    {
        /**
        * @brief Exchange where the symbol needs to be traded.
        */
        public string Exchange { get; set; }

        /**
        * @brief Kind of instrument being monitored.
        */
        public string SecType { get; set; }

        /**
        * @brief Instrument's symbol
        */
        public string Symbol { get; set; }

        private const string header = "trade occurs for ";
        private const string symbolSuffix = " symbol on ";
        private const string exchangeSuffix = " exchange for ";
        private const string secTypeSuffix = " security type";

        public override string ToString() => header + Symbol + symbolSuffix + Exchange + exchangeSuffix + SecType + secTypeSuffix;

        protected override bool TryParse(string cond)
        {
            if (!cond.StartsWith(header))
                return false;

            try
            {
                var parser = new StringSuffixParser(cond.Replace(header, ""));

                Symbol = parser.GetNextSuffixedValue(symbolSuffix);
                Exchange = parser.GetNextSuffixedValue(exchangeSuffix);
                SecType = parser.GetNextSuffixedValue(secTypeSuffix);

                if (!string.IsNullOrWhiteSpace(parser.Rest)) return base.TryParse(parser.Rest);
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

            SecType = inStream.ReadString();
            Exchange = inStream.ReadString();
            Symbol = inStream.ReadString();
        }

        public override void Serialize(BinaryWriter outStream)
        {
            base.Serialize(outStream);

            outStream.AddParameter(SecType);
            outStream.AddParameter(Exchange);
            outStream.AddParameter(Symbol);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ExecutionCondition other))
                return false;

            return base.Equals(obj)
                && Exchange.Equals(other.Exchange, System.StringComparison.Ordinal)
                && SecType.Equals(other.SecType, System.StringComparison.Ordinal)
                && Symbol.Equals(other.Symbol, System.StringComparison.Ordinal);
        }

        public override int GetHashCode() => base.GetHashCode() + Exchange.GetHashCode() + SecType.GetHashCode() + Symbol.GetHashCode();
    }
}
