using System.Globalization;
using Bookstore.Desktop.Services;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmBooks : CatalogPage
{
    private readonly BookstoreApiClient api;
    private readonly TextBox search = new() { Width = 220, PlaceholderText = "Tên sách, ISBN hoặc tác giả", Margin = new Padding(3, 6, 3, 3) };
    private readonly Button edit;
    private readonly Button availability;
    private readonly Button delete;

    public FrmBooks(BookstoreApiClient apiClient) : base("Quản lý sách")
    {
        api = apiClient;

        Toolbar.Controls.Add(search);
        search.AccessibleName = "Tìm sách";
        AddButton("Tìm kiếm / Tải lại", () => RunAsync(LoadBooksAsync));
        AddButton("Thêm sách", () => EditBookAsync(null), primary: true);
        edit = AddButton("Sửa", EditSelectedBookAsync);
        availability = AddButton("Ẩn sách", SetAvailabilityAsync);
        delete = AddButton("Xóa", DeleteBookAsync);

        AddColumn("Mã", nameof(CatalogBook.Id), 36);
        AddColumn("Tên sách", nameof(CatalogBook.Title), 155);
        AddColumn("ISBN", nameof(CatalogBook.Isbn), 92);
        AddColumn("Thể loại", nameof(CatalogBook.Category), 84);
        AddColumn("Tác giả", nameof(CatalogBook.Author), 90);
        AddColumn("Nhà xuất bản", nameof(CatalogBook.Publisher), 100);
        AddColumn("Giá (₫)", nameof(CatalogBook.Price), 65, "N0");
        AddColumn("Tồn", nameof(CatalogBook.Stock), 44);
        Grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Đang bán",
            DataPropertyName = nameof(CatalogBook.Active),
            FillWeight = 60
        });

        Grid.CurrentCellChanged += (_, _) => UpdateSelection();
        Grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
                await EditSelectedBookAsync();
        };
        search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            await RunAsync(LoadBooksAsync);
        };
        Shown += async (_, _) => await RunAsync(LoadBooksAsync);
        UpdateSelection();
    }

    private CatalogBook? SelectedBook => Grid.CurrentRow?.DataBoundItem as CatalogBook;

    private void UpdateSelection()
    {
        var book = SelectedBook;
        edit.Enabled = availability.Enabled = delete.Enabled = book is not null;
        availability.Text = book is { Active: false } ? "Hiện sách" : "Ẩn sách";
    }

    private async Task LoadBooksAsync(CancellationToken token)
    {
        var books = await api.GetCatalogBooksAsync(search.Text, token);
        if (IsDisposed)
            return;

        Grid.DataSource = books;
        SetStatus(books.Count == 0 ? "Không có sách phù hợp." : $"{books.Count:N0} sách");
        UpdateSelection();
    }

    private Task EditSelectedBookAsync() => SelectedBook is { } book ? EditBookAsync(book.Id) : Task.CompletedTask;

    private Task EditBookAsync(int? bookId) => RunAsync(async token =>
    {
        var references = await api.GetCatalogReferencesAsync(token);
        var book = bookId is int id ? await api.GetCatalogBookAsync(id, token) : null;
        if (IsDisposed)
            return;

        var title = new TextBox { Text = book?.Title ?? "", MaxLength = 180 };
        var isbn = new TextBox { Text = book?.Isbn ?? "", MaxLength = 40 };
        var price = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, ThousandsSeparator = true, Value = book?.Price ?? 0 };
        var category = CatalogEditorDialog.ReferencePicker(references.Categories, book?.CategoryId, "Chọn thể loại");
        var author = CatalogEditorDialog.ReferencePicker(references.Authors, book?.AuthorId, "Chọn tác giả");
        var publisher = CatalogEditorDialog.ReferencePicker(references.Publishers, book?.PublisherId, "Chọn nhà xuất bản");
        var pages = new TextBox { Text = book?.Pages?.ToString(CultureInfo.CurrentCulture) ?? "", MaxLength = 10 };
        var year = new TextBox { Text = book?.Year?.ToString(CultureInfo.CurrentCulture) ?? "", MaxLength = 10 };
        var cover = new TextBox { Text = book?.CoverUrl ?? "", MaxLength = 500, PlaceholderText = "https://… hoặc /images/…" };
        var description = new TextBox
        {
            Text = book?.Description ?? "",
            MaxLength = 4000,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 82
        };
        var active = new CheckBox { Text = "Đang bán", Checked = book?.Active ?? true, AutoSize = true };

        using var editor = new CatalogEditorDialog(book is null ? "Thêm sách" : "Sửa sách", 482, async () =>
        {
            if (!TryReadOptionalInt(pages.Text, out var pageCount) || !TryReadOptionalInt(year.Text, out var publicationYear))
                return "Số trang và năm xuất bản phải là số nguyên hoặc để trống.";

            await api.SaveBookAsync(bookId, new CatalogBookDraft(title.Text, isbn.Text, (int)price.Value, active.Checked,
                CatalogEditorDialog.ReferenceId(category), CatalogEditorDialog.ReferenceId(author), CatalogEditorDialog.ReferenceId(publisher),
                description.Text, cover.Text, pageCount, publicationYear), token);
            return null;
        }, token);

        editor.AddField("Tên sách", title);
        editor.AddField("ISBN", isbn);
        editor.AddField("Giá bán (₫)", price);
        editor.AddField("Thể loại", category);
        editor.AddField("Tác giả", author);
        editor.AddField("Nhà xuất bản", publisher);
        editor.AddField("Số trang", pages);
        editor.AddField("Năm xuất bản", year);
        editor.AddField("Đường dẫn ảnh bìa", cover);
        editor.AddField("Mô tả", description);
        editor.AddField("Trạng thái", active);
        if (editor.ShowDialog(this) == DialogResult.OK)
            await LoadBooksAsync(token);
    });

    private Task SetAvailabilityAsync() => RunAsync(async token =>
    {
        if (SelectedBook is not { } book)
            return;

        await api.SetBookActiveAsync(book.Id, !book.Active, token);
        await LoadBooksAsync(token);
    });

    private Task DeleteBookAsync() => RunAsync(async token =>
    {
        if (SelectedBook is not { } book || !Confirm($"Xóa sách “{book.Title}” khỏi danh mục?"))
            return;

        await api.DeleteBookAsync(book.Id, token);
        await LoadBooksAsync(token);
    });

    private static bool TryReadOptionalInt(string value, out int? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!int.TryParse(value, out int result))
            return false;

        number = result;
        return true;
    }
}
