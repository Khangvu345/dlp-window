const state = document.querySelector("#state");
const started = document.querySelector("#started");
const refresh = document.querySelector("#refresh");
document.querySelector("#origin").textContent = window.location.origin;

async function readStatus() {
  refresh.disabled = true;
  try {
    const response = await fetch("/api/status", { cache: "no-store" });
    if (!response.ok) throw new Error("Lỗi kết nối");
    const status = await response.json();
    const formatted = new Intl.DateTimeFormat("vi-VN", {
      dateStyle: "short", timeStyle: "medium"
    }).format(new Date(status.startedAtUtc));
    started.textContent = formatted;
    state.textContent = "Đang chạy";
  } catch {
    state.textContent = "Mất kết nối";
    started.textContent = "—";
  } finally {
    refresh.disabled = false;
  }
}

refresh.addEventListener("click", readStatus);
readStatus();
