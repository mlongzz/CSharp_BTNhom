
using System.Drawing;
using System.Windows.Forms;
using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

public sealed class FrmLogin : Form
{
    private readonly BookstoreApiClient api =
        new("http://localhost:5187/");

    private readonly TextBox txtUsername = new();
    private readonly TextBox txtPassword = new();
    private readonly Button btnLogin = new();

    public FrmLogin()
    {
        Text = "Đăng nhập - Bookstore Admin";
        ClientSize = new Size(420, 330);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var title = new Label
        {
            Text = "BOOKSTORE ADMIN",
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(25, 25, 370, 50)
        };

        var lblUsername = new Label
        {
            Text = "Tài khoản",
            Bounds = new Rectangle(50, 90, 320, 25)
        };

        txtUsername.Bounds =
            new Rectangle(50, 115, 320, 32);

        var lblPassword = new Label
        {
            Text = "Mật khẩu",
            Bounds = new Rectangle(50, 160, 320, 25)
        };

        txtPassword.Bounds =
            new Rectangle(50, 185, 320, 32);

        txtPassword.UseSystemPasswordChar = true;

        btnLogin.Text = "ĐĂNG NHẬP";
        btnLogin.Bounds =
            new Rectangle(50, 245, 320, 42);

        btnLogin.BackColor = Color.FromArgb(45, 55, 72);
        btnLogin.ForeColor = Color.White;
        btnLogin.FlatStyle = FlatStyle.Flat;

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

        FormClosed += (_, _) => api.Dispose();
    }

    private async Task HandleLoginAsync()
    {
        string username = txtUsername.Text.Trim();
        string password = txtPassword.Text;

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "Vui lòng nhập tài khoản và mật khẩu.");
            return;
        }

        btnLogin.Enabled = false;

        try
        {
            bool success = await api.LoginAsync(
                username, password);

            if (!success)
            {
                MessageBox.Show(
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
                    // Token vẫn được xóa khỏi client.
                }

                txtPassword.Clear();
                Show();
            }
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                "Không thể giao tiếp với backend:\n" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show(
                "Kết nối quá thời gian cho phép.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Đã xảy ra lỗi:\n" + ex.Message);
        }
        finally
        {
            btnLogin.Enabled = true;
        }
    }
}
