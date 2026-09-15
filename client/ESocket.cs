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

namespace IBApi
{
    internal class ESocket : ETransport, IDisposable
    {
        private readonly BinaryWriter tcpWriter;
        private readonly object tcpWriterLock = new object();

        public ESocket(Stream socketStream) => tcpWriter = new BinaryWriter(socketStream);

        public void Send(EMessage msg)
        {
            lock (tcpWriterLock)
            {
                tcpWriter.Write(msg.GetBuf());
            }
        }

        public void Dispose() => tcpWriter.Dispose();
    }
}
