using Bookstore.Desktop.Services;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmReturns : AdminPage
{
    private readonly BookstoreApiClient api;
    private readonly TextBox search = new()
    {
        Width = 250, PlaceholderText = "Mã yêu cầu, mã đơn, khách hoặc sách", AccessibleName = "Tìm yêu cầu đổi trả"
    };
    private readonly ComboBox status = new()
    {
        Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Trạng thái đổi trả"
    };
    private readonly DataGridView grid = CreateGrid();
    private readonly TextBox details = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White, AccessibleName = "Chi tiết yêu cầu đổi trả"
    };
    private readonly Button approve = ActionButton("Duyệt yêu cầu");
    private readonly Button reject = ActionButton("Từ chối yêu cầu");
    private readonly Button receive = ActionButton("Xác nhận đã nhận sách");
    private readonly FlowLayoutPanel actions = CreateToolbar();

    private ReturnRequest? SelectedRequest => grid.CurrentRow?.DataBoundItem as ReturnRequest;

    public FrmReturns(BookstoreApiClient api) : base("Quản lý đổi trả")
    {
        this.api = api;

        status.Items.Add(new StatusOption(null, "Tất cả trạng thái"));
        foreach (var item in new[] { "Pending", "Approved", "Rejected", "Completed" })
            status.Items.Add(new StatusOption(item, StatusLabel(item)));
        status.SelectedIndex = 0;

        var filters = CreateToolbar();
        var refresh = ActionButton("Lọc / Tải lại");
        filters.Controls.AddRange([search, status, refresh]);
        actions.Controls.AddRange([approve, receive, reject]);

        grid.AutoGenerateColumns = false;
        AddColumn(nameof(ReturnRequest.Id), "Mã Y/C", 55);
        AddColumn(nameof(ReturnRequest.OrderId), "Mã đơn", 55);
        AddColumn(nameof(ReturnRequest.Customer), "Khách hàng", 88);
        AddColumn(nameof(ReturnRequest.Title), "Sách", 145);
        AddColumn(nameof(ReturnRequest.Quantity), "SL", 40);
        AddColumn(nameof(ReturnRequest.Outcome), "Hình thức", 76);
        AddColumn(nameof(ReturnRequest.Status), "Trạng thái", 117);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(details, 0, 3);
        Body.Controls.Add(layout);

        grid.CurrentCellChanged += (_, _) => UpdateSelection();
        grid.CellFormatting += FormatCell;
        refresh.Click += async (_, _) => await RunAsync(token => ReloadAsync(token));
        approve.Click += async (_, _) => await ExecuteAsync("Approve", "Duyệt yêu cầu");
        reject.Click += async (_, _) => await ExecuteAsync("Reject", "Từ chối yêu cầu");
        receive.Click += async (_, _) => await ExecuteAsync("Receive", "Xác nhận đã nhận đủ sách còn nguyên vẹn cho yêu cầu");
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

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs entry)
    {
        if (entry.ColumnIndex < 0 || entry.Value is not string value)
            return;

        switch (grid.Columns[entry.ColumnIndex].DataPropertyName)
        {
            case nameof(ReturnRequest.Status):
                entry.Value = StatusLabel(value);
                entry.FormattingApplied = true;
                break;
            case nameof(ReturnRequest.Outcome):
                entry.Value = OutcomeLabel(value);
                entry.FormattingApplied = true;
                break;
        }
    }

    private async Task ReloadAsync(CancellationToken token, long? preserveId = null, string? success = null)
    {
        preserveId ??= SelectedRequest?.Id;
        var requests = await api.GetReturnsAsync(search.Text, (status.SelectedItem as StatusOption)?.Value, token);
        if (token.IsCancellationRequested)
            return;

        grid.DataSource = requests;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is ReturnRequest request && request.Id == preserveId)
            {
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
        UpdateSelection();
        var count = requests.Count == 0 ? "Không tìm thấy yêu cầu đổi trả." : $"{requests.Count} yêu cầu đổi trả";
        SetStatus(success is null ? count : $"{success}  ·  {count}");
    }

    private void UpdateSelection()
    {
        var request = SelectedRequest;
        approve.Visible = request?.Status == "Pending";
        receive.Visible = request?.Status == "Approved";
        reject.Visible = request?.Status is "Pending" or "Approved";
        reject.Text = request?.Status == "Approved" ? "Từ chối hàng không đạt" : "Từ chối yêu cầu";
        actions.Visible = request?.Status is "Pending" or "Approved";

        if (request is null)
        {
            details.Clear();
            return;
        }

        details.Text = $"Yêu cầu #{request.Id}  ·  Đơn #{request.OrderId}  ·  {StatusLabel(request.Status)}\r\n" +
            $"{request.Customer}  ·  {request.Title}  ·  {request.Quantity} cuốn  ·  {OutcomeLabel(request.Outcome)}\r\n" +
            $"Lý do  ·  {request.Reason}";
        if (request.Status == "Completed" && request.Outcome == "Refund")
            details.AppendText($"\r\nTiền hoàn đã ghi nhận  ·  {Money(request.RefundAmount)}");
    }

    private Task ExecuteAsync(string decision, string label)
    {
        if (SelectedRequest is not { } request)
            return Task.CompletedTask;

        if (MessageBox.Show(this, $"{label} #{request.Id}?", Text, MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return Task.CompletedTask;

        return RunAsync(async token =>
        {
            await api.UpdateReturnAsync(request.Id, request.Status, decision, token);
            if (token.IsCancellationRequested)
                return;
            var success = $"Đã cập nhật yêu cầu #{request.Id}.";
            SetStatus(success);
            grid.DataSource = null;
            await ReloadAsync(token, request.Id, success);
        });
    }

    private static string StatusLabel(string value) => value switch
    {
        "Pending" => "Chờ duyệt",
        "Approved" => "Đã duyệt, chờ nhận sách",
        "Rejected" => "Đã từ chối",
        "Completed" => "Hoàn tất",
        _ => value
    };

    private static string OutcomeLabel(string value) => value switch
    {
        "Refund" => "Hoàn tiền",
        "Exchange" => "Đổi sách",
        _ => value
    };

    private sealed record StatusOption(string? Value, string Label)
    {
        public override string ToString() => Label;
    }
}
