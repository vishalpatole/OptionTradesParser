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
     * @class NewsProvider
     * @brief Class describing news provider
     * @sa EClient::reqNewsProviders, EWrapper::newsProviders
     */
    public class NewsProvider
    {
        /**
         * @brief The API news provider code
         */
        public string ProviderCode { get; set; }

        /**
         * @brief The API news provider name
         */
        public string ProviderName { get; set; }

        public NewsProvider() { }

        public NewsProvider(string providerCode, string providerName)
        {
            ProviderCode = providerCode;
            ProviderName = providerName;
        }
    }
}
