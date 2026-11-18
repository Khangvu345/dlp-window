# Repo DLP

Endpoint DLP cho Windows. Mốc hiện tại là **nhiệm vụ 3: dựng khung repo và agent**. Một tiến trình chạy ở khay hệ thống và phục vụ trang quản lý trên máy cá nhân. Trang hiển thị trạng thái kết nối và thời điểm khởi động của phiên agent.

## Chạy trên Windows

Cài **.NET 10 SDK x64** cho Windows. Giải nén ZIP, mở PowerShell tại thư mục chứa `RepoDlp.slnx` rồi chạy:

```powershell
dotnet build RepoDlp.slnx
dotnet run --project src/Agent.Windows
```

Mở vùng biểu tượng ẩn cạnh đồng hồ Windows, tìm **Repo DLP**, nhấp đúp hoặc nhấp phải và chọn **Mở trang quản lý**. Trình duyệt mở địa chỉ `http://127.0.0.1:<cổng>/` do Windows cấp cho phiên này. Chọn **Thoát agent** để đóng cả tray và web.

## Cấu trúc mã nguồn

| Đường dẫn | Trách nhiệm |
|---|---|
| `src/Agent.Windows/` | Entry point STA, WinForms tray, mở trình duyệt, dừng agent và ngăn chạy hai bản trong cùng phiên Windows. |
| `src/Core/` | Kiểu dữ liệu dùng chung. Hiện có hợp đồng trạng thái agent. Phần policy và phân loại sẽ đặt tại đây. |
| `src/Infrastructure/` | Project dành cho bộ đọc TXT/DOCX/PDF, theo dõi thư mục và SQLite ở các nhiệm vụ tiếp theo. |
| `src/Web/` | Vòng đời ASP.NET Core Minimal API, route chỉ đọc `GET /api/status`, HTML/CSS/JavaScript trong `wwwroot/`. Tài nguyên web được nhúng vào assembly. |
| `samples/` | Vị trí dành cho bộ tài liệu thử. |
| `tests/Web.Smoke/` | Bài kiểm tra tự động chạy một web host thực, gọi trang, CSS, JS và API. |

Cấu trúc này dùng một entry point để khởi động toàn bộ ứng dụng. Các mốc tiếp theo sẽ bổ sung quét file, phân loại theo policy, clipboard, screenshot và lưu sự kiện.

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
