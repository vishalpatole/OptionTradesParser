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

using System.Runtime.InteropServices;

namespace IBApi
{
    /**
     * @class HistoricalSession
     * @brief The historical session. Used when requesting historical schedule with whatToShow = SCHEDULE
     * @sa EClient, EWrapper
     */
    [ComVisible(true)]
    public class HistoricalSession
    {
        public HistoricalSession() { }

        public HistoricalSession(string startDateTime, string endDateTime, string refDate)
        {
            StartDateTime = startDateTime;
            EndDateTime = endDateTime;
            RefDate = refDate;
        }

        public string StartDateTime { get; private set; }
        public string EndDateTime { get; private set; }
        public string RefDate { get; private set; }
    }
}
