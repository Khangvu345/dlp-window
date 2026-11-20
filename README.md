# Repo DLP

Endpoint DLP cho Windows. Agent chạy ở khay hệ thống và mở trang quản lý trên loopback. Trang theo dõi thư mục quét và nhãn tài liệu, chỉnh policy và xem nhật ký clipboard hoặc chụp màn hình.

## Chạy trên Windows

Cài **.NET 10 SDK x64** cho Windows. Mở PowerShell tại thư mục chứa `RepoDlp.slnx` rồi chạy:

```powershell
dotnet build RepoDlp.slnx
dotnet run --project src/Agent.Windows
```

Mở vùng biểu tượng ẩn cạnh đồng hồ Windows, tìm **Repo DLP**, nhấp đúp hoặc nhấp phải và chọn **Mở trang quản lý**. Trình duyệt mở địa chỉ `http://127.0.0.1:<cổng>/` do Windows cấp cho phiên này. Chọn **Thoát agent** để đóng cả tray và web.

### Thử cảnh báo và nhật ký trên máy cài agent

1. Nhấp phải biểu tượng **Repo DLP** cạnh đồng hồ và chọn **Thử popup cảnh báo**. Hộp thoại Windows hiển thị trên màn hình của phiên đang chạy agent. Bấm **OK** để đóng. Lần thử này không ghi sự kiện vi phạm.
2. Chọn **Xem nhật ký trên máy** để xem 200 sự kiện gần nhất. Cửa sổ đọc SQLite tại `%LOCALAPPDATA%\RepoDlp\index.sqlite3` và cập nhật khi có sự kiện mới.
3. Với bảo vệ đang bật và policy mẫu, gõ chuỗi giả `123456789012` trong Notepad rồi chọn chuỗi và nhấn Ctrl+C. Kiểm tra hộp thoại **Repo DLP · Đã chặn** và dòng Clipboard có ID **clipboard-fake-12-digits**. Đóng hộp thoại rồi lặp lại thao tác để kiểm tra lần cảnh báo tiếp theo.

## Cấu trúc mã nguồn

| Đường dẫn | Trách nhiệm |
|---|---|
| `src/Agent.Windows/` | Entry point STA, WinForms tray, mở trình duyệt, dừng agent và ngăn chạy hai bản trong cùng phiên Windows. |
| `src/Core/` | Kiểu dữ liệu dùng chung. Hợp đồng dữ liệu và bộ đánh giá policy. |
| `src/Infrastructure/` | Bộ đọc TXT, DOCX và PDF, theo dõi thư mục, lưu phân loại và sự kiện vào SQLite. |
| `src/Web/` | Vòng đời ASP.NET Core Minimal API, API quản lý và HTML/CSS/JavaScript trong `wwwroot/`. Tài nguyên web được nhúng vào assembly. |
| `samples/` | Policy mẫu dùng khi khởi tạo agent. |
| `tests/Web.Smoke/` | Kiểm tra tự động trên web host thật và các API quản lý. |

## Thử NV4–NV8

1. Từ biểu tượng khay hệ thống, mở trang quản lý và chọn thư mục chứa bản sao tài liệu thử. Bấm lưu cấu hình và đợi bảng tài liệu cập nhật.
2. Mở mục **Quy tắc** để sửa policy. Policy mẫu phân loại tài liệu chứa `Tài liệu nội bộ` và có một quy tắc clipboard cho chuỗi đúng 12 chữ số ASCII độc lập.
3. Từ menu agent, mở tài liệu TXT trong trình xem Repo DLP. Mở mục **Nhật ký** để kiểm tra các lần sao chép và phím chụp mà agent quan sát được. Dùng bộ lọc thời gian và hành vi để tìm sự kiện.

Cài đặt phiên người dùng, `rules.json` và chỉ mục SQLite nằm trong `%LOCALAPPDATA%\RepoDlp`. Nhật ký lưu metadata sự kiện. Phiên bản policy dùng khi phân loại xuất hiện trong API tài liệu.

Hướng dẫn kiểm tra desktop Windows nằm trong thư mục [docs](docs). Trình xem Repo DLP là ứng dụng demo cho việc nhận diện nguồn clipboard và thử cơ chế loại trừ cửa sổ khỏi ảnh chụp. Bảng kiểm Windows trong tài liệu NV7 phân biệt kết quả kỳ vọng và kết quả đã quan sát.

## Nghiệm thu nhiệm vụ 3

Chạy kiểm tra HTTP từ thư mục gốc:

```powershell
dotnet run --project tests/Web.Smoke
```

Kết quả mong đợi là dòng `PASS` và mã thoát 0. Kiểm tra tiếp trong phiên desktop Windows:

1. Mở agent. Kiểm tra biểu tượng xuất hiện ở tray.
2. Chọn **Mở trang quản lý**. Trang hiển thị **Đang chạy**, giờ khởi động và địa chỉ loopback. Bấm **Kiểm tra lại**, giờ khởi động giữ nguyên.
3. Chạy lại lệnh mở agent trong PowerShell thứ hai. Bản thứ hai thông báo agent đã chạy, tray giữ một biểu tượng.
4. Chọn **Thoát agent**. Biểu tượng biến mất. Trên tab còn mở, bấm **Kiểm tra lại** và xác nhận trạng thái **Mất kết nối**.
5. Khởi động lại agent và mở trang từ tray. Kiểm tra thời điểm khởi động mới.
