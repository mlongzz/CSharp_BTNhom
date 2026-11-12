using Bookstore.Application;

namespace Bookstore.Models;

public enum ReportSection { Overview, Revenue, Orders, TopBooks, Inventory }

public sealed record ReportsViewModel(DateOnly From, DateOnly Through, ReportSection Section, BookstoreReport? Report)
{
    public static string Status(string status) => status switch
    {
        "Pending" => "Chờ xác nhận", "Confirmed" => "Đã xác nhận", "Packing" => "Đang đóng gói",
        "Shipped" => "Đang giao", "Completed" => "Hoàn tất", "Cancelled" => "Đã hủy", "Rejected" => "Bị từ chối",
        _ => status
    };
}
