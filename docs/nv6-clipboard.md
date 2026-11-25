# Nghiệm thu clipboard trên Windows

Chạy agent trong một phiên desktop Windows thử nghiệm. Ghi phiên bản Windows, Chrome và Notepad vào kết quả. Chọn thư mục quét có hai thư mục con dưới đây và chờ dashboard hiển thị đủ hai đường dẫn, nhãn và trạng thái **Đã phân loại**. Dùng policy mẫu và bật bảo vệ.

| Tệp UTF-8 | Nội dung theo từng dòng | Nhãn mong đợi |
|---|---|---|
| `phong-a\bao-cao.txt` | Tài liệu nội bộ<br>Hẹn gặp vào ngày mai. | Nội bộ |
| `phong-b\bao-cao.txt` | Hẹn gặp vào ngày mai.<br>123456789012 | Thông thường |

Sao chép từng mẫu riêng, đợi agent xử lý rồi dán vào ô văn bản của một ứng dụng khác. Với ca **Đã chặn**, đóng hộp thoại nếu hiện lên và kiểm tra nội dung dán là `Nội dung sao chép đã bị chặn bởi Repo DLP.`. Với ca **Cho qua**, kiểm tra nội dung dán khớp đoạn đã chọn. Dùng một tài liệu đích trống và đối chiếu sự kiện theo thời gian trong mục **Nhật ký**, lọc hành vi **Clipboard**.

## Chrome

Mở hai tệp trong hai tab Chrome bằng `Ctrl+O`. Địa chỉ mỗi tab phải là `file://` trỏ đến đúng tệp. Chọn câu `Hẹn gặp vào ngày mai.` trong nội dung trang rồi nhấn `Ctrl+C`. Lặp theo thứ tự tab Nội bộ, tab Thông thường, rồi trở lại tab Nội bộ. Agent đọc `SourceURL` trong định dạng clipboard HTML của Windows (`CF_HTML`) khi Chrome cung cấp URL nguồn tới tệp cục bộ. Hai tệp cùng tên giúp kiểm tra quyết định dựa trên đường dẫn đầy đủ.

| Nguồn | Nội dung sao chép | Kết quả mong đợi |
|---|---|---|
| Tab tệp Nội bộ có `SourceURL` hợp lệ | Câu thường | **Đã chặn** với `clipboard-internal`. Dán ra thông báo. Nhật ký ghi đã xác định nguồn từ HTML clipboard. |
| Tab tệp Thông thường có `SourceURL` hợp lệ | Cùng câu thường | **Cho qua**. Dán được nguyên câu. Nhật ký ghi đã xác định nguồn từ HTML clipboard. |
| Tab tệp Thông thường | `123456789012` | **Đã chặn** với `clipboard-fake-12-digits`. Dán ra thông báo. |
| Trang HTTPS thử nghiệm có sẵn | Câu thường, rồi `123456789012` | Câu thường **Cho qua**. Chuỗi số **Đã chặn** với `clipboard-fake-12-digits`. Nhật ký ghi lý do chưa xác định được tệp nguồn. |

Nếu Chrome không đặt `CF_HTML` hoặc `SourceURL` cục bộ hợp lệ cho thao tác đã thử, ghi rõ lý do trên nhật ký và đánh dấu ca nhận diện nhãn chưa xác minh. Câu thường phải dán được và chuỗi 12 chữ số giả vẫn bị chặn bằng regex.

## Notepad

Mở hai tệp cùng tên trong hai tab của Notepad. Sao chép đúng câu thử từ tab Nội bộ, đợi agent xử lý, chuyển sang tab Thông thường rồi sao chép cùng câu, sau đó quay lại tab Nội bộ và lặp lại. Agent dùng đường dẫn tuyệt đối mà UI Automation cung cấp cho **tab đang chọn**, khi có thể đối chiếu vùng chọn với nội dung clipboard. Tên `bao-cao.txt` riêng lẻ không đủ để tra nhãn.

| Nguồn | Nội dung sao chép | Kết quả mong đợi |
|---|---|---|
| Tab Nội bộ cung cấp đường dẫn tuyệt đối | Câu thường | **Đã chặn** với `clipboard-internal`. Dán ra thông báo. Nhật ký ghi đã xác định nguồn từ tab Notepad. |
| Tab Thông thường cung cấp đường dẫn tuyệt đối | Cùng câu thường | **Cho qua**. Dán được nguyên câu. Nhật ký ghi đã xác định nguồn từ tab Notepad. |
| Tab chưa lưu hoặc phiên bản Notepad chỉ cung cấp tên tệp | Câu thường, rồi `123456789012` | Nhật ký ghi lý do chưa lấy được đường dẫn đầy đủ. Câu thường **Cho qua**. Chuỗi số **Đã chặn** với `clipboard-fake-12-digits`. |

Nếu phiên bản Notepad trong phiên thử không cung cấp đường dẫn tuyệt đối cho tab đã lưu, ghi lại bản ứng dụng và chi tiết nhật ký. Khi đó chỉ xác nhận hành vi xét regex khi thiếu nguồn. Ca nhận diện nhãn vẫn chờ phiên bản có dữ liệu UI Automation phù hợp. Thử thêm một lượt chuyển tab ngay sau khi sao chép. Nếu agent không đối chiếu được tab hoặc vùng chọn, nhật ký phải nêu nguồn chưa xác định và quyết định phải dựa trên regex.

## Đối chứng và nhật ký

1. Trong trình xem Repo DLP, sao chép cùng câu thường từ hai tệp mẫu. Tệp Nội bộ bị chặn theo `clipboard-internal`, còn tệp Thông thường dán được nguyên câu.
2. Với một ca chặn, dán hai lần sau cùng một lần sao chép. Cả hai lần phải nhận thông báo và không sinh sự kiện chặn mới. Sao chép lại nội dung mẫu một lần nữa để kiểm tra có đúng một sự kiện chặn mới.
3. Tắt bảo vệ trên dashboard và lặp các ca chặn ở trên. Câu thường từ tệp Nội bộ và chuỗi giả 12 chữ số đều dán được nguyên vẹn. Nhật ký ghi bảo vệ đang tắt. Bật lại bảo vệ nếu còn ca thử tiếp.
4. Đối chiếu tên ứng dụng, kết quả, ID quy tắc và lý do xác định nguồn hoặc thiếu nguồn. Kiểm tra chi tiết giải thích vì sao quy tắc nhãn hoặc regex khớp, hoặc vì sao agent cho qua mặc định. Khi bật bảo vệ, đối chiếu cả phiên bản policy trong chi tiết. Nhật ký chỉ lưu metadata gồm thời gian, ứng dụng, hành vi, kết quả, ID quy tắc khi có và chi tiết trạng thái. Kiểm tra nhật ký không chứa câu hoặc chuỗi đã sao chép, URL nguồn hay đường dẫn tệp.

Ghi kết quả thực tế riêng cho từng ca. Các kết quả trong tài liệu là kỳ vọng dùng để nghiệm thu trên desktop Windows.
