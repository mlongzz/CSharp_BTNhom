using System.Globalization;

namespace Bookstore.Desktop.Forms;

internal abstract class AdminPage : Form
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ToolStripStatusLabel status = new();

    protected Panel Body { get; } = new() { Dock = DockStyle.Fill };

    protected AdminPage(string title)
    {
        Text = title;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var footer = new StatusStrip { SizingGrip = false };
        footer.Items.Add(status);
        Controls.Add(Body);
        Controls.Add(footer);
    }

    protected void SetStatus(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    protected async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsDisposed || !Body.Enabled)
            return;

        const string busy = "Đang xử lý…";
        var previousStatus = status.Text;
        var previousColor = status.ForeColor;
        var cancellationToken = lifetime.Token;
        Body.Enabled = false;
        SetStatus(busy);

        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                SetStatus("Kết nối backend quá thời gian cho phép.", error: true);
        }
        catch (HttpRequestException ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                SetStatus(ex.StatusCode is null ? $"Lỗi kết nối backend. {ex.Message}" : ex.Message, error: true);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Body.Enabled = true;
                if (status.Text == busy)
                {
                    status.Text = previousStatus;
                    status.ForeColor = previousColor;
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    protected static DataGridView CreateGrid(params string[] headings)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        foreach (var heading in headings)
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = heading, MinimumWidth = 85 });
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 251);
        return grid;
    }

    protected static FlowLayoutPanel CreateToolbar() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(0, 3, 0, 7)
    };

    protected static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 8, 4, 0) };

    protected static Button ActionButton(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(0, 29) };

    protected static DateOnly Today() => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    protected static string Money(long amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}
