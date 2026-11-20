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
6. Với **Snipping Tool**, mở từ Start, chọn **New** rồi chọn vùng bao phủ trình xem. Thử cả chế độ tự sao chép và nút **Copy** trong bản xem trước. Khi thử quy tắc chặn, chờ sự kiện **Chụp màn hình** rồi dán vào Paint và Notepad. Paint không được nhận ảnh vừa chụp. Notepad nhận thông báo nếu agent đã thay nội dung clipboard. Bấm **Copy** lần nữa trong Snipping Tool và lặp lại. Đối chiếu thời gian, kết quả và ID rule với từng lần ảnh được đưa lên clipboard.
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

Ghi riêng kết quả chặn phím chụp, loại nội dung khỏi ảnh và chặn dán ảnh qua clipboard. Để xác nhận ảnh đã loại khỏi clipboard, đối chiếu nhật ký và thử dán sau khi agent xử lý. Lỗi của công cụ, chế độ không được hỗ trợ và các lần chưa chạy được ghi bằng trạng thái riêng. Trạng thái Windows đã nhận yêu cầu chỉ xác nhận kết quả gọi API.

Với phím chụp của trình xem đang ở tiền cảnh, **Đã chặn** ghi nhận phím đã bị agent chặn. Với Snipping Tool, kết quả **Đã chặn** ở bước clipboard ghi nhận agent đã loại được ảnh của lượt sao chép đó. Kiểm tra riêng ảnh trong bản xem trước hoặc ảnh đã lưu ra đĩa để nghiệm thu cơ chế loại trừ cửa sổ của Windows.

Khi thử Snipping Tool, giữ tài liệu có nhãn mở và hiển thị trong trình xem Repo DLP. Agent nhận diện SnippingTool, ScreenClippingHost hoặc ScreenSketch. Khi Windows không cung cấp tên chủ clipboard, agent kiểm tra ứng dụng tiền cảnh. Clipboard không cung cấp tọa độ vùng chụp. Vì vậy khi tài liệu bảo vệ đang hiển thị, ảnh từ công cụ đã nhận diện có thể bị loại cả khi người dùng chọn vùng khác. Khi trình xem đã đóng hoặc thu nhỏ, bước này không xác định được tài liệu nguồn. Làm đối chứng bằng một ảnh sao chép từ ứng dụng vẽ, một tài liệu thường và một lượt tắt bảo vệ.
