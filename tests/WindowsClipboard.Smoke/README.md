# Smoke test clipboard Windows

Chạy từ thư mục repo trong phiên desktop Windows riêng, dùng .NET 10 SDK. Test ghi và xóa clipboard của phiên đó. Thoát agent đang chạy trong phiên thử trước khi chạy lệnh.

```powershell
dotnet run --project tests/WindowsClipboard.Smoke -- --cho-phep-ghi-clipboard
```

Test dùng `ClipboardMonitor` thật, clipboard Win32 thật và thao tác dán qua `TextBox` trên luồng STA. Policy dùng quyết định mẫu và sự kiện được kiểm tra trong bộ nhớ.

- Sao chép hai lần từ nguồn được nhận diện. Mỗi lần đọc và dán cảnh báo hai lượt, với đúng một sự kiện chặn.
- Ghi bitmap kèm văn bản và PNG kèm văn bản trong hai lần riêng. Mỗi ảnh bị xóa, cảnh báo thay thế có thể đọc và dán hai lần, với đúng một sự kiện chặn ảnh.
- Cho qua bitmap và PNG kèm văn bản. Hình ảnh, byte PNG và chú thích vẫn đọc được. Sự kiện cuối ghi nhận cho qua.
