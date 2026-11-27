
using System.Drawing;
using System.Windows.Forms;
using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmLogin : Form
{
    private readonly BookstoreApiClient api;
    private readonly CancellationTokenSource lifetime = new();

    private readonly TextBox txtUsername = new();
    private readonly TextBox txtPassword = new();
    private readonly Button btnLogin = new();

    public FrmLogin()
    {
        var apiUrl = Environment.GetEnvironmentVariable("Bookstore__DesktopApiUrl");
        api = new BookstoreApiClient(string.IsNullOrWhiteSpace(apiUrl) ? "http://localhost:5187/" : apiUrl.Trim());

        Text = "Đăng nhập - Bookstore Admin";
        ClientSize = new Size(420, 338);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = AdminTheme.Surface;
        ForeColor = AdminTheme.Text;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 92,
            Text = "Bookstore Admin",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            BackColor = AdminTheme.Forest,
            ForeColor = AdminTheme.Surface,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblUsername = new Label
        {
            Text = "Tài khoản",
            Bounds = new Rectangle(50, 120, 320, 25)
        };

        txtUsername.Bounds = new Rectangle(50, 145, 320, 32);
        txtUsername.AccessibleName = lblUsername.Text;
        txtUsername.MaxLength = 254;

        var lblPassword = new Label
        {
            Text = "Mật khẩu",
            Bounds = new Rectangle(50, 184, 320, 25)
        };

        txtPassword.Bounds = new Rectangle(50, 209, 320, 32);
        txtPassword.AccessibleName = lblPassword.Text;
        txtPassword.UseSystemPasswordChar = true;
        txtPassword.MaxLength = 128;

        btnLogin.Text = "Đăng nhập";
        btnLogin.Bounds = new Rectangle(50, 266, 320, 42);
        AdminTheme.StyleButton(btnLogin, primary: true);

        Controls.AddRange(new Control[]
        {
            title,
            lblUsername,
            txtUsername,
            lblPassword,
            txtPassword,
            btnLogin
        });

        AcceptButton = btnLogin;

        btnLogin.Click += async (_, _) =>
            await HandleLoginAsync();
    }

    private async Task HandleLoginAsync()
    {
        string username = txtUsername.Text.Trim();
        string password = txtPassword.Text;

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(this,
                "Vui lòng nhập tài khoản và mật khẩu.");
            return;
        }

        btnLogin.Enabled = false;
        var cancellationToken = lifetime.Token;

        try
        {
            bool success = await api.LoginAsync(
                username, password, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!success)
            {
                MessageBox.Show(this,
                    "Đăng nhập không thành công hoặc không có quyền Admin.");
                return;
            }

            Hide();

            try
            {
                using var dashboard = new FrmDashboard(api);
                dashboard.ShowDialog();
            }
            finally
            {
                try
                {
                    await api.LogoutAsync();
                }
                catch (HttpRequestException)
                {
                }
                catch (OperationCanceledException)
                {
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    txtPassword.Clear();
                    Show();
                    txtPassword.Focus();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                MessageBox.Show(this, "Không thể giao tiếp với backend.\n" + ex.Message);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "Kết nối quá thời gian cho phép.");
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                MessageBox.Show(this, "Đã xảy ra lỗi.\n" + ex.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                btnLogin.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lifetime.Cancel();
            lifetime.Dispose();
            api.Dispose();
        }
        base.Dispose(disposing);
    }
}
