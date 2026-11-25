using System.Text.Json;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal static class WarehouseForm
{
    internal static TableLayoutPanel Page(string title, Control toolbar, Control content)
    {
        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 15, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 10)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(content, 0, 2);
        return layout;
    }

    internal static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        toolbar.Controls.AddRange(controls);
        return toolbar;
    }

    internal static Button Button(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(7, 3, 7, 3)
    };

    internal static DataGridView Grid(params DataGridViewTextBoxColumn[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 251);
        grid.Columns.AddRange(columns);
        return grid;
    }

    internal static DataGridViewTextBoxColumn Column(string heading, string property, int weight = 100, string? format = null) => new()
    {
        HeaderText = heading,
        DataPropertyName = property,
        FillWeight = weight,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" }
    };

    internal static TableLayoutPanel Fields() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2,
        ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) }
    };

    internal static void AddField(TableLayoutPanel fields, string label, Control control)
    {
        var row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 10, 6)
        }, 0, row);
        control.Dock = DockStyle.Fill;
        control.AccessibleName = label;
        fields.Controls.Add(control, 1, row);
    }

    internal static bool ShowEditor(Form owner, string title, Control content, Func<Task> save,
        int width = 540, int height = 270, string submitText = "Lưu")
    {
        using var dialog = new Form
        {
            Text = title,
            Font = owner.Font,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(width, height),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };
        var buttons = Toolbar();
        buttons.FlowDirection = FlowDirection.RightToLeft;
        var confirm = Button(submitText);
        var cancel = Button("Hủy");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([confirm, cancel]);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(content, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        dialog.FormClosing += (_, args) => args.Cancel = !layout.Enabled;
        confirm.Click += async (_, _) =>
        {
            if (await RunAsync(layout, save))
                dialog.DialogResult = DialogResult.OK;
        };
        return dialog.ShowDialog(owner.TopLevelControl ?? owner) == DialogResult.OK;
    }

    internal static async Task<bool> RunAsync(Control surface, Func<Task> action)
    {
        surface.Enabled = false;
        try
        {
            await action();
            return !surface.IsDisposed;
        }
        catch (HttpRequestException exception)
        {
            ShowError(surface, exception.Message);
        }
        catch (OperationCanceledException)
        {
            ShowError(surface, "Kết nối API quá thời gian cho phép.");
        }
        catch (JsonException)
        {
            ShowError(surface, "Máy chủ trả về dữ liệu không hợp lệ.");
        }
        catch (ReceivingRuleException exception)
        {
            ShowError(surface, exception.Message);
        }
        finally
        {
            if (!surface.IsDisposed)
                surface.Enabled = true;
        }
        return false;
    }

    internal static string FormatDate(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");

    private static void ShowError(Control surface, string message)
    {
        if (!surface.IsDisposed)
            MessageBox.Show(surface.FindForm(), message, "Không thể hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
