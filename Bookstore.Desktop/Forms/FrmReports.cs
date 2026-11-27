using System.Globalization;
using Bookstore.Application;
using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmReports : AdminPage
{
    private readonly BookstoreApiClient api;
    private readonly DateTimePicker start = DateField("Từ ngày");
    private readonly DateTimePicker end = DateField("Đến ngày");
    private readonly Button export = ActionButton("Xuất Excel");
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView overview = CreateGrid("Chỉ tiêu", "Giá trị");
    private readonly DataGridView revenue = CreateGrid("Ngày", "Đơn hoàn tất", "Doanh thu");
    private readonly DataGridView orders = CreateGrid("Trạng thái", "Số đơn");
    private readonly DataGridView topBooks = CreateGrid("Sách", "Số cuốn đã bán");
    private readonly DataGridView inventory = CreateGrid("Sách", "Tồn hiện tại", "Ngưỡng", "Cảnh báo");
    private (DateOnly From, DateOnly Through)? loadedRange;

    public FrmReports(BookstoreApiClient api) : base("Báo cáo và thống kê")
    {
        this.api = api;
        var today = Today();
        start.Value = new DateTime(today.Year, today.Month, 1);
        end.Value = today.ToDateTime(TimeOnly.MinValue);

        var load = ActionButton("Xem báo cáo", primary: true);
        var toolbar = CreateToolbar();
        toolbar.Controls.AddRange([FieldLabel("Từ ngày"), start, FieldLabel("Đến ngày"), end, load, export]);
        AddTab("Tổng quan", "Overview", overview);
        AddTab("Doanh thu", "Revenue", revenue);
        AddTab("Đơn hàng", "Orders", orders);
        AddTab("Sách bán chạy", "TopBooks", topBooks);
        AddTab("Nhập và tồn kho", "Inventory", inventory);
        Body.Controls.Add(tabs);
        Body.Controls.Add(toolbar);

        export.Enabled = false;
        export.Click += async (_, _) => await ExportAsync();
        load.Click += async (_, _) => await LoadReportAsync();
        start.ValueChanged += (_, _) => FilterChanged();
        end.ValueChanged += (_, _) => FilterChanged();
        Shown += async (_, _) => await LoadReportAsync();
    }

    private (DateOnly From, DateOnly Through) Range => (DateOnly.FromDateTime(start.Value), DateOnly.FromDateTime(end.Value));

    private void FilterChanged()
    {
        export.Enabled = loadedRange == Range;
        if (!export.Enabled)
            SetStatus("Chọn Xem báo cáo để cập nhật khoảng ngày.");
        else if (loadedRange is { } range)
            SetStatus(PeriodLabel(range));
    }

    private void AddTab(string title, string section, DataGridView grid)
    {
        var page = new TabPage(title) { Tag = section, Padding = new Padding(5), BackColor = AdminTheme.Surface };
        page.Controls.Add(grid);
        tabs.TabPages.Add(page);
    }

    private Task LoadReportAsync()
    {
        var range = Range;
        if (range.From > range.Through)
        {
            SetStatus("Đến ngày phải bằng hoặc sau Từ ngày.", error: true);
            return Task.CompletedTask;
        }

        return RunAsync(async cancellationToken =>
        {
            var report = await api.GetReportAsync(range.From, range.Through, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowReport(report);
            loadedRange = range;
            export.Enabled = true;
            SetStatus(PeriodLabel(range));
        });
    }

    private void ShowReport(BookstoreReport report)
    {
        overview.Rows.Clear();
        overview.Rows.Add("Doanh thu hôm nay", Money(report.RevenueToday));
        overview.Rows.Add("Đơn mới hôm nay", report.NewOrdersToday);
        overview.Rows.Add("Đơn đang chờ xác nhận", report.PendingOrders);
        overview.Rows.Add("Doanh thu đơn hoàn tất trong kỳ", Money(report.Revenue.Sum(day => day.Revenue)));
        overview.Rows.Add("Số cuốn thực nhập trong kỳ", report.ReceivedQuantity);
        overview.Rows.Add("Giá trị thực nhập trong kỳ", Money(report.ReceivingCost));
        overview.Rows.Add("Sách dưới ngưỡng tồn hiện tại", report.Inventory.Count(book => book.Stock < book.Minimum));

        revenue.Rows.Clear();
        foreach (var day in report.Revenue)
        {
            var date = DateOnly.ParseExact(day.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            revenue.Rows.Add(date.ToString("dd/MM/yyyy"), day.CompletedOrders, Money(day.Revenue));
        }

        orders.Rows.Clear();
        foreach (var order in report.Orders)
            orders.Rows.Add(OrderStatusName(order.Status), order.Count);

        topBooks.Rows.Clear();
        foreach (var book in report.TopBooks)
            topBooks.Rows.Add(book.Title, book.Quantity);

        inventory.Rows.Clear();
        foreach (var book in report.Inventory)
        {
            var belowMinimum = book.Stock < book.Minimum;
            var row = inventory.Rows[inventory.Rows.Add(book.Title, book.Stock, book.Minimum, belowMinimum ? "Sắp hết" : "Đủ tồn")];
            if (belowMinimum)
                row.Cells[3].Style.ForeColor = Color.Firebrick;
        }
    }

    private Task ExportAsync()
    {
        if (loadedRange is not { } range || range != Range || tabs.SelectedTab?.Tag is not string section)
            return Task.CompletedTask;

        using var dialog = new SaveFileDialog
        {
            Title = "Xuất báo cáo Excel",
            Filter = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            FileName = $"Bao-cao-{range.From:yyyyMMdd}-{range.Through:yyyyMMdd}.xlsx"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return Task.CompletedTask;

        var path = dialog.FileName;
        return RunAsync(async cancellationToken =>
        {
            var workbook = await api.ExportReportAsync(range.From, range.Through, section, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SaveWorkbookAsync(path, workbook, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                SetStatus($"Đã xuất {Path.GetFileName(path)}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    SetStatus($"Không thể lưu báo cáo. {ex.Message}", error: true);
            }
        });
    }

    private static async Task SaveWorkbookAsync(string path, byte[] workbook, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, Path.GetRandomFileName());
        try
        {
            await File.WriteAllBytesAsync(temporary, workbook, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static DateTimePicker DateField(string name) => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130, AccessibleName = name
    };

    private static string PeriodLabel((DateOnly From, DateOnly Through) range) => $"Kỳ báo cáo {range.From:dd/MM/yyyy} – {range.Through:dd/MM/yyyy}";

    private static string OrderStatusName(string value) => value switch
    {
        "Pending" => "Chờ xác nhận",
        "Confirmed" => "Đã xác nhận",
        "Packing" => "Đang đóng gói",
        "Shipped" => "Đang giao",
        "Completed" => "Hoàn tất",
        "Cancelled" => "Đã hủy",
        "DeliveryFailed" => "Giao thất bại",
        "ReturnedToWarehouse" => "Đã hoàn về kho",
        "Rejected" => "Bị từ chối",
        _ => value
    };
}
