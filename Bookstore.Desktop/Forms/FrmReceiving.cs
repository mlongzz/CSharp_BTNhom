using Bookstore.Desktop.Services;
using Bookstore.Domain;
using static Bookstore.Desktop.Forms.WarehouseForm;

namespace Bookstore.Desktop.Forms;

public sealed class FrmReceiving : Form
{
    private readonly BookstoreApiClient api;
    private readonly ComboBox statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, AccessibleName = "Trạng thái phiếu" };
    private readonly Button approve = Button("Duyệt phiếu");
    private readonly Button receive = Button("Xác nhận thực nhận");
    private readonly DataGridView purchases = Grid(
        Column("Mã phiếu", nameof(PurchaseRow.Id), 60),
        Column("Nhà cung cấp", nameof(PurchaseRow.Supplier), 160),
        Column("Người lập", nameof(PurchaseRow.CreatedBy), 90),
        Column("Ngày lập", nameof(PurchaseRow.CreatedAt), 115),
        Column("Trạng thái", nameof(PurchaseRow.Status), 90),
        Column("Tổng đặt (₫)", nameof(PurchaseRow.Total), 100, "N0"));
    private readonly DataGridView details = Grid(
        Column("Mã sách", nameof(PurchaseLine.BookId), 50),
        Column("Sách", nameof(PurchaseLine.Title), 210),
        Column("Số lượng đặt", nameof(PurchaseLine.Quantity), 88, "N0"),
        Column("Giá nhập (₫)", nameof(PurchaseLine.UnitCost), 92, "N0"),
        Column("Thực nhận", nameof(PurchaseLine.ReceivedQuantity), 80, "N0"));
    private readonly TextBox summary = new() { Dock = DockStyle.Fill, Height = 50, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TableLayoutPanel page;
    private List<PurchaseOrder> orders = [];

    private PurchaseOrder? SelectedPurchase => (purchases.CurrentRow?.DataBoundItem as PurchaseRow)?.Order;

    public FrmReceiving(BookstoreApiClient api)
    {
        this.api = api;
        Text = "Nhập kho";
        statusFilter.Items.AddRange(["Mọi trạng thái", "Chờ duyệt", "Đã duyệt", "Đã nhập"]);
        statusFilter.SelectedIndex = 0;

        var create = Button("Lập phiếu nhập");
        var reload = Button("Tải lại");
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        content.Controls.Add(purchases, 0, 0);
        content.Controls.Add(new Label { Text = "Chi tiết phiếu nhập", AutoSize = true, Margin = new Padding(3, 8, 3, 4) }, 0, 1);
        content.Controls.Add(details, 0, 2);
        content.Controls.Add(summary, 0, 3);
        page = Page(Text, Toolbar(statusFilter, create, approve, receive, reload), content);
        Controls.Add(page);

        create.Click += async (_, _) => await CreateAsync();
        approve.Click += async (_, _) => await ApproveAsync();
        receive.Click += async (_, _) => await ReceiveAsync();
        reload.Click += async (_, _) => await RunAsync(page, LoadAsync);
        Shown += async (_, _) => await RunAsync(page, LoadAsync);
        statusFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        purchases.SelectionChanged += (_, _) => ShowSelectedPurchase();
        ShowSelectedPurchase();
    }

    private async Task LoadAsync()
    {
        orders = await api.GetPurchasesAsync();
        if (!IsDisposed)
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedPurchase?.Id;
        var filter = statusFilter.SelectedIndex;
        purchases.DataSource = orders.Where(order => filter == 0 || filter == (int)order.Status + 1)
            .Select(order => new PurchaseRow(order)).ToList();
        foreach (DataGridViewRow row in purchases.Rows)
        {
            if (row.DataBoundItem is PurchaseRow { Id: var id } && id == selectedId)
            {
                purchases.CurrentCell = row.Cells[0];
                break;
            }
        }
        ShowSelectedPurchase();
    }

    private void ShowSelectedPurchase()
    {
        var order = SelectedPurchase;
        details.DataSource = order?.Lines.ToList();
        approve.Enabled = order?.Status == PurchaseStatus.Pending;
        receive.Enabled = order?.Status == PurchaseStatus.Approved;

        var information = new List<string>();
        if (!string.IsNullOrEmpty(order?.ApprovedBy))
            information.Add($"Duyệt bởi {order.ApprovedBy}");
        if (order?.ReceivedAt is DateTimeOffset receivedAt)
            information.Add($"Nhận bởi {order.ReceivedBy} · {FormatDate(receivedAt)}");
        if (!string.IsNullOrEmpty(order?.Note))
            information.Add($"Ghi chú · {order.Note}");
        summary.Lines = information.ToArray();
    }

    private async Task CreateAsync()
    {
        List<Supplier> suppliers = [];
        List<InventoryBook> books = [];
        if (!await RunAsync(page, async () =>
        {
            var suppliersTask = api.GetSuppliersAsync();
            var booksTask = api.GetInventoryAsync();
            await Task.WhenAll(suppliersTask, booksTask);
            suppliers = await suppliersTask;
            books = await booksTask;
        }))
            return;

        if (suppliers.Count == 0 || books.Count == 0)
        {
            MessageBox.Show(this, "Cần có ít nhất một nhà cung cấp và một đầu sách để lập phiếu.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var supplier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DataSource = suppliers, FormattingEnabled = true };
        supplier.Format += (_, args) =>
        {
            if (args.ListItem is Supplier item)
                args.Value = $"#{item.Id} · {item.Name}";
        };
        var book = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DataSource = books, FormattingEnabled = true };
        book.Format += (_, args) =>
        {
            if (args.ListItem is InventoryBook item)
                args.Value = $"#{item.Id} · {item.Title}";
        };
        var quantity = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = 1, ThousandsSeparator = true };
        var cost = new NumericUpDown { Minimum = 1, Maximum = int.MaxValue, Value = 1, ThousandsSeparator = true };
        var fields = Fields();
        AddField(fields, "Nhà cung cấp", supplier);
        AddField(fields, "Sách cần nhập", book);
        AddField(fields, "Số lượng đặt", quantity);
        AddField(fields, "Giá nhập mỗi cuốn (₫)", cost);

        var lines = Grid(
            Column("Mã sách", nameof(DraftLine.Id), 60),
            Column("Sách", nameof(DraftLine.Title), 200),
            Column("Số lượng", nameof(DraftLine.Quantity), 80, "N0"),
            Column("Giá nhập (₫)", nameof(DraftLine.UnitCost), 100, "N0"));
        var total = new Label { AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
        var draft = new Dictionary<int, DraftLine>();
        var add = Button("Thêm hoặc cập nhật dòng");
        var remove = Button("Bỏ dòng đã chọn");

        void RefreshLines()
        {
            lines.DataSource = draft.Values.ToList();
            total.Text = $"Tổng đặt · {draft.Values.Sum(line => (decimal)line.Quantity * line.UnitCost):N0} ₫";
        }

        add.Click += (_, _) =>
        {
            if (book.SelectedItem is not InventoryBook selected)
                return;
            draft[selected.Id] = new(selected.Id, selected.Title, (int)quantity.Value, (int)cost.Value);
            RefreshLines();
        };
        remove.Click += (_, _) =>
        {
            if (lines.CurrentRow?.DataBoundItem is not DraftLine selected)
                return;
            draft.Remove(selected.Id);
            RefreshLines();
        };
        RefreshLines();

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(fields, 0, 0);
        content.Controls.Add(Toolbar(add, remove), 0, 1);
        content.Controls.Add(lines, 0, 2);
        content.Controls.Add(total, 0, 3);

        if (ShowEditor(this, "Lập phiếu nhập", content, () =>
        {
            if (supplier.SelectedItem is not Supplier selected || draft.Count == 0)
                throw new ReceivingRuleException("Chọn nhà cung cấp và thêm ít nhất một dòng sách.");
            return api.CreatePurchaseAsync(selected.Id, draft.ToDictionary(item => item.Key,
                item => new PurchaseInput(item.Value.Quantity, item.Value.UnitCost)));
        }, width: 680, height: 490, submitText: "Gửi duyệt"))
        {
            statusFilter.SelectedIndex = 0;
            await RunAsync(page, LoadAsync);
        }
    }

    private async Task ApproveAsync()
    {
        if (SelectedPurchase is not { Status: PurchaseStatus.Pending } order ||
            MessageBox.Show(this, $"Duyệt phiếu nhập #{order.Id} của {order.Supplier}?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        await RunAsync(page, async () =>
        {
            await api.ApprovePurchaseAsync(order.Id);
            await LoadAsync();
        });
    }

    private async Task ReceiveAsync()
    {
        if (SelectedPurchase is not { Status: PurchaseStatus.Approved } order)
            return;

        var quantities = Grid(Column("Sách", "", 185), Column("Số lượng đặt", "", 90, "N0"), Column("Số lượng thực nhận", "", 120));
        quantities.ReadOnly = false;
        quantities.Columns[0].ReadOnly = true;
        quantities.Columns[1].ReadOnly = true;
        quantities.SelectionMode = DataGridViewSelectionMode.CellSelect;
        quantities.EditMode = DataGridViewEditMode.EditOnEnter;
        foreach (var line in order.Lines)
        {
            var index = quantities.Rows.Add(line.Title, line.Quantity, line.Quantity.ToString());
            quantities.Rows[index].Tag = line;
        }

        var note = new TextBox { MaxLength = 500, Multiline = true, Height = 63 };
        var fields = Fields();
        AddField(fields, "Ghi chú chênh lệch", note);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(quantities, 0, 0);
        content.Controls.Add(fields, 0, 1);

        if (ShowEditor(this, $"Nhận phiếu #{order.Id}", content, () =>
        {
            quantities.EndEdit();
            var actual = new Dictionary<int, int>();
            foreach (DataGridViewRow row in quantities.Rows)
            {
                var line = (PurchaseLine)row.Tag!;
                if (!int.TryParse(Convert.ToString(row.Cells[2].Value), out var count) || count < 0)
                    throw new ReceivingRuleException($"Số lượng thực nhận của «{line.Title}» phải là số nguyên không âm.");
                actual.Add(line.BookId, count);
            }
            return api.ReceivePurchaseAsync(order.Id, actual, note.Text);
        }, width: 680, height: 420, submitText: "Nhập kho"))
            await RunAsync(page, LoadAsync);
    }

    private sealed record DraftLine(int Id, string Title, int Quantity, int UnitCost);

    private sealed record PurchaseRow(PurchaseOrder Order)
    {
        public long Id => Order.Id;
        public string Supplier => Order.Supplier;
        public string CreatedBy => Order.CreatedBy;
        public string CreatedAt => FormatDate(Order.CreatedAt);
        public long Total => Order.Total;
        public string Status => Order.Status switch
        {
            PurchaseStatus.Pending => "Chờ duyệt",
            PurchaseStatus.Approved => "Đã duyệt",
            PurchaseStatus.Received => "Đã nhập",
            _ => Order.Status.ToString()
        };
    }
}
