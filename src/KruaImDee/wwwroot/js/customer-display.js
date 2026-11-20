var displayRoot = document.querySelector("[data-customer-display]");
var previousDisplay = "";

// Describe the latest bill in the order of importance.
function customerBillStatus(state) {
  if (state.status === "Cancelled") {
    return "บิลถูกยกเลิกแล้ว";
  }
  if (state.paid) {
    return "รับชำระแล้ว";
  }
  if (state.status === "AwaitingPayment") {
    return "กรุณาตรวจสอบรายการและยอดชำระ";
  }
  if (state.orderId) {
    return "รับออเดอร์แล้ว";
  }
  if (state.lines.length > 0) {
    return "รายการอาหารของคุณ";
  }
  return "เลือกอาหารกับพนักงานได้เลย";
}

function displayMoney(value) {
  return (
    "฿" +
    Number(value || 0).toLocaleString("th-TH", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })
  );
}
function drawCustomerDisplay(state) {
  var container = displayRoot.querySelector("[data-display-lines]");
  container.replaceChildren();
  state.lines.forEach(function (line) {
    var row = document.createElement("div");
    row.className = "customer-display-line";
    var photo = document.createElement("img");
    if (line.imageUrl) photo.src = line.imageUrl;
    photo.alt = "";
    var name = document.createElement("strong");
    name.textContent = line.name;
    var quantity = document.createElement("span");
    quantity.textContent = line.quantity + " จาน";
    var total = document.createElement("strong");
    total.textContent = displayMoney(line.lineTotal);
    if (line.imageUrl) row.append(photo);
    else row.classList.add("no-photo");
    row.append(name, quantity, total);
    container.append(row);
  });
  displayRoot.querySelector("[data-display-heading]").textContent = state.lines
    .length
    ? state.tableName || "รับกลับบ้าน"
    : "ยินดีต้อนรับ";
  displayRoot.querySelector("[data-display-status]").textContent = customerBillStatus(state);
  displayRoot.querySelector("[data-display-subtotal]").textContent =
    displayMoney(state.subtotal);
  displayRoot.querySelector("[data-display-discount]").textContent =
    displayMoney(state.discount);
  displayRoot.querySelector("[data-display-vat]").textContent = displayMoney(
    state.vatAmount,
  );
  displayRoot.querySelector("[data-display-total]").textContent = displayMoney(
    state.total,
  );
  if (state.message) {
    displayRoot.querySelector("[data-display-status]").textContent =
      "พนักงานกำลังตรวจสอบรายการ";
    displayRoot.querySelector("[data-display-total]").textContent =
      "กำลังคำนวณ";
  }
  displayRoot.querySelector("[data-transfer-amount]").textContent =
    displayMoney(state.total);
  displayRoot.querySelector("[data-display-qr]").hidden =
    state.paymentMethod !== "Transfer" ||
    state.paid ||
    !state.orderId ||
    state.status === "Cancelled";
  displayRoot.querySelector("[data-display-paid]").hidden = !state.paid;
  displayRoot.querySelector("[data-display-change]").textContent =
    Number(state.change) > 0 ? "เงินทอน " + displayMoney(state.change) : "";
}
async function refreshCustomerDisplay() {
  try {
    var response = await fetch("/Staff/DisplayState", { cache: "no-store" });
    if (!response.ok) throw new Error("Display unavailable");
    var state = await response.json();
    var signature = JSON.stringify(state);
    if (signature !== previousDisplay) {
      drawCustomerDisplay(state);
      previousDisplay = signature;
    }
  } catch (error) {
    displayRoot.querySelector("[data-display-qr]").hidden = true;
    displayRoot.querySelector("[data-display-paid]").hidden = true;
    displayRoot.querySelector("[data-display-total]").textContent =
      "รอเชื่อมต่อ";
    displayRoot.querySelector("[data-display-status]").textContent =
      "กำลังเชื่อมต่อกับพนักงาน";
    previousDisplay = "";
  }
  // Schedule after completion so a slow connection cannot overlap requests.
  setTimeout(refreshCustomerDisplay, 1000);
}
refreshCustomerDisplay();
