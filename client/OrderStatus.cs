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
    /**
     * @class OrderStatus
     * @brief All possible order status values returned by TWS/Gateway via the
     *        orderStatus and openOrder callbacks.
     * @sa OrderStatusUtils
     */
    public enum OrderStatus
    {
        /** @brief Order has been received by the API but not yet transmitted to TWS. */
        ApiPending,

        /** @brief Order was cancelled via the API before being acknowledged by TWS. */
        ApiCancelled,

        /** @brief Simulated order has been accepted; waiting for election criteria to be met. */
        PreSubmitted,

        /** @brief Cancellation has been requested but not yet confirmed by the exchange. */
        PendingCancel,

        /** @brief Order has been confirmed cancelled by the exchange. */
        Cancelled,

        /** @brief Order has been accepted and is working. */
        Submitted,

        /** @brief Order has been completely filled. */
        Filled,

        /** @brief Order was received but is no longer active (e.g. rejected or cancelled by destination). */
        Inactive,

        /** @brief Order has been received but not yet transmitted to TWS. */
        PendingSubmit,

        /** @brief Status string was not recognised by OrderStatusUtils.Get(). */
        Unknown,
    }

    /**
     * @class OrderStatusUtils
     * @brief Helper and extension methods for the OrderStatus enum.
     * @sa OrderStatus
     */
    public static class OrderStatusUtils
    {
        /**
         * @brief Converts a raw TWS/Gateway order status string to the corresponding
         *        OrderStatus value.  Comparison is case-insensitive.
         *        Returns OrderStatus.Unknown if the string is not recognised.
         * @param apiString The status string from EWrapper.orderStatus or EWrapper.openOrder.
         * @return The matching OrderStatus, or OrderStatus.Unknown.
         */
        public static OrderStatus Get(string apiString)
        {
            if (Enum.TryParse(apiString, ignoreCase: true, out OrderStatus result))
                return result;
            return OrderStatus.Unknown;
        }

        /**
         * @brief Returns true if this status indicates the order is still working
         *        (i.e. further fills or a cancellation are still possible).
         * @param status The OrderStatus to test.
         * @return True if the order is active.
         */
        public static bool IsActive(this OrderStatus status)
        {
            return status == OrderStatus.PreSubmitted
                || status == OrderStatus.PendingCancel
                || status == OrderStatus.Submitted
                || status == OrderStatus.PendingSubmit;
        }

        /**
         * @brief Returns true if this status is terminal — no further fills are expected
         *        and the order will not transition to another state.
         * @param status The OrderStatus to test.
         * @return True if the order is in a terminal state.
         */
        public static bool IsTerminal(this OrderStatus status)
        {
            return status == OrderStatus.Filled
                || status == OrderStatus.Cancelled
                || status == OrderStatus.Inactive
                || status == OrderStatus.ApiCancelled;
        }
    }
}
