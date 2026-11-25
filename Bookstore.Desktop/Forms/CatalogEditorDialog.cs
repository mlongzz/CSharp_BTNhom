using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal sealed class CatalogEditorDialog : Form
{
    private readonly TableLayoutPanel fields = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(14, 10, 14, 0)
    };

    private bool saving;

    public CatalogEditorDialog(string title, int height, Func<Task<string?>> saveAsync, CancellationToken cancellationToken)
    {
        Text = title;
        ClientSize = new Size(565, height);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;

        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var save = new Button { Text = "Lưu", AutoSize = true, Height = 31 };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, AutoSize = true, Height = 31 };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(10, 4, 10, 8)
        };
        footer.Controls.AddRange([cancel, save]);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(fields);
        Controls.Add(scroll);
        Controls.Add(footer);
        AcceptButton = save;
        CancelButton = cancel;

        save.Click += async (_, _) =>
        {
            saving = true;
            fields.Enabled = save.Enabled = cancel.Enabled = false;
            bool saved = false;
            try
            {
                var error = await saveAsync();
                if (error is null)
                    saved = true;
                else
                    ShowError(error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                ShowError("Kết nối API quá thời gian cho phép.");
            }
            catch (HttpRequestException exception)
            {
                ShowError(exception.Message);
            }
            finally
            {
                saving = false;
                fields.Enabled = save.Enabled = cancel.Enabled = true;
            }

            if (saved)
                DialogResult = DialogResult.OK;
        };
        FormClosing += (_, e) => e.Cancel |= saving;
    }

    public void AddField(string label, Control input)
    {
        int row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 0)
        }, 0, row);
        input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        input.Margin = new Padding(0, 3, 0, 5);
        input.AccessibleName = label;
        fields.Controls.Add(input, 1, row);
    }

    public static ComboBox ReferencePicker(IEnumerable<CatalogReference> items, int? selected, string placeholder)
    {
        var options = new List<ReferenceOption> { new(null, placeholder) };
        options.AddRange(items.Select(item => new ReferenceOption(item.Id, item.Name)));
        var picker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(ReferenceOption.Name)
        };
        picker.Items.AddRange(options.ToArray());
        picker.SelectedIndex = Math.Max(0, options.FindIndex(option => option.Id == selected));
        return picker;
    }

    public static int? ReferenceId(ComboBox picker) => (picker.SelectedItem as ReferenceOption)?.Id;

    private void ShowError(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed record ReferenceOption(int? Id, string Name);
}
