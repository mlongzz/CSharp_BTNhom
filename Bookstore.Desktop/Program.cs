
using System.Windows.Forms;
using Bookstore.Desktop.Forms;

namespace Bookstore.Desktop;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        FrmLogin login;
        try
        {
            login = new FrmLogin();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Cấu hình Bookstore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (login)
            System.Windows.Forms.Application.Run(login);
    }
}
