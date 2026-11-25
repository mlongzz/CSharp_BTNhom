
using System.Drawing;
using System.Windows.Forms;
using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmDashboard : Form
{
    private readonly BookstoreApiClient api;
    private readonly Panel workspace = new();

    public FrmDashboard(BookstoreApiClient apiClient)
    {
        api = apiClient;

        Text = "Bookstore - Hệ thống quản trị";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(950, 600);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);

        // Layout chính: Sidebar + Header + Nội dung
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 210));

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 65));

        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        // Menu bên trái
        var sidebar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(35, 43, 55),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10)
        };

        sidebar.Controls.Add(CreateMenu(
            "Tổng quan", () => ShowWelcome()));

        sidebar.Controls.Add(CreateMenu(
            "Quản lý sách",
            () => OpenPage(new FrmBooks(api))));

        sidebar.Controls.Add(CreateMenu(
            "Đơn hàng",
            () => ShowPlaceholder("Quản lý đơn hàng")));

        sidebar.Controls.Add(CreateMenu(
            "Kho hàng",
            () => ShowPlaceholder("Quản lý kho")));

        sidebar.Controls.Add(CreateMenu(
            "Đổi trả",
            () => ShowPlaceholder("Quản lý đổi trả")));

        sidebar.Controls.Add(CreateMenu(
            "Voucher",
            () => ShowPlaceholder("Quản lý voucher")));

        sidebar.Controls.Add(CreateMenu(
            "Tài khoản",
            () => ShowPlaceholder("Quản lý tài khoản")));

        sidebar.Controls.Add(CreateMenu(
            "Báo cáo",
            () => ShowPlaceholder("Báo cáo thống kê")));

        sidebar.Controls.Add(CreateMenu(
            "Đăng xuất", () => Close()));

        // Header
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(244, 246, 248)
        };

        var lblHeader = new Label
        {
            Text = "BOOKSTORE MANAGEMENT | ADMIN",
            Font = new Font(
                "Segoe UI", 15, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0)
        };

        header.Controls.Add(lblHeader);

        workspace.Dock = DockStyle.Fill;
        workspace.BackColor = Color.White;
        workspace.Padding = new Padding(15);

        layout.Controls.Add(sidebar, 0, 0);
        layout.SetRowSpan(sidebar, 2);

        layout.Controls.Add(header, 1, 0);
        layout.Controls.Add(workspace, 1, 1);

        Controls.Add(layout);

        ShowWelcome();
    }

    private Button CreateMenu(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            Width = 180,
            Height = 46,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = Color.FromArgb(45, 55, 72),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0)
        };

        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();

        return button;
    }

    private void ShowWelcome()
    {
        ShowPlaceholder("Chào mừng đến hệ thống quản trị!");
    }

    private void ShowPlaceholder(string title)
    {
        var page = new Form
        {
            BackColor = Color.White
        };

        page.Controls.Add(new Label
        {
            Text = title + "\n\nChức năng sẽ được triển khai tiếp.",
            Dock = DockStyle.Fill,
            Font = new Font(
                "Segoe UI", 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        });

        OpenPage(page);
    }

    private void OpenPage(Form page)
    {
        foreach (Control control in workspace.Controls)
            control.Dispose();

        workspace.Controls.Clear();

        page.TopLevel = false;
        page.FormBorderStyle = FormBorderStyle.None;
        page.Dock = DockStyle.Fill;

        workspace.Controls.Add(page);
        page.Show();
    }
}
