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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace IBApi
{
    /**
    * @brief Captures incoming messages to the API client and places them into a queue.
    */
    public class EReader
    {
        private readonly EClientSocket eClientSocket;
        private readonly EReaderSignal eReaderSignal;
        private readonly Queue<EMessage> msgQueue = new Queue<EMessage>();
        private readonly EDecoder processMsgsDecoder;
        private const int defaultInBufSize = ushort.MaxValue / 8;

        private bool UseV100Plus => eClientSocket.UseV100Plus;

        private static readonly EWrapper defaultWrapper = new DefaultEWrapper();

        public EReader(EClientSocket clientSocket, EReaderSignal signal)
        {
            eClientSocket = clientSocket;
            eReaderSignal = signal;
            processMsgsDecoder = new EDecoder(eClientSocket.ServerVersion, eClientSocket.Wrapper, eClientSocket);
        }

        public void Start()
        {
            new Thread(() =>
                {
                    try
                    {
                        while (eClientSocket.IsConnected())
                        {
                            if (eClientSocket.IsDataAvailable() && !putMessageToQueue())
                            {
                                eClientSocket.eDisconnect();
                                break;
                            }

                            // Poll here will return true if new data is available or connection is broken.
                            if (eClientSocket.Poll(1000) && !eClientSocket.IsDataAvailable()) // The connection is broken.
                            {
                                // Throw 10054 socket error - An existing connection was forcibly closed by the remote host.
                                throw new System.Net.Sockets.SocketException(10054);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (eClientSocket.IsConnected())
                        {
                            eClientSocket.Wrapper.error(ex);
                            eClientSocket.eDisconnect();
                        }
                    }
                    eReaderSignal.issueSignal();
                })
                { IsBackground = true }.Start();
        }

        private EMessage getMsg()
        {
            lock (msgQueue)
            {
                return msgQueue.Count == 0 ? null : msgQueue.Dequeue();
            }
        }

        public void processMsgs()
        {
            var msg = getMsg();

            while (msg != null && processMsgsDecoder.ParseAndProcessMsg(msg.GetBuf()) > 0)
            {
                msg = getMsg();
            }
        }

        public bool putMessageToQueue()
        {
            try
            {
                var msg = readSingleMessage();

                if (msg == null) return false;

                lock (msgQueue)
                {
                    msgQueue.Enqueue(msg);
                }

                eReaderSignal.issueSignal();

                return true;
            }
            catch (Exception ex)
            {
                if (eClientSocket.IsConnected()) eClientSocket.Wrapper.error(ex);

                return false;
            }
        }

        private readonly List<byte> inBuf = new List<byte>(defaultInBufSize);

        private EMessage readSingleMessage()
        {
            int msgSize;
            if (UseV100Plus)
            {
                msgSize = eClientSocket.ReadInt();

                if (msgSize > Constants.MaxMsgSize) throw new EClientException(EClientErrors.BAD_LENGTH);

                return new EMessage(eClientSocket.ReadByteArray(msgSize));
            }

            if (inBuf.Count == 0)
                AppendInBuf();

            while (true)
            {
                try
                {
                    msgSize = new EDecoder(eClientSocket.ServerVersion, defaultWrapper).ParseAndProcessMsg(inBuf.ToArray());
                    break;
                }
                catch (EndOfStreamException)
                {
                    if (inBuf.Count >= inBuf.Capacity * 3 / 4) inBuf.Capacity *= 2;

                    AppendInBuf();
                }
            }

            var msgBuf = new byte[msgSize];

            inBuf.CopyTo(0, msgBuf, 0, msgSize);
            inBuf.RemoveRange(0, msgSize);

            if (inBuf.Count < defaultInBufSize && inBuf.Capacity > defaultInBufSize) inBuf.Capacity = defaultInBufSize;

            return new EMessage(msgBuf);
        }

        private void AppendInBuf() => inBuf.AddRange(eClientSocket.ReadAtLeastNBytes(inBuf.Capacity - inBuf.Count));
    }
}
