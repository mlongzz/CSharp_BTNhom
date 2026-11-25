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
    private readonly ComboBox type = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(ReferenceType.Name),
        Width = 155,
        AccessibleName = "Loại danh mục",
        Margin = new Padding(3, 6, 8, 3)
    };
    private readonly Button edit;
    private readonly Button delete;
    private readonly DataGridViewTextBoxColumn parentColumn;
    private readonly DataGridViewTextBoxColumn descriptionColumn;
    private CatalogReferences references = new([], [], []);

    public FrmCategories(BookstoreApiClient apiClient) : base("Thể loại, tác giả và nhà xuất bản")
    {
        api = apiClient;

        type.Items.AddRange(ReferenceTypes);
        type.SelectedIndex = 0;
        Toolbar.Controls.Add(type);
        AddButton("Tải lại", () => RunAsync(LoadReferencesAsync));
        AddButton("Thêm", () => EditReferenceAsync(null));
        edit = AddButton("Sửa", EditSelectedReferenceAsync);
        delete = AddButton("Xóa", DeleteReferenceAsync);

        AddColumn("Mã", nameof(ReferenceRow.Id), 35);
        AddColumn("Tên", nameof(ReferenceRow.Name), 110);
        parentColumn = AddColumn("Thể loại cha", nameof(ReferenceRow.ParentName), 110);
        descriptionColumn = AddColumn("Mô tả", nameof(ReferenceRow.Description), 210);

        type.SelectedIndexChanged += (_, _) => BindReferences();
        Grid.CurrentCellChanged += (_, _) => UpdateSelection();
        Grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
                await EditSelectedReferenceAsync();
        };
        Shown += async (_, _) => await RunAsync(LoadReferencesAsync);
        UpdateSelection();
    }

    private ReferenceType CurrentType => type.SelectedItem as ReferenceType ?? ReferenceTypes[0];
    private ReferenceRow? SelectedRow => Grid.CurrentRow?.DataBoundItem as ReferenceRow;

    private void UpdateSelection() => edit.Enabled = delete.Enabled = SelectedRow is not null;

    private async Task LoadReferencesAsync(CancellationToken token)
    {
        references = await api.GetCatalogReferencesAsync(token);
        if (!IsDisposed)
            BindReferences();
    }

    private void BindReferences()
    {
        var selectedType = CurrentType;
        var items = selectedType.Kind switch
        {
            ReferenceKind.Category => references.Categories,
            ReferenceKind.Author => references.Authors,
            _ => references.Publishers
        };
        var names = items.ToDictionary(item => item.Id, item => item.Name);
        Grid.DataSource = items.Select(item => new ReferenceRow(item,
            item.ParentId is int parent ? names.GetValueOrDefault(parent) : null)).ToList();
        parentColumn.Visible = selectedType.Kind == ReferenceKind.Category;
        descriptionColumn.HeaderText = selectedType.DescriptionLabel;
        SetStatus(items.Count == 0 ? "Chưa có bản ghi." : $"{items.Count:N0} bản ghi");
        UpdateSelection();
    }

    private Task EditSelectedReferenceAsync() => SelectedRow is { } row ? EditReferenceAsync(row.Item) : Task.CompletedTask;

    private Task EditReferenceAsync(CatalogReference? item) => RunAsync(async token =>
    {
        var selectedType = CurrentType;
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

        await api.DeleteCatalogReferenceAsync(CurrentType.Kind, row.Id, token);
        await LoadReferencesAsync(token);
    });

    private sealed record ReferenceType(ReferenceKind Kind, string Name, string DescriptionLabel);

    private sealed record ReferenceRow(CatalogReference Item, string? ParentName)
    {
        public int Id => Item.Id;
        public string Name => Item.Name;
        public string? Description => Item.Description;
    }
}
