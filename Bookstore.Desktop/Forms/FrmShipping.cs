using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmShipping(BookstoreApiClient api)
    : OrderManagementPage(api, "Quản lý vận chuyển", shipping: true)
{
}
