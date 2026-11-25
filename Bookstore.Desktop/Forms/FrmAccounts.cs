using Bookstore.Desktop.Services;
using Bookstore.Domain;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmAccounts : AdminPage
{
    private readonly BookstoreApiClient api;
    private readonly TextBox search = new() { Width = 260, PlaceholderText = "Tài khoản, tên hoặc email", AccessibleName = "Tìm tài khoản" };
    private readonly ComboBox role = new() { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Vai trò" };
    private readonly CheckBox locked = new() { Text = "Khóa tài khoản", AutoSize = true, Margin = new Padding(14, 7, 8, 0) };
    private readonly Button save = ActionButton("Lưu quyền");
    private readonly Label selection = FieldLabel("");
    private readonly DataGridView grid = CreateGrid("Tài khoản", "Họ tên", "Email", "Vai trò", "Trạng thái", "Đăng nhập gần nhất");
    private List<Account> accounts = [];

    public FrmAccounts(BookstoreApiClient api) : base("Tài khoản và phân quyền")
    {
        this.api = api;
        role.DisplayMember = nameof(RoleOption.Label);
        role.ValueMember = nameof(RoleOption.Value);
        role.DataSource = AccountRoles.All.Select(value => new RoleOption(value, RoleName(value))).ToList();

        var refresh = ActionButton("Tải lại");
        var toolbar = CreateToolbar();
        toolbar.Controls.AddRange([FieldLabel("Tìm tài khoản"), search, refresh]);

        var editor = CreateToolbar();
        editor.Dock = DockStyle.Bottom;
        editor.Padding = new Padding(0, 8, 0, 3);
        editor.Controls.AddRange([FieldLabel("Vai trò"), role, locked, save, selection]);

        Body.Controls.Add(grid);
        Body.Controls.Add(editor);
        Body.Controls.Add(toolbar);

        search.TextChanged += (_, _) => ShowAccounts();
        grid.SelectionChanged += (_, _) => ShowSelection();
        role.SelectedIndexChanged += (_, _) => UpdateSaveButton();
        locked.CheckedChanged += (_, _) => UpdateSaveButton();
        refresh.Click += async (_, _) => await LoadAccountsAsync();
        save.Click += async (_, _) => await SaveAccessAsync();
        Shown += async (_, _) => await LoadAccountsAsync();

        ShowSelection();
    }

    private Account? CurrentAccount => grid.CurrentRow?.Tag as Account;

    private Task LoadAccountsAsync() => RunAsync(async cancellationToken =>
    {
        var username = CurrentAccount?.Username;
        accounts = await api.GetAccountsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ShowAccounts(username);
    });

    private void ShowAccounts(string? selectedUsername = null)
    {
        var query = search.Text.Trim();
        grid.Rows.Clear();
        foreach (var account in accounts.Where(account => Contains(account.Username, query) || Contains(account.Name, query) ||
                     Contains(account.Email, query)))
        {
            var lastLogin = account.LastLogin?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm") ?? "—";
            var row = grid.Rows[grid.Rows.Add(account.Username, account.Name, account.Email ?? "", RoleName(account.Role),
                account.IsLocked ? "Đã khóa" : "Hoạt động", lastLogin)];
            row.Tag = account;
            if (account.Username == selectedUsername)
                grid.CurrentCell = row.Cells[0];
        }
        ShowSelection();
        SetStatus($"{grid.Rows.Count} tài khoản");
    }

    private void ShowSelection()
    {
        var account = CurrentAccount;
        var isCurrentUser = account is not null && string.Equals(account.Username, api.Username, StringComparison.OrdinalIgnoreCase);
        role.SelectedIndex = -1;
        if (account is not null)
            role.SelectedValue = account.Role;
        locked.Checked = account?.IsLocked == true;
        var canEdit = account is not null && !isCurrentUser;
        role.Enabled = canEdit;
        locked.Enabled = canEdit;
        selection.Text = isCurrentUser ? "Tài khoản đang đăng nhập" : "";
        UpdateSaveButton();
    }

    private void UpdateSaveButton() => save.Enabled = role.Enabled && CurrentAccount is { } account &&
        role.SelectedValue is string value && (account.Role != value || account.IsLocked != locked.Checked);

    private Task SaveAccessAsync()
    {
        if (!save.Enabled || CurrentAccount is not { } account || role.SelectedValue is not string newRole)
            return Task.CompletedTask;

        var newLocked = locked.Checked;
        var accessState = newLocked ? "Đã khóa" : "Hoạt động";
        if (MessageBox.Show(this, $"Lưu thay đổi cho {account.Email ?? account.Username}?\n{RoleName(newRole)} · {accessState}",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return Task.CompletedTask;

        return RunAsync(async cancellationToken =>
        {
            await api.UpdateAccountAccessAsync(account.Username, newRole, newLocked, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var index = accounts.FindIndex(item => item.Username == account.Username);
            accounts[index] = account with { Role = newRole, IsLocked = newLocked };
            ShowAccounts(account.Username);
            SetStatus("Đã lưu trạng thái và vai trò.");
        });
    }

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string RoleName(string value) => value switch
    {
        AccountRoles.Admin => "Quản trị viên",
        AccountRoles.Customer => "Khách hàng",
        _ => value
    };

    private sealed record RoleOption(string Value, string Label);
}
