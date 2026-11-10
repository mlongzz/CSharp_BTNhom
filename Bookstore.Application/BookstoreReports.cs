using Bookstore.Domain;

namespace Bookstore.Application;

public interface IBookstoreReports
{
    BookstoreReport Read(DateOnly from, DateOnly through, DateOnly today);
}

public sealed record BookstoreReport(long RevenueToday, int NewOrdersToday, int PendingOrders,
    IReadOnlyList<DailyRevenue> Revenue, IReadOnlyList<OrderCount> Orders,
    IReadOnlyList<TopBook> TopBooks, IReadOnlyList<InventoryBook> Inventory,
    long ReceivedQuantity, long ReceivingCost);

public sealed record DailyRevenue(string Day, int CompletedOrders, long Revenue);
public sealed record OrderCount(string Status, int Count);
public sealed record TopBook(string Title, long Quantity);
