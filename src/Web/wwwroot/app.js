const dinhDangGio = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "short", timeStyle: "medium"
});
const trang = { overview: "Tổng quan", policy: "Quy tắc", events: "Nhật ký" };
const trangThaiTaiLieu = {
  classified: ["Đã phân loại", "good"],
  needsOcr: ["Cần OCR", "muted"],
  readError: ["Lỗi đọc", "error"]
};
const ketQuaSuKien = {
  allow: ["Cho qua", "good"],
  block: ["Đã chặn", "error"],
  observe: ["Chỉ quan sát", "muted"]
};
const loaiDieuKien = { keyword: "Từ khóa", regex: "Regex", label: "Nhãn nguồn" };
const loaiTheoPhamVi = {
  classification: ["keyword", "regex"],
  clipboard: ["label", "regex"],
  screenshot: ["label"]
};
const hanhVi = { clipboard: "Clipboard", screenshot: "Chụp màn hình" };
const chiTietClipboard = {
  "source-known": "Đã xác định nguồn",
  "source-unknown": "Chưa xác định nguồn",
  "read-failed": "Lỗi đọc clipboard",
  "no-text": "Clipboard không có văn bản",
  "clipboard-changed": "Clipboard đã thay đổi",
  "clear-failed": "Không xóa được clipboard"
};

const trangThaiKetNoi = document.querySelector("#state");
const lamMoi = document.querySelector("#refresh");
const bieuMauCauHinh = document.querySelector("#settings-form");
const thuMuc = document.querySelector("#scan-directory");
const chonThuMuc = document.querySelector("#choose-folder");
const baoVe = document.querySelector("#protection");
const bieuMauQuyTac = document.querySelector("#policy-form");
const danhSachQuyTac = document.querySelector("#rules");
const bieuMauBoLoc = document.querySelector("#event-filters");
let trangHienTai = "";
let daSuaCauHinh = false;
let dangLuuCauHinh = false;
let dangCapNhatTongQuan = false;
let phienBanCauHinh = 0;
let daTaiChinhSach = false;
let phamViDangXem = "classification";
const huongDanPhamVi = {
  classification: "Khi quét thư mục, agent đọc nội dung và tự gán nhãn cho tài liệu.",
  clipboard: "Agent kiểm tra nội dung sao chép ngay cả khi chưa xác định được nhãn tài liệu nguồn.",
  screenshot: "Chọn hành động theo nhãn tài liệu mở trong trình xem Repo DLP."
};

function chonPhamVi(phamVi) {
  phamViDangXem = phamVi;
  document.querySelector("#scope-help").textContent = huongDanPhamVi[phamVi];
  for (const nut of document.querySelectorAll("[data-scope]"))
    nut.setAttribute("aria-pressed", String(nut.dataset.scope === phamVi));
  for (const muc of danhSachQuyTac.children)
    muc.hidden = layTruong(muc, "scope").value !== phamVi;
}
for (const nut of document.querySelectorAll("[data-scope]"))
  nut.addEventListener("click", () => chonPhamVi(nut.dataset.scope));

let thongSoNhatKy = "";
let lanTaiNhatKy = 0;

document.querySelector("#origin").textContent = window.location.host;

function hienThongBao(phanTu, noiDung, coLoi = false) {
  phanTu.textContent = noiDung ?? "";
  phanTu.hidden = !noiDung;
  phanTu.dataset.tone = coLoi ? "error" : "good";
}

async function goiApi(duongDan, phuongThuc = "GET", duLieu) {
  const tuyChon = { method: phuongThuc, cache: "no-store" };
  if (duLieu !== undefined) {
    tuyChon.headers = { "Content-Type": "application/json" };
    tuyChon.body = JSON.stringify(duLieu);
  }
  const phanHoi = await fetch(duongDan, tuyChon);
  const ketQua = await phanHoi.json().catch(() => null);
  if (!phanHoi.ok) {
    const loi = new Error(ketQua?.message ?? "Không thể thực hiện yêu cầu.");
    loi.chiTiet = ketQua?.errors ?? [];
    throw loi;
  }
  return ketQua;
}

