# Hệ thống quản lý bán sách trực tuyến

Ứng dụng ASP.NET Core MVC trên .NET 9, với các project Web, Application, Domain, Infrastructure và Tests. Xem [bảng đối chiếu UC01–UC21](../UseCase-Verification.md) để biết màn hình, bằng chứng kiểm thử và phạm vi mô phỏng.

## Chạy ứng dụng

Mở PowerShell tại thư mục chứa `Bookstore.sln`:

```powershell
dotnet run --project .\Bookstore\Bookstore.csproj --no-launch-profile --urls http://localhost:5187
```

Nếu bản trước đang chạy ở cổng này, nhấn `Ctrl+C` trong cửa sổ đó trước khi chạy lệnh. SQLite tự tạo hoặc nâng cấp `Bookstore/bookstore.db`. Dùng biến môi trường `Bookstore__Database` với đường dẫn tuyệt đối để chạy dữ liệu riêng.

| Vai trò | Tài khoản mẫu | Mật khẩu |
|---|---|---|
| Khách hàng | `khach` | `Khach123!` |
| Nhân viên bán hàng | `nhanvien` | `Nhanvien123!` |
| Nhân viên kho | `kho` | `Kho12345!` |
| Quản trị viên | `admin` | `Admin123!` |

Có thể đăng nhập bằng bí danh ở trên hoặc địa chỉ `<bí danh>@bookstore.test`. Tài khoản tự đăng ký là khách hàng. Các tài khoản mẫu dành cho chạy local.

## Luồng demo

1. **Khách hàng** tìm sách, mở chi tiết, thêm giỏ, chọn địa chỉ và voucher, rồi xem lại tổng tiền. Phí giao hàng demo là 15.000 ₫. Chọn COD hoặc cổng trực tuyến mô phỏng.
2. **Nhân viên bán hàng** xác nhận đơn để trừ tồn, đóng gói, ghi vận đơn và xác nhận giao thành công. COD được đánh dấu đã thu lúc giao thành công.
3. **Khách hàng** xem vận đơn, đánh giá sách từ đơn hoàn tất hoặc gửi yêu cầu đổi trả trong 7 ngày. **Bán hàng** duyệt, **kho** kiểm tra và ghi nhận hàng trả.
4. **Kho** tạo nhà cung cấp và lập phiếu nhập. **Quản trị viên** duyệt. **Kho** đối chiếu thực nhận và xác nhận để cộng tồn. Nếu số lượng khác phiếu phải ghi chú.
5. **Quản trị viên** quản lý sách, danh mục, voucher, tài khoản và báo cáo. Chọn khoảng ngày để xuất Excel.

## Cấu hình và kiểm thử

- Thư mặc định nằm trong hộp thư nội bộ. Bật SMTP bằng `Bookstore__Smtp__Host` và `Bookstore__Smtp__Sender`. Port mặc định 587, STARTTLS bật mặc định. Cấu hình tùy chọn `Bookstore__Smtp__Port`, `Bookstore__Smtp__EnableSsl`, `Bookstore__Smtp__Username`, `Bookstore__Smtp__Password`.
- Phiên dùng JWT trong cookie HttpOnly. Có thể cấp khóa ổn định bằng `Bookstore__JwtSigningKey` với ít nhất 32 byte UTF-8. `Bookstore__DemoAccounts=false` tắt việc tạo tài khoản mẫu.
- Kiểm thử tự động tạo SQLite tạm riêng với dữ liệu đang sử dụng.

```powershell
dotnet test .\Bookstore.Tests\Bookstore.Tests.csproj
```
