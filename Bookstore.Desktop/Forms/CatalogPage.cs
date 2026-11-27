namespace Bookstore.Desktop.Forms;

internal abstract class CatalogPage : AdminPage
{
    protected DataGridView Grid { get; } = CreateGrid();
    protected FlowLayoutPanel Toolbar { get; } = CreateToolbar();

    protected CatalogPage(string title) : base(title)
    {
        Grid.AutoGenerateColumns = false;
        Body.Controls.Add(Grid);
        Body.Controls.Add(Toolbar);
    }

    protected Button AddButton(string text, Func<Task> click, bool primary = false)
    {
        var button = ActionButton(text, primary);
        button.Click += async (_, _) => await click();
        Toolbar.Controls.Add(button);
        return button;
    }

    protected DataGridViewTextBoxColumn AddColumn(string heading, string property, float weight, string? format = null)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = heading,
            DataPropertyName = property,
            FillWeight = weight,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" }
        };
        Grid.Columns.Add(column);
        return column;
    }

    protected bool Confirm(string message) => MessageBox.Show(this, message, Text,
        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