function veBang(thanBang, soCot, duLieu, taoHang, thongBaoRong) {
  const noiDung = document.createDocumentFragment();
  for (const muc of duLieu) {
    const hang = document.createElement("tr");
    for (const [giaTri, sacThai] of taoHang(muc)) {
      const o = document.createElement("td");
      o.textContent = giaTri ?? "—";
      if (sacThai) o.dataset.tone = sacThai;
      hang.append(o);
    }
    noiDung.append(hang);
  }
  if (duLieu.length === 0) {
    const hang = document.createElement("tr");
    const o = document.createElement("td");
    o.colSpan = soCot;
    o.className = "empty";
    o.textContent = thongBaoRong;
    hang.append(o);
    noiDung.append(hang);
  }
  thanBang.replaceChildren(noiDung);
}

function veCauHinh(phanHoi) {
  const { settings, scanError, hasFolderPicker } = phanHoi;
  if (!daSuaCauHinh) {
    thuMuc.value = settings.scanDirectory ?? "";
    baoVe.checked = settings.protectionEnabled;
  }
  chonThuMuc.hidden = !hasFolderPicker;
  document.querySelector("#protection-state").textContent = settings.protectionEnabled ? (settings.scanDirectory ? "Đang bật" : "Đang bật · Chưa chọn thư mục") : "Đang tắt";
  hienThongBao(document.querySelector("#scan-error"), scanError, true);
}

async function taiTongQuan() {
  if (dangCapNhatTongQuan || dangLuuCauHinh) return;
  dangCapNhatTongQuan = true;
  const phienBan = phienBanCauHinh;
  try {
    const [cauHinh, taiLieu] = await Promise.all([goiApi("/api/settings"), goiApi("/api/documents")]);
    if (dangLuuCauHinh || phienBan !== phienBanCauHinh) return;
    veCauHinh(cauHinh);
    document.querySelector("#document-count").textContent = taiLieu.length.toLocaleString("vi-VN");
    veBang(document.querySelector("#documents"), 4, taiLieu, muc =>
      [[muc.path], trangThaiTaiLieu[muc.status] ?? [muc.status], [muc.label], [muc.ruleId]],
    cauHinh.settings.scanDirectory ? "Chưa có tài liệu được lập chỉ mục." : "Chọn thư mục quét để xem tài liệu.");
  } finally {
    dangCapNhatTongQuan = false;
  }
}

async function luuCauHinh(suKien) {
  suKien.preventDefault();
  const thongBao = document.querySelector("#settings-message");
  const nutLuu = bieuMauCauHinh.querySelector("button[type=submit]");
  const duLieu = { scanDirectory: thuMuc.value.trim() || null, protectionEnabled: baoVe.checked };
  phienBanCauHinh++;
  dangLuuCauHinh = true;
  for (const phanTu of [nutLuu, thuMuc, baoVe, chonThuMuc]) phanTu.disabled = true;
  hienThongBao(thongBao);
  try {
    const phanHoi = await goiApi("/api/settings", "PUT", duLieu);
    daSuaCauHinh = false;
    veCauHinh(phanHoi);
    hienThongBao(thongBao, phanHoi.settings.scanDirectory ? "Đã áp dụng cấu hình." : "Chưa chọn thư mục. Agent chưa quét tài liệu.");
  } catch (loi) {
    hienThongBao(thongBao, loi.message, true);
  } finally {
    dangLuuCauHinh = false;
    for (const phanTu of [nutLuu, thuMuc, baoVe, chonThuMuc]) phanTu.disabled = false;
  }
  await lamMoiTrang();
}

