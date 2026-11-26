using System.Globalization;
using System.Text;
using Bookstore.Desktop.Services;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal abstract class OrderManagementPage : AdminPage
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private readonly BookstoreApiClient api;
    private readonly bool shipping;
    private readonly ComboBox status = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox search = new() { Width = 215, AccessibleName = "Tìm đơn hàng" };
    private readonly DateTimePicker from = DateInput("Từ");
    private readonly DateTimePicker to = DateInput("Đến");
    private readonly Button refresh = ActionButton("Lọc / Tải lại");
    private readonly DataGridView grid = CreateGrid();
    private readonly TextBox details = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White, AccessibleName = "Chi tiết và lịch sử đơn hàng"
    };
    private readonly TextBox reason = new() { Width = 210, MaxLength = 500, AccessibleName = "Lý do" };
    private readonly TextBox carrier = new() { Width = 155, MaxLength = 100, PlaceholderText = "Đơn vị vận chuyển", AccessibleName = "Đơn vị vận chuyển" };
    private readonly TextBox tracking = new() { Width = 140, MaxLength = 100, PlaceholderText = "Mã vận đơn", AccessibleName = "Mã vận đơn" };
    private readonly Button primary = ActionButton(string.Empty);
    private readonly Button secondary = ActionButton(string.Empty);
    private readonly FlowLayoutPanel actions = CreateToolbar();
    private long? selectedOrderId;

    private Order? SelectedOrder => grid.CurrentRow?.DataBoundItem as Order;

    protected OrderManagementPage(BookstoreApiClient api, string title, bool shipping) : base(title)
    {
        this.api = api;
        this.shipping = shipping;

        search.PlaceholderText = shipping ? "Mã đơn, khách, vận đơn, đơn vị" : "Mã đơn hoặc tài khoản khách";
        status.AccessibleName = "Trạng thái đơn hàng";
        status.Items.Add(new StatusOption(null, "Tất cả trạng thái"));
        foreach (var item in Enum.GetValues<OrderStatus>())
        {
            if (!shipping || item is not (OrderStatus.Pending or OrderStatus.Cancelled))
                status.Items.Add(new StatusOption(item, StatusLabel(item)));
        }
        status.SelectedIndex = 0;
        var filters = CreateToolbar();
        filters.Controls.AddRange([search, status, from, to, refresh]);
        actions.Controls.AddRange([carrier, tracking, primary, reason, secondary]);

        grid.AutoGenerateColumns = false;
        AddColumn(nameof(Order.Id), "Mã đơn", 48);
        AddColumn(nameof(Order.Customer), "Khách hàng", 100);
        AddColumn(nameof(Order.Status), "Trạng thái", 106);
        if (shipping)
        {
            AddColumn(nameof(Order.Carrier), "Đơn vị vận chuyển", 100);
            AddColumn(nameof(Order.TrackingCode), "Mã vận đơn", 85);
        }
        else
        {
            AddColumn(nameof(Order.CreatedAt), "Ngày đặt", 104);
            AddColumn(nameof(Order.Total), "Tổng thanh toán", 89);
        }

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(details, 0, 3);
        Body.Controls.Add(layout);

        grid.CurrentCellChanged += (_, _) => UpdateSelection();
        grid.CellFormatting += FormatCell;
        refresh.Click += async (_, _) => await RunAsync(token => ReloadAsync(token));
        primary.Click += async (_, _) => await ExecuteAsync(primary);
        secondary.Click += async (_, _) => await ExecuteAsync(secondary);
        search.KeyDown += (_, entry) =>
        {
            if (entry.KeyCode == Keys.Enter)
            {
                entry.SuppressKeyPress = true;
                refresh.PerformClick();
            }
        };
        Shown += async (_, _) => await RunAsync(token => ReloadAsync(token));
        UpdateSelection();
    }

    private void AddColumn(string property, string header, float weight) => grid.Columns.Add(new DataGridViewTextBoxColumn
    {
        DataPropertyName = property, HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private static DateTimePicker DateInput(string label) => new()
    {
        Width = 140, Format = DateTimePickerFormat.Custom, CustomFormat = $"'{label}' dd/MM/yyyy",
        ShowCheckBox = true, Checked = false, AccessibleName = $"{label} ngày"
    };

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs entry)
    {
        if (entry.ColumnIndex < 0)
            return;

        var formatted = entry.Value switch
        {
            OrderStatus value => StatusLabel(value),
            DateTimeOffset value => DateLabel(value),
            long value when grid.Columns[entry.ColumnIndex].DataPropertyName == nameof(Order.Total) => Money(value),
            _ => null
        };
        if (formatted is not null)
        {
            entry.Value = formatted;
            entry.FormattingApplied = true;
        }
    }

    private async Task ReloadAsync(CancellationToken token, long? preserveId = null, string? success = null)
    {
        preserveId ??= SelectedOrder?.Id;
        var orders = await api.GetOrdersAsync(shipping, search.Text, (status.SelectedItem as StatusOption)?.Value,
            from.Checked ? DateOnly.FromDateTime(from.Value) : null,
            to.Checked ? DateOnly.FromDateTime(to.Value) : null, token);
        if (token.IsCancellationRequested)
            return;

        grid.DataSource = orders;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is Order order && order.Id == preserveId)
            {
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
        UpdateSelection();
        var count = orders.Count == 0 ? "Không tìm thấy đơn hàng." : $"{orders.Count} đơn hàng";
        SetStatus(success is null ? count : $"{success}  ·  {count}");
    }

    private void UpdateSelection()
    {
        var order = SelectedOrder;
        if (order?.Id != selectedOrderId)
        {
            selectedOrderId = order?.Id;
            reason.Clear();
            carrier.Clear();
            tracking.Clear();
        }
        details.Text = order is null ? string.Empty : Describe(order);

        var orderStatus = order?.Status;
        var first = orderStatus switch
        {
            OrderStatus.Pending when !shipping => (OrderAction.Confirmed, "Xác nhận đơn"),
            OrderStatus.Confirmed => (OrderAction.Packing, "Bắt đầu đóng gói"),
            OrderStatus.Packing when shipping => (OrderAction.Shipped, "Bàn giao vận chuyển"),
            OrderStatus.Shipped when shipping && order is { DeliveryConfirmed: false } =>
                (OrderAction.DeliveryConfirmed, "Xác nhận đã giao"),
            OrderStatus.DeliveryFailed when shipping => (OrderAction.ReturnedToWarehouse, "Nhận hàng hoàn"),
            _ => ((OrderAction Action, string Label)?)null
        };
        var second = orderStatus switch
        {
            OrderStatus.Pending when !shipping => (OrderAction.Rejected, "Từ chối đơn"),
            OrderStatus.Shipped when shipping && order is { CanReportDeliveryFailure: true } =>
                (OrderAction.DeliveryFailed, "Giao thất bại"),
            _ => ((OrderAction Action, string Label)?)null
        };
        SetAction(primary, first);
        SetAction(secondary, second);
        carrier.Visible = tracking.Visible = first?.Action == OrderAction.Shipped;
        reason.Visible = second is not null;
        reason.PlaceholderText = orderStatus == OrderStatus.Pending ? "Lý do từ chối" : "Lý do giao thất bại";
        actions.Visible = first is not null || second is not null;
    }

    private static void SetAction(Button button, (OrderAction Action, string Label)? item)
    {
        button.Visible = item is not null;
        button.Text = item?.Label ?? string.Empty;
        button.Tag = item?.Action;
    }

    private Task ExecuteAsync(Button button)
    {
        if (SelectedOrder is not { } order || button.Tag is not OrderAction action)
            return Task.CompletedTask;

        if (action is (OrderAction.Rejected or OrderAction.DeliveryFailed) && string.IsNullOrWhiteSpace(reason.Text))
        {
            SetStatus("Vui lòng nhập lý do.", error: true);
            reason.Focus();
            return Task.CompletedTask;
        }
        if (action == OrderAction.Shipped && (string.IsNullOrWhiteSpace(carrier.Text) || string.IsNullOrWhiteSpace(tracking.Text)))
        {
            SetStatus("Vui lòng nhập đơn vị vận chuyển và mã vận đơn.", error: true);
            return Task.CompletedTask;
        }

        var prompt = action == OrderAction.ReturnedToWarehouse
            ? $"Bạn đã đối chiếu và nhận đủ sách còn nguyên vẹn của đơn #{order.Id}?"
            : $"{button.Text} cho đơn #{order.Id}?";
        if (MessageBox.Show(this, prompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return Task.CompletedTask;

        return RunAsync(async token =>
        {
            await api.UpdateOrderAsync(order.Id, action, carrier.Text.Trim(), tracking.Text.Trim(), reason.Text.Trim(), token);
            if (token.IsCancellationRequested)
                return;
            var success = $"Đã cập nhật đơn #{order.Id}.";
            SetStatus(success);
            grid.DataSource = null;
            await ReloadAsync(token, order.Id, success);
        });
    }

    private static string Describe(Order order)
    {
        var payment = order.PaymentStatus switch
        {
            "Paid" => "Đã thanh toán",
            "Refunded" => "Đã ghi nhận hoàn tiền",
            _ => "Chưa thu tiền"
        };
        var description = new StringBuilder()
            .AppendLine($"Đơn #{order.Id}  ·  {StatusLabel(order.Status)}  ·  {DateLabel(order.CreatedAt)}")
            .AppendLine($"Khách hàng {order.Customer}  ·  Giao đến {order.Address}")
            .AppendLine($"Tổng {Money(order.Total)}  ·  Giảm giá {Money(order.Discount)}  ·  Phí vận chuyển {Money(order.ShippingFee)}")
            .AppendLine($"{(order.PaymentMethod == "Cod" ? "COD" : "Trực tuyến")}  ·  {payment}");
        if (order.TrackingCode is not null)
            description.AppendLine($"Vận đơn {order.TrackingCode}  ·  {order.Carrier}");
        if (order.Status == OrderStatus.Shipped && order.DeliveryConfirmed)
            description.AppendLine("Đã xác nhận giao hàng. Đang chờ khách xác nhận đã nhận hàng.");
        else if (order.Status == OrderStatus.Shipped && order.ReceiptConfirmed)
            description.AppendLine("Khách đã xác nhận nhận được hàng. Đang chờ quản trị viên xác nhận đã giao hàng.");
        if (order.RejectionReason is not null)
            description.AppendLine($"Lý do  ·  {order.RejectionReason}");

        description.AppendLine().AppendLine("Sách trong đơn");
        foreach (var line in order.Lines)
            description.AppendLine($"• {line.Title}  ·  {line.Quantity} × {Money(line.Price)}");

        description.AppendLine().AppendLine("Lịch sử xử lý");
        if (order.Events.Count == 0)
            description.AppendLine("Đơn được tạo trước khi có lịch sử xử lý.");
        foreach (var entry in order.Events)
            description.AppendLine($"• {DateLabel(entry.OccurredAt)}  ·  {ActionLabel(entry.Action)}  ·  {entry.Actor}");
        return description.ToString();
    }

    private static string DateLabel(DateTimeOffset? value) => value is { } date
        ? date.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", Vietnamese) : "—";

    private static string StatusLabel(OrderStatus value) => value switch
    {
        OrderStatus.Pending => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Packing => "Đang đóng gói",
        OrderStatus.Shipped => "Đang giao",
        OrderStatus.Completed => "Hoàn tất",
        OrderStatus.Cancelled => "Đã hủy",
        OrderStatus.DeliveryFailed => "Giao thất bại",
        OrderStatus.ReturnedToWarehouse => "Đã hoàn về kho",
        _ => value.ToString()
    };

    private static string ActionLabel(OrderAction value) => value switch
    {
        OrderAction.Placed => "Đặt hàng",
        OrderAction.Confirmed => "Xác nhận đơn",
        OrderAction.Packing => "Bắt đầu đóng gói",
        OrderAction.Shipped => "Bàn giao vận chuyển",
        OrderAction.DeliveryConfirmed => "Quản trị viên xác nhận đã giao hàng",
        OrderAction.ReceiptConfirmed => "Khách xác nhận đã nhận hàng",
        OrderAction.Completed => "Đơn hàng hoàn tất",
        OrderAction.Cancelled => "Hủy đơn",
        OrderAction.Rejected => "Từ chối đơn",
        OrderAction.DeliveryFailed => "Giao thất bại",
        OrderAction.ReturnedToWarehouse => "Nhận hàng hoàn",
        _ => value.ToString()
    };

    private sealed record StatusOption(OrderStatus? Value, string Label)
    {
        public override string ToString() => Label;
    }
}
