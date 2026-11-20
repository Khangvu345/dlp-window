# Nghiệm thu clipboard trên Windows

Chạy agent trong phiên desktop Windows sau khi tích hợp trình xem văn bản của NV7. Lưu hai tệp UTF-8 trong thư mục quét và đợi web hiển thị nhãn.

| Tệp | Nội dung theo từng dòng | Nhãn mong đợi |
|---|---|---|
| `noi-bo.txt` | Tài liệu nội bộ<br>Hẹn gặp vào ngày mai. | Nội bộ |
| `thuong.txt` | Hẹn gặp vào ngày mai.<br>123456789012 | Thông thường |

1. Bật bảo vệ. Mở hai tệp trong trình xem Repo DLP. Chọn đúng câu `Hẹn gặp vào ngày mai.` ở mỗi tệp rồi sao chép. Kiểm tra nhật ký clipboard trên web và dán vào Notepad sau khi sự kiện xuất hiện. Tệp Nội bộ cho kết quả chặn theo `clipboard-internal` và dán ra thông điệp cảnh báo. Tệp Thông thường cho kết quả cho qua và dán được câu.
2. Sao chép riêng dòng `123456789012` từ trình xem của tệp Thông thường. Kiểm tra một sự kiện chặn với ID `clipboard-fake-12-digits`, sau đó dán vào Notepad hai lần. Cả hai lần phải hiện thông điệp cảnh báo. Sao chép lại chuỗi số rồi lặp lại phép thử. Hai lần sao chép phải tạo hai log chặn. Các lần dán thông điệp không tạo log chặn mới.
3. Gõ và sao chép cùng chuỗi 12 chữ số giả từ Notepad. Kiểm tra kết quả chặn theo `clipboard-fake-12-digits` với chi tiết `source-unknown`. Agent dùng thông tin chủ sở hữu clipboard để tìm ứng dụng khi có thể.
4. Tắt bảo vệ trên web, sao chép lại từ tệp Nội bộ và thử lại chuỗi 12 chữ số. Kiểm tra cả hai lần đều cho qua và dán được.
5. Lọc hành vi Clipboard và đếm sự kiện chặn của từng lần thực hiện. Mỗi lần xóa thực tế sinh đúng một sự kiện chặn. Nhật ký chỉ chứa thời gian, ứng dụng, hành vi, kết quả, ID rule khi có và mô tả trạng thái nguồn/xử lý.

Kết quả dán được ghi nhận sau khi agent xử lý clipboard. Trường hợp clipboard đã đổi hoặc xóa thất bại được ghi là chỉ quan sát. Bảng trên mô tả kết quả mong đợi để kiểm tra trên Windows.