bieuMauCauHinh.addEventListener("input", () => { daSuaCauHinh = true; });
bieuMauCauHinh.addEventListener("submit", luuCauHinh);
chonThuMuc.addEventListener("click", async () => {
  chonThuMuc.disabled = true;
  const nutLuu = bieuMauCauHinh.querySelector("button[type=submit]");
  nutLuu.disabled = true;
  const thongBao = document.querySelector("#settings-message");
  hienThongBao(thongBao, "Đang mở cửa sổ chọn thư mục trên Windows…");
  try {
    const { path } = await goiApi("/api/folder-picker", "POST");
    if (path) {
      daSuaCauHinh = true;
      thuMuc.value = path;
      thuMuc.focus();
      hienThongBao(thongBao, "Đã chọn thư mục. Bấm Áp dụng cấu hình để bắt đầu quét.");
    } else {
      hienThongBao(thongBao, "Chưa chọn thư mục mới.");
    }
  } catch (loi) {
    hienThongBao(document.querySelector("#settings-message"), loi.message, true);
  } finally {
    chonThuMuc.disabled = false;
    nutLuu.disabled = false;
  }
});

function layTruong(quyTac, ten) {
  return quyTac.querySelector(`[data-field="${ten}"]`);
}

function capNhatTruongQuyTac(phanTu, kieuDaChon) {
  const phamVi = layTruong(phanTu, "scope").value;
  const loai = layTruong(phanTu, "kind");
  const cacLoai = loaiTheoPhamVi[phamVi];
  loai.replaceChildren(...cacLoai.map(kieu => new Option(loaiDieuKien[kieu], kieu)));
  loai.value = cacLoai.includes(kieuDaChon) ? kieuDaChon : cacLoai[0];
  phanTu.querySelector("[data-pattern-label]").textContent = loaiDieuKien[loai.value];
  const laPhanLoai = phamVi === "classification";
  phanTu.querySelector("[data-label-field]").hidden = !laPhanLoai;
  layTruong(phanTu, "label").disabled = !laPhanLoai;
  phanTu.querySelector("[data-action-field]").hidden = laPhanLoai;
  layTruong(phanTu, "action").disabled = laPhanLoai;
}

function themQuyTac(quyTac, chuyenTieuDiem = false) {
  const khuon = document.querySelector("#rule-template");
  const phanTu = khuon.content.firstElementChild.cloneNode(true);
  const tieuDe = phanTu.querySelector("legend");
  tieuDe.textContent = quyTac.scope === "classification"
    ? quyTac.label || "Nhãn mới"
    : quyTac.pattern || "Điều kiện mới";
  phanTu.hidden = quyTac.scope !== phamViDangXem;
  layTruong(phanTu, "id").value = quyTac.id;
  layTruong(phanTu, "scope").value = quyTac.scope;
  layTruong(phanTu, "priority").value = quyTac.priority;
  layTruong(phanTu, "pattern").value = quyTac.pattern;
  layTruong(phanTu, "label").value = quyTac.label ?? "";
  layTruong(phanTu, "action").value = quyTac.action ?? "block";
  capNhatTruongQuyTac(phanTu, quyTac.kind);
  const truongTieuDe = layTruong(phanTu, quyTac.scope === "classification" ? "label" : "pattern");
  truongTieuDe.addEventListener("input", () => { tieuDe.textContent = truongTieuDe.value || "Quy tắc mới"; });
  layTruong(phanTu, "kind").addEventListener("change", ({ target }) => {
    phanTu.querySelector("[data-pattern-label]").textContent = loaiDieuKien[target.value];
  });
  phanTu.querySelector(".remove-rule").addEventListener("click", () => {
    phanTu.remove();
    document.querySelector("#add-rule").focus();
  });
  danhSachQuyTac.append(phanTu);
  if (chuyenTieuDiem) layTruong(phanTu, "pattern").focus();
}

function layChinhSachTuBieuMau() {
  return {
    rules: Array.from(danhSachQuyTac.children, phanTu => {
      const phamVi = layTruong(phanTu, "scope").value;
      return {
        id: layTruong(phanTu, "id").value.trim(),
        scope: phamVi,
        priority: layTruong(phanTu, "priority").valueAsNumber,
        kind: layTruong(phanTu, "kind").value,
        pattern: layTruong(phanTu, "pattern").value,
        label: phamVi === "classification" ? layTruong(phanTu, "label").value.trim() : null,
        action: phamVi === "classification" ? null : layTruong(phanTu, "action").value
      };
    })
  };
}

