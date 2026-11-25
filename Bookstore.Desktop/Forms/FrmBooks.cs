
using System.Drawing;
using System.Windows.Forms;
using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmBooks : Form
{
    private readonly BookstoreApiClient api;

    private readonly DataGridView dgvBooks = new();
    private readonly TextBox txtSearch = new();
    private readonly Button btnSearch = new();

    public FrmBooks(BookstoreApiClient apiClient)
    {
        api = apiClient;

        Text = "Quản lý sách";
        BackColor = Color.White;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(10)
        };

        toolbar.Controls.Add(new Label
        {
            Text = "Tìm sách:",
            AutoSize = true,
            Margin = new Padding(5, 10, 8, 0)
        });

        txtSearch.Width = 300;
        toolbar.Controls.Add(txtSearch);

        btnSearch.Text = "Tìm kiếm / Tải lại";
        btnSearch.Width = 150;
        btnSearch.Height = 32;
        toolbar.Controls.Add(btnSearch);

        dgvBooks.Dock = DockStyle.Fill;
        dgvBooks.ReadOnly = true;
        dgvBooks.AllowUserToAddRows = false;
        dgvBooks.AllowUserToDeleteRows = false;
        dgvBooks.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        dgvBooks.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.Fill;

        Controls.Add(dgvBooks);
        Controls.Add(toolbar);

        btnSearch.Click += async (_, _) =>
            await LoadBooksAsync();

        Shown += async (_, _) =>
            await LoadBooksAsync();
    }

    private async Task LoadBooksAsync()
    {
        btnSearch.Enabled = false;

        try
        {
            var books = await api.GetBooksAsync(txtSearch.Text);
            dgvBooks.DataSource = books;
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                "Không tải được danh sách sách:\n" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show(
                "Kết nối API quá thời gian cho phép.");
        }
        finally
        {
            btnSearch.Enabled = true;
        }
    }
}
