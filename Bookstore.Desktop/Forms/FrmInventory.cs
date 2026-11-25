using Bookstore.Desktop.Services;
using Bookstore.Domain;
using static Bookstore.Desktop.Forms.WarehouseForm;

namespace Bookstore.Desktop.Forms;

public sealed class FrmInventory : Form
{
    private readonly BookstoreApiClient api;
    private readonly TextBox search = new() { Width = 185, PlaceholderText = "Tìm đầu sách", AccessibleName = "Tìm đầu sách" };
    private readonly CheckBox lowOnly = new() { Text = "Sắp hết", AutoSize = true, Margin = new Padding(7, 9, 7, 3) };
    private readonly Button minimum = Button("Đặt ngưỡng");
    private readonly Button reconcile = Button("Kiểm kê");
    private readonly Button byBook = Button("Biến động sách đã chọn");
    private readonly Label historyTitle = new() { Text = "Lịch sử biến động", AutoSize = true, Margin = new Padding(3, 9, 8, 3) };
    private readonly DataGridView stock = Grid(
        Column("Mã", nameof(InventoryBook.Id), 45),
        Column("Sách", nameof(InventoryBook.Title), 230),
        Column("Tồn thực tế", nameof(InventoryBook.Stock), 85, "N0"),
        Column("Ngưỡng", nameof(InventoryBook.Minimum), 70, "N0"),
        Column("Cảnh báo", nameof(StockRow.Warning), 75));
    private readonly DataGridView history = Grid(
        Column("Thời gian", nameof(MovementRow.Time), 120),
        Column("Sách", nameof(MovementRow.Title), 135),
        Column("Thay đổi", nameof(MovementRow.Delta), 62, "+#,0;-#,0;0"),
        Column("Nghiệp vụ", nameof(MovementRow.Kind), 105),
        Column("Mã phiếu", nameof(MovementRow.Reference), 68),
        Column("Người thực hiện", nameof(MovementRow.Actor), 95),
        Column("Ghi chú", nameof(MovementRow.Note), 140));
    private readonly TableLayoutPanel page;
    private List<InventoryBook> books = [];
    private InventoryBook? historyBook;

    private InventoryBook? SelectedBook => (stock.CurrentRow?.DataBoundItem as StockRow)?.Book;

    public FrmInventory(BookstoreApiClient api)
    {
        this.api = api;
        Text = "Tồn kho và kiểm kê";

        var reload = Button("Tải lại");
        var all = Button("Tất cả biến động");
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        content.Controls.Add(stock, 0, 0);
        content.Controls.Add(Toolbar(historyTitle, byBook, all), 0, 1);
        content.Controls.Add(history, 0, 2);
        page = Page(Text, Toolbar(search, lowOnly, minimum, reconcile, reload), content);
        Controls.Add(page);

        Shown += async (_, _) => await RunAsync(page, LoadAsync);
        reload.Click += async (_, _) => await RunAsync(page, LoadAsync);
        byBook.Click += async (_, _) => await RunAsync(page, () => LoadHistoryAsync(SelectedBook));
        all.Click += async (_, _) => await RunAsync(page, () => LoadHistoryAsync(null));
        minimum.Click += async (_, _) => await EditBookAsync(SelectedBook, false);
        reconcile.Click += async (_, _) => await EditBookAsync(SelectedBook, true);
        search.TextChanged += (_, _) => ApplyFilter();
        lowOnly.CheckedChanged += (_, _) => ApplyFilter();
        stock.SelectionChanged += (_, _) => UpdateActions();
        UpdateActions();
    }

    private async Task LoadAsync()
    {
        var inventory = api.GetInventoryAsync();
        var movements = api.GetStockMovementsAsync(historyBook?.Id);
        await Task.WhenAll(inventory, movements);
        if (IsDisposed)
            return;

        books = await inventory;
        ApplyFilter();
        ShowHistory(await movements, historyBook);
    }

    private async Task LoadHistoryAsync(InventoryBook? book)
    {
        var movements = await api.GetStockMovementsAsync(book?.Id);
        if (!IsDisposed)
            ShowHistory(movements, book);
    }

    private void ShowHistory(IEnumerable<StockMovement> movements, InventoryBook? book)
    {
        historyBook = book;
        historyTitle.Text = book is null ? "Lịch sử biến động" : $"Biến động sách #{book.Id}";
        history.DataSource = movements.Select(movement => new MovementRow(movement)).ToList();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedBook?.Id;
        var keyword = search.Text.Trim();
        stock.DataSource = books.Where(book => (!lowOnly.Checked || book.Stock < book.Minimum)
            && (book.Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) || book.Id.ToString() == keyword))
            .Select(book => new StockRow(book)).ToList();
        foreach (DataGridViewRow row in stock.Rows)
        {
            if (row.DataBoundItem is StockRow { Id: var id } && id == selectedId)
            {
                stock.CurrentCell = row.Cells[0];
                break;
            }
        }
        UpdateActions();
    }

    private void UpdateActions() => minimum.Enabled = reconcile.Enabled = byBook.Enabled = SelectedBook is not null;

    private async Task EditBookAsync(InventoryBook? book, bool adjust)
    {
        if (book is null)
            return;

        var fields = Fields();
        AddField(fields, "Sách", new TextBox { ReadOnly = true, Text = book.Title });
        var value = new NumericUpDown { Maximum = int.MaxValue, Value = adjust ? book.Stock : book.Minimum, ThousandsSeparator = true };
        var reason = new TextBox { MaxLength = 500, Multiline = true, Height = 68 };
        if (adjust)
            AddField(fields, "Tồn theo hệ thống", new TextBox { ReadOnly = true, Text = book.Stock.ToString("N0") });
        AddField(fields, adjust ? "Số lượng kiểm kê" : "Ngưỡng cảnh báo", value);
        if (adjust)
            AddField(fields, "Lý do kiểm kê", reason);

        var title = adjust ? "Ghi nhận kiểm kê" : "Đặt ngưỡng cảnh báo";
        if (ShowEditor(this, title, fields, () => adjust
            ? api.ReconcileStockAsync(book.Id, book.Stock, (int)value.Value, reason.Text)
            : api.SetStockMinimumAsync(book.Id, (int)value.Value), height: adjust ? 260 : 160))
            await RunAsync(page, LoadAsync);
    }

    private sealed record StockRow(InventoryBook Book)
    {
        public int Id => Book.Id;
        public string Title => Book.Title;
        public int Stock => Book.Stock;
        public int Minimum => Book.Minimum;
        public string Warning => Stock < Minimum ? "Sắp hết" : "Đủ tồn";
    }

    private sealed record MovementRow(StockMovement Movement)
    {
        public string Time => FormatDate(Movement.OccurredAt);
        public string Title => Movement.Title;
        public int Delta => Movement.Delta;
        public long? Reference => Movement.Reference;
        public string Actor => Movement.Actor;
        public string? Note => Movement.Note;
        public string Kind => Movement.Kind switch
        {
            "Receipt" => "Nhập kho",
            "Sale" => "Bán sách",
            "Cancellation" => "Hủy đơn",
            "DeliveryReturn" => "Hoàn kho giao thất bại",
            "Return" => "Trả sách",
            "Exchange" => "Đổi sách",
            "Adjustment" => "Kiểm kê",
            _ => Movement.Kind
        };
    }
}
