using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmDashboard : Form
{
    private readonly Panel workspace = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel menu = new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
        Padding = new Padding(12, 4, 12, 8)
    };
    private readonly Label heading = new()
    {
        Dock = DockStyle.Top, Height = 68, Font = new Font("Segoe UI", 18, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(20, 0, 20, 0),
        BackColor = AdminTheme.Forest, ForeColor = AdminTheme.Surface
    };
    private Form? currentPage;
    private Button? currentMenu;

    public FrmDashboard(BookstoreApiClient apiClient)
    {
        Text = "Bookstore - Hệ thống quản trị";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(980, 640);
        BackColor = AdminTheme.Background;
        ForeColor = AdminTheme.Text;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = AdminTheme.Border, ColumnCount = 2, RowCount = 2,
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 248), new ColumnStyle(SizeType.Percent, 100) },
            RowStyles = { new RowStyle(SizeType.Absolute, 69), new RowStyle(SizeType.Percent, 100) }
        };
        var header = CreateHeader();
        var sidebar = CreateSidebar(apiClient.Username);
        var content = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 20, 22, 16),
            BackColor = AdminTheme.Background
        };
        content.Controls.Add(workspace);
        content.Controls.Add(heading);
        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 2);
        layout.Controls.Add(sidebar, 0, 1);
        layout.Controls.Add(content, 1, 1);
        Controls.Add(layout);

        var overview = AddPage("Tổng quan", () => new DashboardOverview(apiClient));
        AddPage("Quản lý sách", () => new FrmBooks(apiClient));
        AddPage("Thể loại, tác giả, NXB", () => new FrmCategories(apiClient));
        AddPage("Đơn hàng", () => new FrmOrders(apiClient));
        AddPage("Vận chuyển", () => new FrmShipping(apiClient));
        AddPage("Đổi trả", () => new FrmReturns(apiClient));
        AddPage("Nhà cung cấp", () => new FrmSuppliers(apiClient));
        AddPage("Nhập kho", () => new FrmReceiving(apiClient));
        AddPage("Tồn kho và kiểm kê", () => new FrmInventory(apiClient));
        AddPage("Tài khoản và phân quyền", () => new FrmAccounts(apiClient));
        AddPage("Báo cáo và thống kê", () => new FrmReports(apiClient));

        Shown += (_, _) => overview.PerformClick();
    }

    private TableLayoutPanel CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 1), BackColor = AdminTheme.Border,
            ColumnCount = 2, RowCount = 1,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.Absolute, 180) },
            RowStyles = { new RowStyle(SizeType.Percent, 100) }
        };
        header.Controls.Add(new Label
        {
            Text = "Bookstore", Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = AdminTheme.Surface, ForeColor = AdminTheme.Forest,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 20, FontStyle.Bold)
        }, 0, 0);

        var logoutArea = new Panel
        {
            Dock = DockStyle.Fill, Margin = new Padding(1, 0, 0, 0),
            Padding = new Padding(22, 14, 22, 14), BackColor = AdminTheme.Surface
        };
        var logout = new Button { Text = "Đăng xuất", Dock = DockStyle.Fill };
        AdminTheme.StyleButton(logout);
        logout.Click += (_, _) => Close();
        logoutArea.Controls.Add(logout);
        header.Controls.Add(logoutArea, 1, 0);
        return header;
    }

    private Panel CreateSidebar(string? username)
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 1, 0), BackColor = AdminTheme.Surface
        };
        var navigation = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        navigation.Controls.Add(menu);
        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(new Label
        {
            Text = "Menu chức năng", Dock = DockStyle.Top, Height = 55, Padding = new Padding(20, 0, 12, 0),
            ForeColor = AdminTheme.Forest, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        });
        sidebar.Controls.Add(new Label
        {
            Text = username is null ? "Admin" : $"Admin · {username}", Dock = DockStyle.Bottom, Height = 44,
            Padding = new Padding(20, 0, 12, 0), AutoEllipsis = true, BackColor = AdminTheme.Tint,
            ForeColor = AdminTheme.Muted, TextAlign = ContentAlignment.MiddleLeft
        });
        return sidebar;
    }

    private Button AddPage(string caption, Func<Form> create)
    {
        var button = new Button
        {
            Text = caption, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(9, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5f)
        };
        StyleMenuButton(button, active: false);
        var row = menu.RowCount++;
        menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        menu.Controls.Add(button, 0, row);
        button.Click += (_, _) =>
        {
            if (currentMenu == button)
                return;

            if (currentMenu is not null)
                StyleMenuButton(currentMenu, active: false);
            currentMenu = button;
            StyleMenuButton(button, active: true);
            OpenPage(create(), caption);
        };
        return button;
    }

    private static void StyleMenuButton(Button button, bool active)
    {
        AdminTheme.StyleButton(button, primary: active);
        button.FlatAppearance.BorderSize = 0;
    }

    private void OpenPage(Form page, string caption)
    {
        currentPage?.Dispose();
        currentPage = page;
        heading.Text = caption;
        heading.Visible = page is not FrmCategories;
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
            var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 102, ColumnCount = 4, RowCount = 1 };
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
                Text = "Sách dưới ngưỡng tồn", Dock = DockStyle.Top, Height = 46, ForeColor = AdminTheme.Forest,
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
                Dock = DockStyle.Fill, BackColor = AdminTheme.Surface, Padding = new Padding(13, 10, 13, 9)
            };
            value.Text = "—";
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.AutoEllipsis = true;
            value.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            value.ForeColor = AdminTheme.Forest;
            card.Controls.Add(value);
            card.Controls.Add(new Label
            {
                Text = title, Dock = DockStyle.Top, Height = 27, AutoEllipsis = true, ForeColor = AdminTheme.Muted
            });
            return card;
        }
    }
}
