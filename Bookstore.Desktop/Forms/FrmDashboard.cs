using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmDashboard : Form
{
    private static readonly Color SidebarColor = Color.FromArgb(35, 43, 55);
    private static readonly Color MenuColor = Color.FromArgb(45, 55, 72);
    private static readonly Color ActiveMenuColor = Color.FromArgb(47, 92, 150);

    private readonly BookstoreApiClient api;
    private readonly Panel workspace = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16) };
    private readonly FlowLayoutPanel sidebar = new()
    {
        Dock = DockStyle.Fill, BackColor = SidebarColor, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(12)
    };
    private readonly Label heading = new()
    {
        Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0)
    };
    private Form? currentPage;
    private Button? currentMenu;

    public FrmDashboard(BookstoreApiClient apiClient)
    {
        api = apiClient;
        Text = "Bookstore - Hệ thống quản trị";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(980, 640);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        sidebar.Controls.Add(new Label
        {
            Text = "Bookstore", ForeColor = Color.White, Font = new Font("Segoe UI", 17, FontStyle.Bold),
            Width = 176, Height = 40, Margin = new Padding(2, 0, 0, 6)
        });
        var overview = AddPage("Tổng quan", () => new DashboardOverview(api));
        AddPage("Quản lý sách", () => new FrmBooks(api));
        AddPage("Thể loại, tác giả, NXB", () => new FrmCategories(api));
        AddPage("Đơn hàng", () => new FrmOrders(api));
        AddPage("Vận chuyển", () => new FrmShipping(api));
        AddPage("Đổi trả", () => new FrmReturns(api));
        AddPage("Nhà cung cấp", () => new FrmSuppliers(api));
        AddPage("Nhập kho", () => new FrmReceiving(api));
        AddPage("Tồn kho và kiểm kê", () => new FrmInventory(api));
        AddPage("Tài khoản và phân quyền", () => new FrmAccounts(api));
        AddPage("Báo cáo và thống kê", () => new FrmReports(api));

        var logout = CreateMenu("Đăng xuất");
        logout.Margin = new Padding(0, 14, 0, 3);
        sidebar.Controls.Add(logout);
        logout.Click += (_, _) => Close();

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 246, 248) };
        header.Controls.Add(heading);
        header.Controls.Add(new Label
        {
            Text = api.Username is { } username ? $"Admin · {username}" : "Admin",
            AutoEllipsis = true, Dock = DockStyle.Right, Width = 190, TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 18, 0)
        });
        layout.Controls.Add(sidebar, 0, 0);
        layout.SetRowSpan(sidebar, 2);
        layout.Controls.Add(header, 1, 0);
        layout.Controls.Add(workspace, 1, 1);
        Controls.Add(layout);

        Shown += (_, _) => overview.PerformClick();
    }

    private Button AddPage(string caption, Func<Form> create)
    {
        var button = CreateMenu(caption);
        sidebar.Controls.Add(button);
        button.Click += (_, _) =>
        {
            if (currentMenu == button)
                return;

            if (currentMenu is not null)
                currentMenu.BackColor = MenuColor;
            currentMenu = button;
            button.BackColor = ActiveMenuColor;
            OpenPage(create(), caption);
        };
        return button;
    }

    private static Button CreateMenu(string text)
    {
        var button = new Button
        {
            Text = text, Width = 174, Height = 38, Margin = new Padding(0, 2, 0, 2),
            BackColor = MenuColor, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(9, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5f)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void OpenPage(Form page, string caption)
    {
        currentPage?.Dispose();
        currentPage = page;
        heading.Text = caption;
        page.TopLevel = false;
        page.FormBorderStyle = FormBorderStyle.None;
        page.Dock = DockStyle.Fill;
        workspace.Controls.Add(page);
        page.Show();
    }

    private sealed class DashboardOverview : AdminPage
    {
        private readonly BookstoreApiClient api;
        private readonly Label revenue = new();
        private readonly Label newOrders = new();
        private readonly Label pendingOrders = new();
        private readonly Label lowStock = new();
        private readonly DataGridView inventory = CreateGrid("Sách", "Tồn hiện tại", "Ngưỡng");

        public DashboardOverview(BookstoreApiClient api) : base("Tổng quan")
        {
            this.api = api;
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 88, ColumnCount = 4, RowCount = 1 };
            foreach (var (index, title, value) in new[]
                     {
                         (0, "Doanh thu hôm nay", revenue), (1, "Đơn mới hôm nay", newOrders),
                         (2, "Đơn chờ xác nhận", pendingOrders), (3, "Sách sắp hết", lowStock)
                     })
            {
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                cards.Controls.Add(CreateCard(title, value), index, 0);
            }

            var refresh = ActionButton("Tải lại");
            var toolbar = CreateToolbar();
            toolbar.Controls.Add(refresh);
            var inventoryTitle = new Label
            {
                Text = "Sách dưới ngưỡng tồn", Dock = DockStyle.Top, Height = 38,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };

            Body.Controls.Add(inventory);
            Body.Controls.Add(inventoryTitle);
            Body.Controls.Add(cards);
            Body.Controls.Add(toolbar);

            refresh.Click += async (_, _) => await LoadOverviewAsync();
            Shown += async (_, _) => await LoadOverviewAsync();
        }

        private Task LoadOverviewAsync() => RunAsync(async cancellationToken =>
        {
            var today = Today();
            var report = await api.GetReportAsync(today, today, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            revenue.Text = Money(report.RevenueToday);
            newOrders.Text = report.NewOrdersToday.ToString();
            pendingOrders.Text = report.PendingOrders.ToString();

            var warnings = report.Inventory.Where(book => book.Stock < book.Minimum).ToList();
            lowStock.Text = warnings.Count.ToString();
            inventory.Rows.Clear();
            foreach (var book in warnings)
                inventory.Rows.Add(book.Title, book.Stock, book.Minimum);

            SetStatus(warnings.Count == 0 ? "Không có sách dưới ngưỡng tồn." : $"{warnings.Count} sách dưới ngưỡng tồn.");
        });

        private static Panel CreateCard(string title, Label value)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(237, 243, 250), Padding = new Padding(10, 7, 10, 6)
            };
            value.Text = "—";
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.AutoEllipsis = true;
            value.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            card.Controls.Add(value);
            card.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 25, AutoEllipsis = true });
            return card;
        }
    }
}
