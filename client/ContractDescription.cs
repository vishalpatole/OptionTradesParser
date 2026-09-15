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
     * @class ContractDescription
     * @brief contract data and list of derivative security types
     * @sa Contract, EClient::reqMatchingSymbols, EWrapper::symbolSamples
     */
    public class ContractDescription
    {
        /**
         * @brief A contract data
         */
        public Contract Contract { get; set; }

        /**
         * @brief A list of derivative security types
         */
        public string[] DerivativeSecTypes { get; set; }

        public ContractDescription() => Contract = new Contract();

        public ContractDescription(Contract contract, string[] derivativeSecTypes)
        {
            Contract = contract;
            DerivativeSecTypes = derivativeSecTypes;
        }

        public override string ToString() => $"{Contract} derivativeSecTypes [{string.Join(", ", DerivativeSecTypes)}]";
    }
}
