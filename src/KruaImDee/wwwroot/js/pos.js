var pos = document.querySelector("[data-pos]");
if (pos) initializePos();

function initializePos() {
  var state = null;
  var busy = false;
  var disconnected = true;
  var token = document.querySelector("#pos-token input").value;
  var lines = pos.querySelector("[data-pos-lines]");
  var options = pos.querySelector("[data-pos-options]");
  var message = pos.querySelector("[data-pos-message]");
  var payment = document.getElementById("pos-payment-dialog");
  var paymentForm = payment.querySelector("form");
  var received = paymentForm.querySelector("[name=received]");
  var confirmed = paymentForm.querySelector("[name=transferConfirmed]");
  var paymentError = payment.querySelector("[data-pos-payment-error]");
  var selectedCategory = "";
  pos
    .querySelector("[data-open-customer-display]")
    .addEventListener("click", function (event) {
      var customerWindow = window.open(
        "/Staff/CustomerDisplay",
        "one-more-bite-customer",
        "popup=yes,width=1100,height=800",
      );
      if (customerWindow) {
        event.preventDefault();
        customerWindow.opener = null;
        customerWindow.focus();
      }
    });

  function money(value) {
    return (
      "฿" +
      Number(value || 0).toLocaleString("th-TH", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })
    );
  }
  function element(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }
  function tell(text, error) {
    message.textContent = text;
    message.classList.toggle("is-error", !!error);
    message.setAttribute("role", error ? "alert" : "status");
    if (payment.open && error) paymentError.textContent = text;
  }
  function controls() {
    var locked = state && !!state.orderId;
    var hasItems = state && state.lines.length > 0;
    pos.querySelectorAll("[data-pos-add]").forEach(function (button) {
      button.disabled =
        busy || disconnected || locked || button.dataset.soldOut === "true";
    });
    lines.querySelectorAll("button,input").forEach(function (control) {
      control.disabled =
        busy || disconnected || locked || control.dataset.atMinimum === "true";
    });
    options
      .querySelectorAll("input,textarea,button")
      .forEach(function (control) {
        control.disabled =
          busy ||
          disconnected ||
          locked ||
          control.dataset.unavailable === "true";
      });
    pos.querySelector("[data-pos-pay]").disabled =
      busy ||
      disconnected ||
      !hasItems ||
      !!(state && state.message) ||
      !!(state && state.paid) ||
      !!(state && state.status === "Cancelled");
    pos.querySelector("[data-pos-send]").disabled =
      busy ||
      disconnected ||
      !hasItems ||
      locked ||
      !state.tableId ||
      !!state.message;
    pos.querySelector("[data-pos-new]").disabled =
      busy || disconnected || !locked || state.status === "AwaitingPayment";
    pos.querySelector("[data-pos-cancel]").disabled = busy || disconnected;
    payment.querySelector("[data-pos-confirm]").disabled =
      busy || disconnected || !state || state.paid;
    payment.querySelectorAll("[name=method]").forEach(function (radio) {
      radio.disabled = busy;
    });
    pos.setAttribute("aria-busy", String(busy));
  }
  function renderLine(line) {
    var row = element("article", "pos-bill-line");
    row.dataset.posLine = line.index;
    var photo = element("img");
    if (line.imageUrl) photo.src = line.imageUrl;
    photo.alt = "";
    var body = element("div", "pos-line-body");
    var heading = element("div", "pos-line-heading");
    heading.append(
      element("strong", "", line.name),
      element("span", "", money(line.lineTotal)),
    );
    var edit = element("div", "pos-line-edit");
    var minus = element("button", "pos-step", "−");
    minus.type = "button";
    minus.setAttribute("aria-label", "ลดจำนวน " + line.name);
    minus.dataset.atMinimum = String(line.quantity <= 1);
    var quantity = element("input");
    quantity.type = "number";
    quantity.min = "1";
    quantity.max = "99";
    quantity.value = line.quantity;
    quantity.dataset.posQuantity = line.index;
    quantity.setAttribute("aria-label", "จำนวน " + line.name);
    var plus = element("button", "pos-step", "+");
    plus.type = "button";
    plus.setAttribute("aria-label", "เพิ่มจำนวน " + line.name);
    var remove = element("button", "text-button pos-remove", "ลบ");
    remove.type = "button";
    remove.setAttribute("aria-label", "ลบ " + line.name);
    remove.dataset.posRemove = line.index;
    function updateQuantity(value) {
      if (!Number.isInteger(value) || value < 1 || value > 99) {
        quantity.value = line.quantity;
        tell("จำนวนอาหารต้องเป็นจำนวนเต็ม 1 ถึง 99", true);
        return;
      }
      mutate("PosUpdate", {
        index: line.index,
        quantity: value,
        note: line.note || "",
      });
    }
    minus.addEventListener("click", function () {
      updateQuantity(line.quantity - 1);
    });
    plus.addEventListener("click", function () {
      updateQuantity(line.quantity + 1);
    });
    quantity.addEventListener("change", function () {
      updateQuantity(Number(quantity.value));
    });
    remove.addEventListener("click", function () {
      mutate("PosRemove", { index: line.index });
    });
    edit.append(
      minus,
      quantity,
      plus,
      element("small", "", money(line.unitPrice) + "/จาน"),
      remove,
    );
    var note = element("details", "pos-line-note");
    note.append(element("summary", "", line.note || "หมายเหตุถึงครัว"));
    var noteInput = element("input");
    noteInput.value = line.note || "";
    noteInput.maxLength = 300;
    noteInput.placeholder = "เช่น เผ็ดน้อย ไม่ใส่ผัก";
    noteInput.setAttribute("aria-label", "หมายเหตุ " + line.name);
    noteInput.addEventListener("change", function () {
      mutate("PosUpdate", {
        index: line.index,
        quantity: line.quantity,
        note: noteInput.value,
      });
    });
    note.append(noteInput);
    body.append(heading, edit, note);
    if (line.imageUrl) row.append(photo);
    row.append(body);
    return row;
  }
  function render(next) {
    state = next;
    disconnected = false;
    lines.replaceChildren();
    if (!state.lines.length)
      lines.append(element("p", "pos-empty", "เลือกอาหารทางซ้ายเพื่อเริ่มบิล"));
    state.lines.forEach(function (line) {
      lines.append(renderLine(line));
    });
    pos.querySelector("[data-pos-subtotal]").textContent = money(
      state.subtotal,
    );
    pos.querySelector("[data-pos-discount]").textContent = money(
      state.discount,
    );
    pos.querySelector("[data-pos-vat]").textContent = money(state.vatAmount);
    pos.querySelector("[data-pos-total]").textContent = money(state.total);
    payment.querySelector("[data-pos-payment-total]").textContent = money(
      state.total,
    );
    payment.querySelector("[data-transfer-amount]").textContent = money(
      state.total,
    );
    pos.querySelector("[data-pos-table-label]").textContent =
      state.tableName || "รับกลับบ้าน";
    var statusText = "ยังไม่ส่งครัว";
    if (state.status === "Cancelled") {
      statusText = "บิลถูกยกเลิกแล้ว";
    } else if (state.paid) {
      statusText = "รับชำระแล้ว, ส่งออเดอร์เข้าครัวแล้ว";
    } else if (state.status === "AwaitingPayment") {
      statusText = "รอรับชำระเงิน, ยังไม่ส่งครัว";
    } else if (state.orderId) {
      statusText = "ส่งครัวแล้ว, รอชำระเงิน";
    }
    pos.querySelector("[data-pos-state-label]").textContent = statusText;
    pos.querySelector("[data-pos-cancel]").hidden =
      state.status !== "AwaitingPayment";
    pos.querySelector("[data-pos-send]").hidden = !!state.orderId;
    var receipt = pos.querySelector("[data-pos-receipt]");
    receipt.hidden = !state.paid;
    if (state.paid)
      receipt.href = state.receiptUrl || "/Orders/Receipt/" + state.orderId;
    if (!options.contains(document.activeElement)) {
      options.querySelector("[name=promo]").value = state.promo || "";
      options.querySelector("[name=note]").value = state.note || "";
    }
    options.querySelectorAll("[name=tableId]").forEach(function (radio) {
      radio.checked = String(state.tableId || "") === radio.value;
    });
    if (payment.open && state.paymentMethod) {
      payment.querySelectorAll("[name=method]").forEach(function (radio) {
        radio.checked = radio.value === state.paymentMethod;
      });
      syncPayment();
    }
    controls();
    if (state.message) tell(state.message, true);
  }
  async function mutate(action, data) {
    // Keep one bill change in flight until its server state has been rendered.
    if (busy) return false;
    busy = true;
    controls();
    tell("กำลังบันทึกรายการ", false);
    var body = new FormData();
    body.append("__RequestVerificationToken", token);
    Object.keys(data || {}).forEach(function (key) {
      body.append(key, data[key] == null ? "" : data[key]);
    });
    try {
      var response = await fetch("/Staff/" + action, {
        method: "POST",
        body: body,
        headers: { "X-Requested-With": "XMLHttpRequest" },
      });
      var result = await response.json();
      if (!response.ok)
        throw new Error(result.message || "บันทึกไม่สำเร็จ กรุณาตรวจสอบรายการ");
      render(result);
      tell(
        result.message ||
          (result.paid ? "รับชำระและส่งครัวแล้ว" : "บันทึกรายการแล้ว"),
        !!result.message,
      );
      return true;
    } catch (error) {
      // The server may have saved the change before the response was lost.
      // Reload the bill before allowing another action.
      try {
        var latest = await fetch("/Staff/PosState", { cache: "no-store" });
        if (!latest.ok) throw new Error("Cannot refresh bill");
        render(await latest.json());
      } catch (refreshError) {
        disconnected = true;
      }
      tell(
        disconnected
          ? "การเชื่อมต่อขัดข้อง กรุณารีเฟรชเพื่อโหลดบิลล่าสุด"
          : error.message || "บันทึกไม่สำเร็จ กรุณาตรวจสอบบิล",
        true,
      );
      return false;
    } finally {
      busy = false;
      controls();
    }
  }
  function filterProducts() {
    var query = pos
      .querySelector("[data-pos-search]")
      .value.trim()
      .toLocaleLowerCase("th");
    var count = 0;
    pos.querySelectorAll("[data-pos-add]").forEach(function (button) {
      var visible =
        (!selectedCategory || button.dataset.category === selectedCategory) &&
        button.dataset.name.toLocaleLowerCase("th").includes(query);
      button.hidden = !visible;
      if (visible) count++;
    });
    pos.querySelector("[data-pos-no-results]").hidden = count > 0;
  }
  pos
    .querySelector("[data-pos-search]")
    .addEventListener("input", filterProducts);
  pos.querySelectorAll("[data-pos-category]").forEach(function (button) {
    button.addEventListener("click", function () {
      selectedCategory = button.dataset.posCategory;
      pos.querySelectorAll("[data-pos-category]").forEach(function (item) {
        item.setAttribute("aria-pressed", String(item === button));
      });
      filterProducts();
    });
  });
  pos.querySelectorAll("[data-pos-add]").forEach(function (button) {
    button.addEventListener("click", function () {
      mutate("PosAdd", { menuItemId: button.dataset.posAdd, quantity: 1 });
    });
  });
  options.querySelectorAll("[name=tableId]").forEach(function (radio) {
    radio.dataset.unavailable = String(radio.disabled);
    radio.addEventListener("change", saveOptions);
  });
  function saveOptions(event) {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    var selected = options.querySelector("[name=tableId]:checked");
    mutate("PosOptions", {
      tableId: selected ? selected.value : "",
      promo: options.querySelector("[name=promo]").value,
      note: options.querySelector("[name=note]").value,
    });
  }
  options.addEventListener("submit", saveOptions);
  function paymentMethod() {
    return payment.querySelector("[name=method]:checked").value;
  }
  function syncPayment() {
    var transfer = paymentMethod() === "Transfer";
    payment.querySelector("[data-pos-transfer]").hidden = !transfer;
    payment.querySelector("[data-pos-cash]").hidden = transfer;
    received.disabled = transfer;
    confirmed.disabled = !transfer;
    confirmed.required = transfer;
    if (!transfer) confirmed.checked = false;
    var change = Math.max(
      0,
      Number(received.value || 0) - (state ? state.total : 0),
    );
    payment.querySelector("[data-pos-change]").textContent = money(change);
  }
  pos
    .querySelector("[data-pos-pay]")
    .addEventListener("click", async function () {
      paymentError.textContent = "";
      received.value = "";
      confirmed.checked = false;
      payment.showModal();
      document.body.classList.add("dialog-open");
      syncPayment();
      await mutate("PosPrepare", {
        requestKey: state.requestKey,
        method: paymentMethod(),
      });
    });
  payment.querySelectorAll("[name=method]").forEach(function (radio) {
    radio.addEventListener("change", async function () {
      paymentError.textContent = "";
      syncPayment();
      await mutate("PosPrepare", {
        requestKey: state.requestKey,
        method: paymentMethod(),
      });
    });
  });
  received.addEventListener("input", syncPayment);
  payment
    .querySelector("[data-pos-exact]")
    .addEventListener("click", function () {
      received.value = state.total;
      syncPayment();
    });
  payment.querySelectorAll("[data-pos-cash-value]").forEach(function (button) {
    button.addEventListener("click", function () {
      received.value = button.dataset.posCashValue;
      syncPayment();
    });
  });
  payment
    .querySelector("[data-pos-close-payment]")
    .addEventListener("click", function () {
      payment.close();
    });
  payment.addEventListener("close", function () {
    document.body.classList.remove("dialog-open");
  });
  paymentForm.addEventListener("submit", async function (event) {
    event.preventDefault();
    event.stopPropagation();
    paymentError.textContent = "";
    if (
      paymentMethod() === "Cash" &&
      (!received.value || Number(received.value) < state.total)
    ) {
      paymentError.textContent = "เงินที่รับไม่พอ กรุณาตรวจสอบยอด";
      received.focus();
      return;
    }
    if (paymentMethod() === "Transfer" && !confirmed.checked) {
      paymentError.textContent = "กรุณาตรวจสอบและยืนยันยอดโอนก่อนรับชำระ";
      confirmed.focus();
      return;
    }
    var saved = await mutate("PosCheckout", {
      requestKey: state.requestKey,
      method: paymentMethod(),
      received: received.value || 0,
      transferConfirmed: confirmed.checked,
      payLater: false,
    });
    if (saved) payment.close();
  });
  pos.querySelector("[data-pos-send]").addEventListener("click", function () {
    mutate("PosCheckout", {
      requestKey: state.requestKey,
      payLater: true,
      method: "Cash",
      received: 0,
      transferConfirmed: false,
    });
  });
  pos.querySelector("[data-pos-cancel]").addEventListener("click", function () {
    mutate("PosCancel", { requestKey: state.requestKey });
  });
  pos.querySelector("[data-pos-new]").addEventListener("click", function () {
    mutate("PosNew", {});
  });
  controls();
  fetch("/Staff/PosState")
    .then(function (response) {
      if (!response.ok) throw new Error("เปิดรายการไม่สำเร็จ กรุณารีเฟรชหน้า");
      return response.json();
    })
    .then(render)
    .catch(function (error) {
      tell(error.message, true);
    });
}
