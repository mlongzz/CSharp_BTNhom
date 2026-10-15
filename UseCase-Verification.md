# Đối chiếu UC01–UC21

Đối chiếu theo workbook UseCase và mã màn hình trong ScreenDesign. Các kiểm thử HTTP dùng ứng dụng chạy cùng SQLite tạm; các kiểm thử C# chạy qua `dotnet test`. Bảng phân biệt những bước đã kiểm chứng với các điểm tích hợp mô phỏng.

| UC | Màn hình và luồng đã triển khai | Bằng chứng hoặc giới hạn |
|---|---|---|
| UC01 Tìm kiếm và lọc | Danh mục công khai. Tìm tên, tác giả, ISBN; lọc thể loại, giá, tác giả, NXB và đánh giá; sắp xếp, phân trang. | HTTP kiểm tra tìm kiếm Unicode, ISBN, bộ lọc và phân trang. |
| UC02 Chi tiết sách | Bìa, tác giả, NXB, giá, tồn, mô tả, ISBN, số trang, năm, đánh giá và sách liên quan; thêm giỏ hoặc mua ngay. | HTTP kiểm tra sách hiển thị, tồn và mã sách không tồn tại. |
| UC03 Đăng ký | Email duy nhất, mật khẩu có chữ và số, bcrypt, xác nhận qua hộp thư. | HTTP kiểm tra email trùng, mật khẩu không đạt, xác nhận. SMTP tùy chọn kiểm thử với máy chủ giả lập cục bộ. |
| UC04 Đăng nhập/đăng xuất | JWT, quyền theo vai trò, thu hồi phiên khi thoát/khóa, 5 lần sai khóa tạm 5 phút. | HTTP kiểm tra tạo và thu hồi phiên, số lần sai và hết thời gian khóa. |
| UC05 Giỏ hàng | Thêm, sửa, xóa có xác nhận, tạm tính và giới hạn số lượng còn trong kho. | HTTP kiểm tra thêm giỏ và số lượng qua checkout. |
| UC06 Đặt hàng | Chọn địa chỉ, voucher, COD/trực tuyến; xem tiền hàng, giảm giá, phí và tổng; kết quả có mã đơn, thư xác nhận. | HTTP kiểm tra đơn mới, tổng có voucher và hết lượt, COD chưa thu tiền. Phí giao hàng demo 15.000 ₫. |
| UC07 Thanh toán trực tuyến | Cổng thử nghiệm, kết quả thành công mới tạo đơn; lỗi/hết hạn giữ giỏ; chặn xử lý lặp. | HTTP và C# kiểm tra ba kết quả, mã phiên đã thất bại không thể tạo đơn. Cổng và khoản hoàn tiền là mô phỏng, chưa nối VNPay. |
| UC08 Theo dõi/hủy | Danh sách riêng của khách, timeline, mã vận đơn; chỉ hủy khi chờ hoặc đã xác nhận. | C# kiểm tra chủ sở hữu, trạng thái được phép và hoàn tồn đúng một lần. HTTP kiểm tra hiển thị vận đơn. |
| UC09 Đánh giá | Chủ đơn hoàn tất có thể chấm 1–5 sao và viết nhận xét; một đánh giá mỗi sách/đơn. | HTTP và C# kiểm tra chưa hoàn tất, không phải chủ đơn và trùng đánh giá. |
| UC10 Hồ sơ/địa chỉ | Sửa hồ sơ, thêm/sửa/xóa địa chỉ, chọn mặc định, chọn địa chỉ khi đặt đơn. | HTTP kiểm tra nhiều địa chỉ, đổi mặc định, xóa và không đọc được địa chỉ của khách khác. |
| UC11 Yêu cầu đổi trả | Chọn sách từ đơn hoàn tất, ghi lý do và chọn hoàn tiền/đổi sách trong 7 ngày. | HTTP kiểm tra yêu cầu chờ duyệt; quy tắc 7 ngày kiểm tra theo thời điểm giao thành công. |
| UC12 Xử lý đơn | Lọc trạng thái, ngày, mã và khách; xác nhận hoặc từ chối có lý do; đóng gói/giao/hoàn tất. | HTTP kiểm tra chuỗi trạng thái và từ chối; C# kiểm tra đồng thời, rollback thiếu tồn. |
| UC13 Vận chuyển | Bán hàng nhập nhà vận chuyển và mã vận đơn, ghi đang giao và giao thành công; COD thu khi hoàn tất. | HTTP kiểm tra vận đơn hiển thị; C# kiểm tra COD chưa thu trước khi hoàn tất. Vận đơn và trạng thái nhập thủ công, chưa nối API vận chuyển. |
| UC14 Xử lý đổi trả | Bán hàng duyệt/từ chối; kho kiểm tra, xác nhận, hoàn tồn và ghi số tiền hoàn hoặc xuất sách đổi. | HTTP và C# kiểm tra phân vai, hoàn tồn đúng một lần, tính tiền hoàn sau giảm giá. Số tiền chỉ ghi nhận trong ứng dụng. |
| UC15 Nhập kho/nhà cung cấp | Quản lý nhà cung cấp; kho lập phiếu có giá, quản trị duyệt, kho nhập thực nhận và ghi chú chênh lệch. | HTTP/C# kiểm tra sai vai trò, chưa duyệt, chênh lệch thiếu ghi chú, nhập lặp và di trú phiếu cũ. |
| UC16 Kiểm kê | Tồn hiện tại, đối chiếu, cảnh báo và lịch sử nhập/bán/hủy/trả/điều chỉnh. | HTTP kiểm tra ngưỡng và nhật ký điều chỉnh. Ngưỡng demo mặc định 3, chỉnh được. |
| UC17 Quản lý sách | Thêm/sửa/ẩn/xóa, tìm sách, giá dương, ISBN duy nhất. | HTTP/C# kiểm tra trùng ISBN, ẩn khỏi danh mục và chặn xóa sách đã tham chiếu. |
| UC18 Thể loại/tác giả/NXB | Quản trị danh mục, thể loại cha và dữ liệu tác giả/NXB. | HTTP kiểm tra CRUD và chặn xóa dữ liệu đang được tham chiếu. Mỗi sách chọn một tác giả ở giao diện hiện tại. |
| UC19 Voucher | Giảm số tiền hoặc phần trăm, điều kiện giá trị đơn, hiệu lực và giới hạn sử dụng. | HTTP/C# kiểm tra trùng mã, cấu hình và áp dụng voucher; HTTP kiểm tra hết lượt ở checkout. |
| UC20 Tài khoản/phân quyền | Customer, Sales, Warehouse, Admin; quản trị trạng thái và vai trò, thu hồi phiên khi thay đổi. | HTTP/C# kiểm tra quyền truy cập, khóa tài khoản và thu hồi phiên đang hoạt động. |
| UC21 Báo cáo | Doanh thu đơn hoàn tất, trạng thái đơn, sách bán chạy, nhập và tồn kho; lọc ngày, biểu đồ, xuất Excel. | HTTP kiểm tra loại đơn chờ khỏi doanh thu và khoảng rỗng. Tệp Excel mở được, gồm 5 sheet với số liệu đối chiếu. |

Nguồn có hai phương án thanh toán online. Bản demo giữ COD và một cổng mô phỏng. Luồng giỏ dùng điều kiện đăng nhập theo UC05. Hủy đơn chỉ hoàn số đã trừ theo BR05–BR06.

Các mục 500 người dùng đồng thời, tải dưới 3 giây dưới tải, sao lưu hằng ngày và vận hành 24/7 là yêu cầu triển khai. Bản local kiểm thử chức năng và toàn vẹn dữ liệu; chưa có kết quả kiểm thử tải hoặc vận hành dài hạn.
