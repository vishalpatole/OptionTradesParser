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
    * @brief Notifies the thread reading information from the TWS whenever there are messages ready to be consumed. Not currently used in Python API.
    */
    public interface EReaderSignal
    {
        /**
         * @brief Issues a signal to the consuming thread when there are things to be consumed.
         */
        void issueSignal();

        /**
         * @brief Makes the consuming thread waiting until a signal is issued.
         */
        void waitForSignal();
    }
}
