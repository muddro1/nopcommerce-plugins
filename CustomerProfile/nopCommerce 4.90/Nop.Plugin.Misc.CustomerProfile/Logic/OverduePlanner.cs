namespace Nop.Plugin.Misc.CustomerProfile.Logic;

public sealed record Net30Snapshot(int OrderId, int CustomerId, DateTime DueOn, bool IsPaid, bool IsCancelled, DateTime? OverdueNoticeSentOnUtc);

public static class OverduePlanner
{
    /// <summary>Unpaid Net 30 orders past their due date that haven't had an overdue reminder yet</summary>
    /// <param name="today">Today in the store time zone</param>
    public static IReadOnlyList<int> OrdersToRemind(IEnumerable<Net30Snapshot> orders, DateTime today, bool enabled)
    {
        if (!enabled)
            return Array.Empty<int>();

        return orders
            .Where(o => !o.IsPaid && !o.IsCancelled && o.OverdueNoticeSentOnUtc == null && o.DueOn.Date < today.Date)
            .Select(o => o.OrderId)
            .ToList();
    }
}
