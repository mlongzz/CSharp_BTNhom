# Hệ thống quản lý bán sách trực tuyến

Hệ thống bán sách trên .NET 9 gồm ứng dụng web ASP.NET Core MVC và ứng dụng quản trị WinForms. Hai ứng dụng dùng chung dữ liệu và nghiệp vụ trên backend. [Bảng đối chiếu UC01–UC21](../UseCase-Verification.md).

## Chạy ứng dụng

Mở PowerShell tại thư mục chứa `Bookstore.sln`:

```powershell
dotnet run --project .\Bookstore\Bookstore.csproj --no-launch-profile --urls http://localhost:5187
```

Nếu bản trước đang chạy ở cổng này, nhấn `Ctrl+C` trong cửa sổ đó trước khi chạy lệnh. SQLite tự tạo hoặc nâng cấp `Bookstore/bookstore.db`. Dùng biến môi trường `Bookstore__Database` với đường dẫn tuyệt đối để chạy dữ liệu riêng.

Trên Windows, giữ backend chạy rồi mở một cửa sổ PowerShell khác tại thư mục solution để chạy WinForms.

```powershell
dotnet run --project .\Bookstore.Desktop\Bookstore.Desktop.csproj
```

WinForms mặc định kết nối `http://localhost:5187`. Nếu backend chạy ở địa chỉ khác, đặt `$env:Bookstore__DesktopApiUrl` thành URL của backend trước khi chạy WinForms. Đăng nhập WinForms bằng tài khoản quản trị viên.

Hệ thống có hai vai trò. Admin đăng nhập WinForms để quản trị, bán hàng, vận chuyển và kho. User mua hàng trên web, dùng vai trò `Customer` trong mã nguồn.

| Vai trò | Tài khoản mẫu | Mật khẩu |
|---|---|---|
| User (Khách hàng) | `khach` | `Khach123!` |
| Admin (Quản trị viên) | `admin` | `Admin123!` |
Các tài khoản mẫu dành cho chạy local.

Khi nâng cấp dữ liệu cũ, hệ thống khóa `kho` và `nhanvien` nếu đã có Admin khác đang hoạt động, thu hồi phiên của các tài khoản bị khóa và giữ lịch sử nghiệp vụ.

## Luồng demo

1. **Khách hàng** tìm sách, mở chi tiết, thêm giỏ, chọn địa chỉ rồi xem lại tổng tiền. Phí giao hàng demo là 15.000 ₫. Chọn COD hoặc cổng trực tuyến mô phỏng.
2. **Quản trị viên trên WinForms** vào Đơn hàng để xác nhận và đóng gói. Trong Vận chuyển, ghi vận đơn rồi bấm **Xác nhận đã giao** khi giao thành công. Có thể ghi nhận giao thất bại và nhận hàng về kho khi chưa có bên nào xác nhận thành công.
3. **Khách hàng** vào Lịch sử đơn hàng để xem vận đơn và bấm **Xác nhận đã nhận hàng**. Hai bên có thể xác nhận theo bất kỳ thứ tự nào. Khi có đủ hai xác nhận, đơn hoàn tất và COD được đánh dấu đã thu. Khách hàng có thể đánh giá sách từ đơn hoàn tất hoặc gửi yêu cầu đổi trả trong 7 ngày. **Quản trị viên** xử lý yêu cầu trong form Đổi trả.
4. **Quản trị viên** tạo nhà cung cấp, lập và duyệt phiếu nhập, rồi đối chiếu thực nhận. Khi số lượng khác phiếu, nhập ghi chú chênh lệch. Form Tồn kho và kiểm kê cho phép xem lịch sử kho, chỉnh ngưỡng và ghi nhận kiểm kê.
5. **Quản trị viên** quản lý sách, thể loại, tác giả, NXB và quyền tài khoản trên WinForms. Trong Báo cáo và thống kê, chọn khoảng ngày, bấm Xem báo cáo rồi chọn tab muốn xuất Excel. Tab Tổng quan xuất tất cả các phần.

## Cấu hình và kiểm thử

- Thư mặc định nằm trong hộp thư nội bộ. Bật SMTP bằng `Bookstore__Smtp__Host` và `Bookstore__Smtp__Sender`. Port mặc định 587, STARTTLS bật mặc định. Cấu hình tùy chọn `Bookstore__Smtp__Port`, `Bookstore__Smtp__EnableSsl`, `Bookstore__Smtp__Username`, `Bookstore__Smtp__Password`.
- Phiên dùng JWT qua cookie HttpOnly trên web và Bearer token trên WinForms. Có thể cấp khóa ổn định bằng `Bookstore__JwtSigningKey` với ít nhất 32 byte UTF-8. `Bookstore__DemoAccounts=false` tắt việc tạo tài khoản mẫu.
- Kiểm thử tự động tạo SQLite tạm riêng với dữ liệu đang sử dụng.

```powershell
dotnet test .\Bookstore.Tests\Bookstore.Tests.csproj
```
## Thêm Github Actions
Workflow Bookstore CI đã chạy thành công.
## Chạy bằng docker http://127.0.0.1:8080