function hienLoiQuyTac(cacLoi) {
  const cacQuyTac = Array.from(danhSachQuyTac.children);
  for (const [viTri, quyTac] of cacQuyTac.entries()) {
    const loi = cacLoi.filter(muc => muc.ruleIndex === viTri);
    const thongBao = quyTac.querySelector(".rule-error");
    const dieuKien = layTruong(quyTac, "pattern");
    thongBao.id = `rule-error-${viTri}`;
    hienThongBao(thongBao, loi.map(muc => muc.message).join(" "), true);
    if (loi.length > 0) {
      dieuKien.setAttribute("aria-invalid", "true");
      dieuKien.setAttribute("aria-describedby", thongBao.id);
    } else {
      dieuKien.removeAttribute("aria-invalid");
      dieuKien.removeAttribute("aria-describedby");
    }
  }
}

function veChinhSach(phanHoi) {
  danhSachQuyTac.replaceChildren();
  for (const quyTac of phanHoi.policy.rules) themQuyTac(quyTac);
  chonPhamVi(phamViDangXem);
  daTaiChinhSach = true;
}

async function kiemTraHoacLuuQuyTac(luu) {
  const thongBao = document.querySelector("#policy-message");
  hienLoiQuyTac([]);
  hienThongBao(thongBao);
  if (!daTaiChinhSach) return;
  const truongSai = bieuMauQuyTac.querySelector("input:invalid, select:invalid");
  if (truongSai) {
    chonPhamVi(layTruong(truongSai.closest(".rule"), "scope").value);
    const nangCao = truongSai.closest("details");
    if (nangCao) nangCao.open = true;
    truongSai.reportValidity();
    return;
  }
  const duLieu = layChinhSachTuBieuMau();
  const khoa = Array.from(bieuMauQuyTac.elements).filter(phanTu => !phanTu.disabled);
  for (const phanTu of khoa) phanTu.disabled = true;
  try {
    if (luu) {
      veChinhSach(await goiApi("/api/policy", "PUT", duLieu));
      hienThongBao(thongBao, "Đã lưu quy tắc.");
    } else {
      const { errors } = await goiApi("/api/policy/validate", "POST", duLieu);
      if (errors.length > 0) {
        hienLoiQuyTac(errors);
        hienThongBao(thongBao, thongBaoLoiChinhSach(errors), true);
      } else {
        hienThongBao(thongBao, "Quy tắc hợp lệ.");
      }
    }
  } catch (loi) {
    if (loi.chiTiet?.length > 0) hienLoiQuyTac(loi.chiTiet);
    hienThongBao(thongBao, loi.chiTiet?.length ? thongBaoLoiChinhSach(loi.chiTiet) : loi.message, true);
  } finally {
    for (const phanTu of khoa) phanTu.disabled = false;
    const truongSai = danhSachQuyTac.querySelector('[aria-invalid="true"]');
    if (truongSai) {
      chonPhamVi(layTruong(truongSai.closest(".rule"), "scope").value);
      truongSai.focus();
    }
  }
}

function thongBaoLoiChinhSach(cacLoi) {
  const loiChung = cacLoi.filter(muc => muc.ruleIndex === null).map(muc => muc.message);
  return loiChung.length ? loiChung.join(" ") : "Kiểm tra các quy tắc có lỗi.";
}

document.querySelector("#add-rule").addEventListener("click", () => {
  if (!daTaiChinhSach) return;
  themQuyTac({ id: phamViDangXem + "-" + crypto.randomUUID(), scope: phamViDangXem,
    priority: 0, kind: loaiTheoPhamVi[phamViDangXem][0], pattern: "" }, true);
});
document.querySelector("#validate-policy").addEventListener("click", () => kiemTraHoacLuuQuyTac(false));
bieuMauQuyTac.addEventListener("submit", suKien => {
  suKien.preventDefault();
  kiemTraHoacLuuQuyTac(true);
});

function dauNgayDiaPhuong(ngay, themMotNgay = false) {
  const dauNgay = new Date(`${ngay}T00:00:00`);
  if (themMotNgay) dauNgay.setDate(dauNgay.getDate() + 1);
  return dauNgay.toISOString();
}

