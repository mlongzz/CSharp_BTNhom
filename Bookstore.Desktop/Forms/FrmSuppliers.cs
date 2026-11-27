using Bookstore.Desktop.Services;
using Bookstore.Domain;
using static Bookstore.Desktop.Forms.WarehouseForm;

namespace Bookstore.Desktop.Forms;

public sealed class FrmSuppliers : Form
{
    private readonly BookstoreApiClient api;
    private readonly TextBox search = new() { Width = 210, PlaceholderText = "Tìm nhà cung cấp", AccessibleName = "Tìm nhà cung cấp" };
    private readonly DataGridView grid = Grid(
        Column("Mã", nameof(Supplier.Id), 40),
        Column("Nhà cung cấp", nameof(Supplier.Name), 135),
        Column("Điện thoại", nameof(Supplier.Phone), 85),
        Column("Email", nameof(Supplier.Email), 135),
        Column("Địa chỉ", nameof(Supplier.Address), 135));
    private readonly Button edit = Button("Sửa");
    private readonly Button delete = Button("Xóa");
    private readonly TableLayoutPanel page;
    private List<Supplier> suppliers = [];

    private Supplier? SelectedSupplier => grid.CurrentRow?.DataBoundItem as Supplier;

    public FrmSuppliers(BookstoreApiClient api)
    {
        this.api = api;
        Text = "Nhà cung cấp";
        Font = new Font("Segoe UI", 10);

        var add = Button("Thêm nhà cung cấp", primary: true);
        var reload = Button("Tải lại");
        page = Page(Toolbar(search, add, edit, delete, reload), grid);
        Controls.Add(page);

        add.Click += async (_, _) => await EditAsync(null);
        edit.Click += async (_, _) => await EditAsync(SelectedSupplier);
        delete.Click += async (_, _) => await DeleteAsync();
        reload.Click += async (_, _) => await RunAsync(page, LoadAsync);
        Shown += async (_, _) => await RunAsync(page, LoadAsync);
        search.TextChanged += (_, _) => ApplyFilter();
        grid.SelectionChanged += (_, _) => UpdateActions();
        grid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0 && SelectedSupplier is { } supplier)
                await EditAsync(supplier);
        };
        UpdateActions();
    }

    private async Task LoadAsync()
    {
        suppliers = await api.GetSuppliersAsync();
        if (!IsDisposed)
            ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = search.Text.Trim();
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        grid.DataSource = suppliers.Where(supplier => supplier.Name.Contains(keyword, comparison)
            || supplier.Phone.Contains(keyword, comparison) || supplier.Email.Contains(keyword, comparison)
            || supplier.Address.Contains(keyword, comparison)).ToList();
        UpdateActions();
    }

    private void UpdateActions() => edit.Enabled = delete.Enabled = SelectedSupplier is not null;

    private async Task EditAsync(Supplier? supplier)
    {
        var name = new TextBox { MaxLength = 120, Text = supplier?.Name ?? "" };
        var phone = new TextBox { MaxLength = 10, Text = supplier?.Phone ?? "" };
        var email = new TextBox { MaxLength = 254, Text = supplier?.Email ?? "" };
        var address = new TextBox { MaxLength = 255, Text = supplier?.Address ?? "" };
        var fields = Fields();
        AddField(fields, "Tên nhà cung cấp", name);
        AddField(fields, "Điện thoại", phone);
        AddField(fields, "Email", email);
        AddField(fields, "Địa chỉ", address);

        var title = supplier is null ? "Thêm nhà cung cấp" : "Sửa nhà cung cấp";
        if (ShowEditor(this, title, fields, () =>
            api.SaveSupplierAsync(new Supplier(supplier?.Id ?? 0, name.Text, phone.Text, email.Text, address.Text))))
            await RunAsync(page, LoadAsync);
    }

    private async Task DeleteAsync()
    {
        if (SelectedSupplier is not { } supplier || MessageBox.Show(this, $"Xóa nhà cung cấp «{supplier.Name}»?",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        await RunAsync(page, async () =>
        {
            await api.DeleteSupplierAsync(supplier.Id);
            await LoadAsync();
        });
    }
}
