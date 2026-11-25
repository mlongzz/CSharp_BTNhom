
using System.Windows.Forms;
using Bookstore.Desktop.Forms;

namespace Bookstore.Desktop;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FrmLogin());
    }
}
