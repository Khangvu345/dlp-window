# Kiểm chứng chụp màn hình trên Windows

Ứng dụng demo là **Repo DLP · Trình xem tài liệu**. Mở TXT từ mục **Mở tài liệu thử** ở khay hệ thống. Kho GitHub tại thời điểm triển khai chưa có kết quả khảo sát NV2. Word, Acrobat, trình duyệt và các cửa sổ bên ngoài agent chưa được xác nhận hỗ trợ.

Trình xem đối chiếu SHA-256 của dữ liệu đang hiển thị với nhãn trong bộ phân loại. Với policy mẫu, rule `screenshot-internal` yêu cầu bảo vệ tài liệu **Nội bộ**. Trình xem đặt `WDA_EXCLUDEFROMCAPTURE` cho chính cửa sổ cấp cao của nó. Khi chờ nhãn đúng với phiên bản đã mở hoặc khi Windows báo lỗi đặt chế độ bảo vệ, nội dung tạm ẩn. Windows 10 phiên bản 2004 (build 19041) trở lên mới có chế độ loại cửa sổ này.

## Chuẩn bị phiên kiểm chứng

1. Chạy `winver` và ghi phiên bản, số build Windows cùng cấu hình **Sử dụng phím Print Screen để mở tính năng chụp màn hình** trong cài đặt trợ năng. Ghi cả phiên bản Snipping Tool nếu có.
2. Chạy agent từ bản tích hợp các nhiệm vụ. Dùng policy mẫu có rule `classify-internal` và `screenshot-internal`. Tạo hai file TXT UTF-8 trong cùng một thư mục, với dữ liệu tổng hợp sau.

   | File | Nội dung | Nhãn mong đợi |
   | --- | --- | --- |
   | `thuong.txt` | `Bản tin ngày hôm nay` | Thông thường |
   | `noi-bo.txt` | `Tài liệu nội bộ — Bản tin ngày hôm nay` | Nội bộ |

3. Chọn thư mục trên trang quản lý và chờ hai nhãn xuất hiện. Đặt trình xem trên màn hình nền dễ nhận biết. Mỗi lần thử, xác nhận đọc được văn bản trong trình xem trước khi bấm phím hoặc mở công cụ. Ghi trường hợp nội dung chưa hiển thị vào cột thực tế và thử lại khi phân loại đã xong.
4. Với **PrintScreen**, đưa trình xem lên trước rồi nhấn phím. Nếu ảnh đi vào clipboard, dán vào Paint để xem. Nếu phím mở Snipping Tool, hoàn tất vùng chụp bao gồm trình xem và ghi rõ hành vi này.
5. Với **Win+Shift+S**, đưa trình xem lên trước, bấm tổ hợp phím và chọn vùng bao phủ trình xem. Mở bản xem trước hoặc dán ảnh vào Paint.
6. Với **Snipping Tool**, mở trực tiếp từ Start, chọn **New** rồi chọn vùng bao phủ trình xem. Mở bản xem trước và kiểm tra chữ trong ảnh.
7. Lặp lại cả ba cách với từng tài liệu khi bật và khi tắt bảo vệ. Chờ thanh trạng thái của trình xem cập nhật sau mỗi lần đổi cài đặt. Ghi số lần thử, nội dung có xuất hiện trong ảnh hay không và lưu ảnh của bài thử bằng dữ liệu tổng hợp, hoặc ghi lại các bước cùng kết quả cụ thể.

## Ma trận ứng dụng và cách chụp

Phiên bản/build Windows, phiên bản Snipping Tool và cấu hình PrintScreen được điền khi chạy trên máy Windows. Các dòng dưới đây chưa chạy trong phiên Windows của lần triển khai này.

| Ứng dụng | Cách chụp | Windows/build | Nhãn | Bảo vệ | Mong đợi | Thực tế |
| --- | --- | --- | --- | --- | --- | --- |
| Trình xem Repo DLP | PrintScreen | Chưa đo | Nội bộ | Bật | Ảnh không có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | PrintScreen | Chưa đo | Thông thường | Bật | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | PrintScreen | Chưa đo | Nội bộ | Tắt | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | PrintScreen | Chưa đo | Thông thường | Tắt | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Win+Shift+S | Chưa đo | Nội bộ | Bật | Ảnh không có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Win+Shift+S | Chưa đo | Thông thường | Bật | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Win+Shift+S | Chưa đo | Nội bộ | Tắt | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Win+Shift+S | Chưa đo | Thông thường | Tắt | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Snipping Tool trực tiếp | Chưa đo | Nội bộ | Bật | Ảnh không có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Snipping Tool trực tiếp | Chưa đo | Thông thường | Bật | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Snipping Tool trực tiếp | Chưa đo | Nội bộ | Tắt | Ảnh có nội dung tài liệu | Chưa thử |
| Trình xem Repo DLP | Snipping Tool trực tiếp | Chưa đo | Thông thường | Tắt | Ảnh có nội dung tài liệu | Chưa thử |

Chỉ ghi **Chặn thành công** sau khi thao tác bị chặn hoặc đã kiểm tra ảnh và thấy nội dung cần bảo vệ vắng mặt. Lỗi của công cụ, chế độ không được hỗ trợ và các lần chưa chạy được ghi bằng trạng thái riêng. Trạng thái Windows đã nhận yêu cầu chỉ xác nhận kết quả gọi API.

Trình xem ghi sự kiện thực tế khi nhận được PrintScreen hoặc Win+Shift+S lúc nó ở tiền cảnh. Nhật ký cho biết policy **Cho qua** hoặc sự kiện **Chỉ quan sát**, ghi ID rule nếu có và chưa xác minh ảnh thu được. Khi mở Snipping Tool trực tiếp, tiến hành đối chiếu bằng ảnh theo bước 6. Trình xem chưa nhận diện tự động cách mở này.
