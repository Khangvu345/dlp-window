# NV5 · Phân loại và lưu nhãn

Agent khởi tạo `rules.json` từ `samples/rules.json` trong thư mục dữ liệu khi chạy lần đầu. Bản mẫu dùng cụm **Tài liệu nội bộ** để gắn nhãn **Nội bộ**. Tài liệu đọc được mà không khớp quy tắc sẽ có nhãn **Thông thường**. Mỗi quy tắc có ID duy nhất và một phạm vi. Quy tắc có `priority` lớn hơn được xét trước. Các quy tắc cùng mức được xét theo thứ tự trong JSON. Quyết định dùng quy tắc khớp đầu tiên.

- **Phân loại** chấp nhận từ khóa hoặc regex và ghi `label` của quy tắc khớp. Từ khóa so sánh không phân biệt hoa thường. Regex tôn trọng cờ trong biểu thức và có giới hạn 100 ms cho mỗi lần so khớp. Một lần so khớp quá hạn được bỏ qua.
- **Clipboard** chấp nhận regex trên văn bản hoặc so khớp nhãn nguồn với `pattern`. Quy tắc `clipboard-fake-12-digits` có ưu tiên 200. Quy tắc `clipboard-internal` có ưu tiên 100. Regex được xét cả khi không xác định được nhãn nguồn.
- **Chụp màn hình** so khớp nhãn nguồn với `pattern`. Quy tắc mẫu `screenshot-internal` yêu cầu chặn với nhãn **Nội bộ**. Tích hợp Windows ghi kết quả theo khả năng thực thi và quan sát được.

Phiên bản policy là SHA-256 của biểu diễn JSON do agent lưu. Sau khi cập nhật policy hợp lệ, agent quét lại thư mục để phân loại các file theo phiên bản mới. Policy không hợp lệ được trả về kèm vị trí và ID của quy tắc lỗi. Có thể sửa `rules.json` trên đĩa trước khi khởi động hoặc dùng trang quản lý để áp dụng thay đổi trong phiên chạy.

SQLite lưu đường dẫn, SHA-256 của dữ liệu đã đọc, phiên bản policy, trạng thái, nhãn và ID quy tắc khớp. File cần OCR và file lỗi có trạng thái riêng và chưa có nhãn. Cơ sở dữ liệu cũng giữ nhật ký dạng metadata gồm thời gian UTC, ứng dụng, hành vi, kết quả, ID quy tắc và trạng thái xử lý. Bộ lọc thời gian xác định khoảng từ mốc bắt đầu bao gồm đến mốc kết thúc loại trừ.

## Kiểm tra

```powershell
dotnet run --project tests/Classification.Smoke
```

Bài kiểm tra dùng file tạm và mẫu PDF của NV4 để kiểm tra nhãn cùng từ khóa trên TXT, DOCX và PDF, trạng thái cần OCR, thứ tự ưu tiên, SHA-256, thay đổi nội dung, policy hợp lệ và không hợp lệ, đổi tên, xóa, khởi động lại, lọc nhật ký và đổi thư mục theo dõi.
