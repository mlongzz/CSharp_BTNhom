using Bookstore.Desktop.Services;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmCategories : CatalogPage
{
    private static readonly ReferenceType[] ReferenceTypes =
    [
        new(ReferenceKind.Category, "Thể loại", "Mô tả"),
        new(ReferenceKind.Author, "Tác giả", "Tiểu sử"),
        new(ReferenceKind.Publisher, "Nhà xuất bản", "Địa chỉ")
    ];

    private readonly BookstoreApiClient api;
    private readonly Button edit;
    private readonly Button delete;
    private readonly Label selectedName = new()
    {
        Dock = DockStyle.Top, Height = 36, AutoEllipsis = true,
        ForeColor = AdminTheme.Forest, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label parentHeading = CreateDetailHeading("Thể loại cha");
    private readonly Label parentName = new()
    {
        Dock = DockStyle.Top, Height = 30, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label descriptionHeading = CreateDetailHeading("Mô tả");
    private readonly TextBox description = new()
    {
        Dock = DockStyle.Top, Height = 108, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = AdminTheme.Background, ForeColor = AdminTheme.Text
    };
    private ReferenceType currentType = ReferenceTypes[0];
    private CatalogReferences references = new([], [], []);

    public FrmCategories(BookstoreApiClient apiClient) : base("Thể loại, tác giả và nhà xuất bản")
    {
        api = apiClient;

        AddButton("Thêm", () => EditReferenceAsync(null), primary: true);
        edit = AddButton("Sửa", EditSelectedReferenceAsync);
        delete = AddButton("Xóa", DeleteReferenceAsync);
        AddButton("Tải lại", () => RunAsync(LoadReferencesAsync));

        AddColumn("Mã", nameof(ReferenceRow.Id), 32);
        AddColumn("Tên danh mục", nameof(ReferenceRow.Name), 68);
        Grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSelectorSection(), 0, 0);
        layout.Controls.Add(CreateCatalogSection(), 0, 1);
        Body.Controls.Add(layout);

        Grid.CurrentCellChanged += (_, _) => UpdateSelection();
        Grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
                await EditSelectedReferenceAsync();
        };
        Shown += async (_, _) => await RunAsync(LoadReferencesAsync);
        UpdateSelection();
    }

    private ReferenceRow? SelectedRow => Grid.CurrentRow?.DataBoundItem as ReferenceRow;

    private Panel CreateSelectorSection()
    {
        Toolbar.Dock = DockStyle.Fill;
        Toolbar.FlowDirection = FlowDirection.RightToLeft;
        Toolbar.WrapContents = false;
        Toolbar.Margin = Padding.Empty;
        Toolbar.Padding = new Padding(0, 4, 0, 0);

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty,
            AccessibleName = "Loại danh mục"
        };
        for (int i = 0; i < ReferenceTypes.Length; i++)
        {
            var item = ReferenceTypes[i];
            var button = new RadioButton
            {
                Text = item.Name, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleCenter, UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                Font = new Font(Font, FontStyle.Bold), Checked = item == currentType
            };
            StyleSelector(button);
            button.CheckedChanged += (_, _) =>
            {
                StyleSelector(button);
                if (button.Checked)
                {
                    currentType = item;
                    BindReferences();
                }
            };
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            choices.Controls.Add(button, i, 0);
        }
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(14, 6, 14, 6), BackColor = AdminTheme.Surface
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new Label
        {
            Text = "Thể loại, tác giả, NXB", Dock = DockStyle.Fill, Margin = Padding.Empty,
            ForeColor = AdminTheme.Forest, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        }, 0, 0);
        content.Controls.Add(choices, 0, 1);
        content.Controls.Add(Toolbar, 0, 2);
        return Frame(content, new Padding(0, 0, 0, 12));
    }

    private static void StyleSelector(RadioButton button)
    {
        button.BackColor = button.Checked ? AdminTheme.Forest : AdminTheme.Surface;
        button.ForeColor = button.Checked ? Color.White : AdminTheme.Forest;
        button.FlatAppearance.BorderColor = button.Checked ? AdminTheme.Forest : AdminTheme.Border;
        button.FlatAppearance.CheckedBackColor = AdminTheme.Forest;
        button.FlatAppearance.MouseOverBackColor = button.Checked ? AdminTheme.Primary : AdminTheme.Tint;
        button.FlatAppearance.MouseDownBackColor = button.Checked ? AdminTheme.Forest : AdminTheme.Tint;
    }

    private TableLayoutPanel CreateCatalogSection()
    {
        selectedName.Font = new Font(Font, FontStyle.Bold);
        var details = new Panel
        {
            Dock = DockStyle.Fill, BackColor = AdminTheme.Surface,
            Padding = new Padding(14, 8, 14, 12), AutoScroll = true
        };
        details.Controls.Add(description);
        details.Controls.Add(descriptionHeading);
        details.Controls.Add(parentName);
        details.Controls.Add(parentHeading);
        details.Controls.Add(selectedName);

        var catalog = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        catalog.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        catalog.Controls.Add(CreateSection("Danh mục", Grid, new Padding(0, 0, 6, 0)), 0, 0);
        catalog.Controls.Add(CreateSection("Chi tiết danh mục", details, new Padding(6, 0, 0, 0)), 1, 0);
        return catalog;
    }

    private Panel CreateSection(string title, Control content, Padding margin)
    {
        var section = new Panel { Dock = DockStyle.Fill, BackColor = AdminTheme.Surface };
        section.Controls.Add(content);
        section.Controls.Add(new Label
        {
            Text = title, Dock = DockStyle.Top, Height = 38, Padding = new Padding(14, 0, 0, 0),
            ForeColor = AdminTheme.Forest, BackColor = AdminTheme.Tint,
            Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
        });
        return Frame(section, margin);
    }

    private static Panel Frame(Control content, Padding margin)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill, BackColor = AdminTheme.Border,
            Padding = new Padding(1), Margin = margin
        };
        frame.Controls.Add(content);
        return frame;
    }

    private static Label CreateDetailHeading(string title) => new()
    {
        Text = title, Dock = DockStyle.Top, Height = 24,
        ForeColor = AdminTheme.Muted, TextAlign = ContentAlignment.MiddleLeft
    };

    private void UpdateSelection()
    {
        var row = SelectedRow;
        edit.Enabled = delete.Enabled = row is not null;
        selectedName.Text = row?.Name ?? "Chọn một mục trong danh sách";
        parentHeading.Visible = parentName.Visible = row is not null && currentType.Kind == ReferenceKind.Category;
        parentName.Text = row?.ParentName ?? "—";
        descriptionHeading.Visible = description.Visible = row is not null;
        descriptionHeading.Text = currentType.DescriptionLabel;
        description.AccessibleName = descriptionHeading.Text;
        description.Text = row?.Item.Description ?? "";
    }

    private async Task LoadReferencesAsync(CancellationToken token)
    {
        references = await api.GetCatalogReferencesAsync(token);
        if (!IsDisposed)
            BindReferences();
    }

    private void BindReferences()
    {
        var items = currentType.Kind switch
        {
            ReferenceKind.Category => references.Categories,
            ReferenceKind.Author => references.Authors,
            _ => references.Publishers
        };
        var names = items.ToDictionary(item => item.Id, item => item.Name);
        Grid.DataSource = items.Select(item => new ReferenceRow(item,
            item.ParentId is int parent ? names.GetValueOrDefault(parent) : null)).ToList();
        SetStatus(items.Count == 0 ? "Chưa có bản ghi." : $"{items.Count:N0} bản ghi");
        UpdateSelection();
    }

    private Task EditSelectedReferenceAsync() => SelectedRow is { } row ? EditReferenceAsync(row.Item) : Task.CompletedTask;

    private Task EditReferenceAsync(CatalogReference? item) => RunAsync(async token =>
    {
        var selectedType = currentType;
        var name = new TextBox { Text = item?.Name ?? "", MaxLength = 120 };
        var description = new TextBox
        {
            Text = item?.Description ?? "",
            MaxLength = 1500,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 100
        };
        var parent = selectedType.Kind == ReferenceKind.Category
            ? CatalogEditorDialog.ReferencePicker(references.Categories.Where(category => category.Id != item?.Id),
                item?.ParentId, "Không có")
            : null;

        string action = item is null ? "Thêm" : "Sửa";
        using var editor = new CatalogEditorDialog($"{action} {selectedType.Name.ToLowerInvariant()}", 235, async () =>
        {
            await api.SaveCatalogReferenceAsync(selectedType.Kind, item?.Id,
                new CatalogReferenceDraft(name.Text, description.Text, parent is null ? null : CatalogEditorDialog.ReferenceId(parent)), token);
            return null;
        }, token);
        editor.AddField("Tên", name);
        editor.AddField(selectedType.DescriptionLabel, description);
        if (parent is not null)
            editor.AddField("Thể loại cha", parent);

        if (editor.ShowDialog(this) == DialogResult.OK)
            await LoadReferencesAsync(token);
    });

    private Task DeleteReferenceAsync() => RunAsync(async token =>
    {
        if (SelectedRow is not { } row || !Confirm($"Xóa “{row.Name}” khỏi danh mục?"))
            return;

        await api.DeleteCatalogReferenceAsync(currentType.Kind, row.Id, token);
        await LoadReferencesAsync(token);
    });

    private sealed record ReferenceType(ReferenceKind Kind, string Name, string DescriptionLabel);

    private sealed record ReferenceRow(CatalogReference Item, string? ParentName)
    {
        public int Id => Item.Id;
        public string Name => Item.Name;
    }
}