async function taiNhatKy() {
  const thuTu = ++lanTaiNhatKy;
  const suKien = await goiApi("/api/events" + thongSoNhatKy);
  if (thuTu !== lanTaiNhatKy) return;
  veBang(document.querySelector("#event-rows"), 6, suKien, muc => [
    [dinhDangGio.format(new Date(muc.occurredAtUtc))],
    [muc.application], [hanhVi[muc.behavior] ?? muc.behavior],
    ketQuaSuKien[muc.result] ?? [muc.result], [muc.ruleId], [moTaSuKien(muc)]
  ], "Không có sự kiện phù hợp.");
}

function moTaSuKien(muc) {
  if (muc.behavior !== "clipboard" || !muc.detail) return muc.detail;
  return muc.detail.split(" ").map(ma => chiTietClipboard[ma] ?? ma).join(" · ");
}

bieuMauBoLoc.addEventListener("submit", suKien => {
  suKien.preventDefault();
  const tuNgay = document.querySelector("#from-date").value;
  const denNgay = document.querySelector("#until-date").value;
  const hanhVi = document.querySelector("#event-behavior").value;
  const thongBao = document.querySelector("#events-message");
  if (tuNgay && denNgay && tuNgay > denNgay) {
    hienThongBao(thongBao, "Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.", true);
    return;
  }
  hienThongBao(thongBao);
  const truyVan = new URLSearchParams();
  if (tuNgay) truyVan.set("fromUtc", dauNgayDiaPhuong(tuNgay));
  if (denNgay) truyVan.set("untilUtc", dauNgayDiaPhuong(denNgay, true));
  if (hanhVi) truyVan.set("behavior", hanhVi);
  thongSoNhatKy = "?" + truyVan;
  lamMoiTrang();
});

bieuMauBoLoc.addEventListener("reset", () => {
  thongSoNhatKy = "";
  hienThongBao(document.querySelector("#events-message"));
  lamMoiTrang();
});

async function lamMoiTrang() {
  const thongBao = document.querySelector("#connection-error");
  lamMoi.disabled = true;
  try {
    const phien = await goiApi("/api/status");
    const batDau = document.querySelector("#started");
    batDau.dateTime = phien.startedAtUtc;
    batDau.textContent = dinhDangGio.format(new Date(phien.startedAtUtc));
    trangThaiKetNoi.textContent = "Agent đang chạy";
    trangThaiKetNoi.dataset.tone = "good";
  } catch {
    trangThaiKetNoi.textContent = "Mất kết nối";
    trangThaiKetNoi.dataset.tone = "error";
    hienThongBao(thongBao, "Không kết nối được với agent.", true);
    lamMoi.disabled = false;
    return;
  }
  try {
    if (trangHienTai === "overview") await taiTongQuan();
    if (trangHienTai === "policy" && !daTaiChinhSach) veChinhSach(await goiApi("/api/policy"));
    if (trangHienTai === "events") await taiNhatKy();
    hienThongBao(thongBao);
  } catch (loi) {
    hienThongBao(thongBao, loi.message, true);
  } finally {
    lamMoi.disabled = false;
  }
}

function chuyenTrang() {
  const maTrang = window.location.hash.slice(1);
  trangHienTai = Object.hasOwn(trang, maTrang) ? maTrang : "overview";
  document.querySelector("#page-title").textContent = trang[trangHienTai];
  for (const ma of Object.keys(trang)) {
    document.getElementById(ma).hidden = ma !== trangHienTai;
    const lienKet = document.querySelector(`nav a[href="#${ma}"]`);
    if (ma === trangHienTai) lienKet.setAttribute("aria-current", "page");
    else lienKet.removeAttribute("aria-current");
  }
  window.scrollTo(0, 0);
  lamMoiTrang();
}

window.addEventListener("hashchange", chuyenTrang);
lamMoi.addEventListener("click", lamMoiTrang);
chuyenTrang();
window.setInterval(() => {
  if (!document.hidden && !lamMoi.disabled && trangHienTai !== "policy") lamMoiTrang();
}, 2500);
