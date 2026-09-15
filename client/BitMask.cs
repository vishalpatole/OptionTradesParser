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

namespace IBApi
{
    internal class BitMask
    {
        private int m_mask;

        public BitMask(int p) => m_mask = p;

        public int GetMask() => m_mask;

        public void Clear() => m_mask = 0;

        public bool this[int index]
        {
            get
            {
                if (index >= 32)
                {
                    throw new IndexOutOfRangeException();
                }

                return (m_mask & (1 << index)) != 0;
            }
            set
            {
                if (index >= 32)
                {
                    throw new IndexOutOfRangeException();
                }

                if (value)
                {
                    m_mask |= 1 << index;
                }
                else
                {
                    m_mask &= ~(1 << index);
                }
            }
        }
    }
}
