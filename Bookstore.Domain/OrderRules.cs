namespace Bookstore.Domain;

public static class OrderRules
{
    public const int MaximumAddressLength = 640;

    public static string ValidatePlacement(string? address, IReadOnlyDictionary<int, int> quantities)
    {
        var trimmedAddress = address?.Trim();
        if (string.IsNullOrEmpty(trimmedAddress) || trimmedAddress.Length > MaximumAddressLength ||
            quantities.Count == 0 || quantities.Values.Any(quantity => quantity <= 0))
            throw new OrderRuleException("Vui lòng chọn sách và nhập địa chỉ giao hàng hợp lệ.");

        return trimmedAddress;
    }

    public static OrderLine Select(Book book, int quantity)
    {
        if (quantity <= 0 || quantity > book.Stock)
            throw new OrderRuleException($"Sách {book.Title} không đủ tồn kho.");

        return new(book.Id, book.Title, quantity, book.Price);
    }

    public static long Total(IEnumerable<OrderLine> lines) =>
        lines.Sum(line => (long)line.Price * line.Quantity);

    public static void EnsureCanConfirm(Order order)
    {
        if (order.Status != OrderStatus.Pending)
            throw new OrderRuleException("Chỉ có thể xác nhận đơn đang chờ.");
    }

    public static bool ShouldRestoreStockOnCancellation(Order order) => order.Status switch
    {
        OrderStatus.Pending => false,
        OrderStatus.Confirmed => true,
        _ => throw new OrderRuleException("Chỉ có thể hủy đơn đang chờ hoặc đã xác nhận.")
    };
}
