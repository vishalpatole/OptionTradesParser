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
    public class WshEventData
    {
        public int ConId { get; set; }

        public string Filter { get; set; }

        public bool FillWatchlist { get; set; }

        public bool FillPortfolio { get; set; }

        public bool FillCompetitors { get; set; }

        public string StartDate { get; set; }

        public string EndDate { get; set; }

        public int TotalLimit { get; set; }


        public WshEventData()
        {
            ConId = int.MaxValue;
            Filter = Order.EMPTY_STR;
            FillWatchlist = false;
            FillPortfolio = false;
            FillCompetitors = false;
            StartDate = Order.EMPTY_STR;
            EndDate = Order.EMPTY_STR;
            TotalLimit = int.MaxValue;
        }

        public WshEventData(int conId, bool fillWatchlist, bool fillPortfolio, bool fillCompetitors, string startDate, string endDate, int totalLimit)
        {
            ConId = conId;
            Filter = Order.EMPTY_STR;
            FillWatchlist = fillWatchlist;
            FillPortfolio = fillPortfolio;
            FillCompetitors = fillCompetitors;
            StartDate = startDate;
            EndDate = endDate;
            TotalLimit = totalLimit;
        }

        public WshEventData(string filter, bool fillWatchlist, bool fillPortfolio, bool fillCompetitors, string startDate, string endDate, int totalLimit)
        {
            ConId = int.MaxValue;
            Filter = filter;
            FillWatchlist = fillWatchlist;
            FillPortfolio = fillPortfolio;
            FillCompetitors = fillCompetitors;
            StartDate = startDate;
            EndDate = endDate;
            TotalLimit = totalLimit;
        }
    }
}
