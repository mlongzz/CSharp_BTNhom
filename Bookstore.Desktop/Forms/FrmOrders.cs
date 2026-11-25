using Bookstore.Desktop.Services;

namespace Bookstore.Desktop.Forms;

internal sealed class FrmOrders(BookstoreApiClient api)
    : OrderManagementPage(api, "Quản lý đơn hàng", shipping: false)
{
}
