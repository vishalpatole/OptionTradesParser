using System;
using System.Collections.Generic;
using System.Linq;

namespace OptionTradesParser
{
    public sealed record PriceIncrementRule(double LowEdge, double Increment);

    public sealed record TakeProfitAllocation(int Level, int Quantity, double TargetPrice);

    public sealed record TakeProfitPlan(IReadOnlyList<TakeProfitAllocation> Targets, int RunnerQuantity);

    public static class OrderSizing
    {
        public static TradeBudgetOption ForBudget(double budget, double limitPrice)
        {
            if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (limitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(limitPrice));

            double contractValue = limitPrice * 100;
            int quantity = Math.Max(1, (int)Math.Round(budget / contractValue, MidpointRounding.AwayFromZero));
            return new TradeBudgetOption(budget, quantity, quantity * contractValue);
        }

        public static double SnapPrice(double price, IReadOnlyList<PriceIncrementRule> rules, bool roundUp)
        {
            if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price));

            double increment = rules
                .Where(rule => rule.Increment > 0 && rule.LowEdge <= price)
                .OrderBy(rule => rule.LowEdge)
                .Select(rule => rule.Increment)
                .LastOrDefault();
            if (increment <= 0) increment = 0.01;

            decimal value = (decimal)price;
            decimal tick = (decimal)increment;
            decimal ticks = value / tick;
            decimal snappedTicks = roundUp ? decimal.Ceiling(ticks) : decimal.Round(ticks, 0, MidpointRounding.AwayFromZero);
            return (double)(snappedTicks * tick);
        }

        public static TakeProfitPlan CreateTakeProfitPlan(int quantity, double averageFillPrice)
            => CreateTakeProfitPlan(quantity, quantity, averageFillPrice, new[] { new PriceIncrementRule(0, 0.01) });

        public static TakeProfitPlan CreateTakeProfitPlan(
            int totalQuantity,
            int filledQuantity,
            double averageFillPrice,
            IReadOnlyList<PriceIncrementRule> priceRules)
        {
            if (totalQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(totalQuantity));
            if (filledQuantity < 0 || filledQuantity > totalQuantity) throw new ArgumentOutOfRangeException(nameof(filledQuantity));
            if (averageFillPrice <= 0) throw new ArgumentOutOfRangeException(nameof(averageFillPrice));

            int[] targetQuantities = new int[3];
            int runnerQuantity = 0;

            if (totalQuantity < 4)
            {
                for (int index = 0; index < filledQuantity; index++) targetQuantities[index] = 1;
            }
            else
            {
                int targetQuota = totalQuantity / 4;
                int runnerQuota = totalQuantity - (targetQuota * 3);
                int allocated = 0;
                while (allocated < filledQuantity)
                {
                    for (int level = 0; level < 3 && allocated < filledQuantity; level++)
                    {
                        if (targetQuantities[level] < targetQuota)
                        {
                            targetQuantities[level]++;
                            allocated++;
                        }
                    }

                    if (runnerQuantity < runnerQuota && allocated < filledQuantity)
                    {
                        runnerQuantity++;
                        allocated++;
                    }
                }
            }

            double[] multipliers = { 1.25, 1.50, 1.75 };
            var targets = new List<TakeProfitAllocation>(3);

            for (int index = 0; index < targetQuantities.Length; index++)
            {
                if (targetQuantities[index] == 0) continue;
                targets.Add(new TakeProfitAllocation(
                    index + 1,
                    targetQuantities[index],
                    SnapPrice(averageFillPrice * multipliers[index], priceRules, roundUp: true)));
            }

            return new TakeProfitPlan(targets, runnerQuantity);
        }
    }
}