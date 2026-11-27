namespace Bookstore.Desktop.Forms;

internal static class AdminTheme
{
    public static readonly Color Forest = Color.FromArgb(23, 69, 54);
    public static readonly Color Primary = Color.FromArgb(22, 84, 63);
    public static readonly Color Background = Color.FromArgb(243, 247, 245);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(213, 226, 219);
    public static readonly Color Text = Color.FromArgb(25, 46, 38);
    public static readonly Color Muted = Color.FromArgb(83, 107, 96);
    public static readonly Color Tint = Color.FromArgb(233, 242, 237);

    private static readonly Color Selection = Color.FromArgb(212, 234, 222);

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Primary : Surface;
        button.ForeColor = primary ? Surface : Primary;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Primary : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Forest : Tint;
        button.FlatAppearance.MouseDownBackColor = primary ? Forest : Selection;
        button.Cursor = Cursors.Hand;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 38;
        grid.RowTemplate.Height = 33;

        var header = grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = Tint;
        header.ForeColor = Forest;
        header.SelectionBackColor = Tint;
        header.SelectionForeColor = Forest;
        header.Padding = new Padding(7, 0, 7, 0);

        var cell = grid.DefaultCellStyle;
        cell.BackColor = Surface;
        cell.ForeColor = Text;
        cell.SelectionBackColor = Selection;
        cell.SelectionForeColor = Forest;
        cell.Padding = new Padding(7, 0, 7, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Background;
    }
}
