/* Inline feedback leaves entered values in the form. */
function fieldRules(field) {
  var name = field.name.toLowerCase();
  var examples = {
    name: "เช่น น้องเอาอีก กินจุจัง",
    email: "you@example.com",
    phone: "เช่น 0812345678",
    password: "กรอกรหัสผ่าน",
    q: "ค้นหาชื่ออาหาร",
    note: "เช่น เผ็ดน้อย ไม่ใส่ผัก",
    reason: "ระบุเหตุผลในการยกเลิกหรือคืนเงิน",
    description: "อธิบายส่วนประกอบหรือรสชาติ",
    comment: "เล่ารสชาติหรือสิ่งที่ชอบ",
    imageurl: "https://example.com/food.jpg",
    address: "เลขที่ ถนน แขวง เขต จังหวัด",
    code: "เช่น SAVE10",
    promo: "กรอกรหัสส่วนลด ถ้ามี",
    price: "เช่น 99.00",
    received: "เช่น 500.00",
    value: "เช่น 10",
    minimum: "เช่น 200",
    capacity: "เช่น 4",
    quantity: "1",
    guests: "2",
    discountpercent: "เช่น 10",
  };
  var label = field.closest("label");
  var titleParts = [];
  if (label) {
    // Read only the label text, excluding its input and help elements.
    for (var child of label.childNodes) {
      if (child.nodeType === Node.TEXT_NODE) {
        titleParts.push(child.textContent.trim());
      }
    }
  }
  var title = titleParts.join(" ");
  var help = field.required ? "จำเป็นต้องกรอก" : "ไม่จำเป็นต้องกรอก";
  if (field.tagName === "SELECT") help = "เลือกหนึ่งรายการจากตัวเลือก";
  else if (field.type === "checkbox")
    help = "เลือกเพื่อเปิดใช้ ล้างเครื่องหมายเพื่อปิด";
  else if (field.type === "email") {
    help = "รูปแบบอีเมล เช่น you@example.com";
    field.maxLength = 180;
  } else if (name === "phone") {
    help = "ตัวเลข 9 ถึง 10 หลัก ไม่ใส่ขีดหรือเว้นวรรค";
    field.pattern = "[0-9]{9,10}";
    field.inputMode = "tel";
    field.maxLength = 10;
  } else if (field.type === "password") {
    var current = field.autocomplete === "current-password";
    help = current ? "กรอกรหัสผ่านของบัญชีนี้" : "8 ถึง 128 ตัวอักษร";
    if (!current) field.minLength = 8;
    field.maxLength = 128;
    if (!field.required) help += " , เว้นว่างเพื่อใช้รหัสผ่านเดิม";
  } else if (field.type === "number")
    help =
      "กรอกตัวเลข" +
      (field.min ? " ตั้งแต่ " + field.min : "") +
      (field.max ? " ถึง " + field.max : "") +
      (field.step && field.step !== "1" ? " , ทศนิยมได้" : " , จำนวนเต็ม");
  else if (field.type === "datetime-local")
    help =
      name === "startsat" &&
      field.closest("form").action.includes("/Reservations/")
        ? "เลือกวันและเวลา 10:00 ถึง 20:30 น. รอบละ 90 นาที"
        : "เลือกวันที่และเวลาท้องถิ่น";
  else if (field.type === "date") help = "เลือกวัน เดือน และปีจากปฏิทิน";
  else if (field.type === "file") help = "JPEG, PNG หรือ WebP ขนาดไม่เกิน 5 MB";
  else if (name === "imageurl")
    help = "URL รูปภาพขึ้นต้นด้วย https:// หรือที่อยู่ไฟล์ /images/";
  else if (field.maxLength > 0)
    help += " , ไม่เกิน " + field.maxLength + " ตัวอักษร";
  if (
    !field.placeholder &&
    !["checkbox", "radio", "file"].includes(field.type) &&
    field.tagName !== "SELECT"
  )
    field.placeholder = examples[name] || "กรอก" + (title || "ข้อมูล");
  if (name === "name" && field.closest("form").action.includes("/Manage/")) {
    if (title.includes("ชื่อเมนู")) {
      field.placeholder = "เช่น ข้าวผัดกุ้ง";
    } else if (title.includes("โต๊ะ")) {
      field.placeholder = "เช่น A1";
    } else if (title.includes("หมวด")) {
      field.placeholder = "เช่น อาหารจานเดียว";
    } else {
      field.placeholder = "กรอก" + title;
    }
  }
  return help;
}

function initializeFields() {
  var index = 0;
  document.querySelectorAll("form").forEach(function (form) {
    form.noValidate = true;
    form
      .querySelectorAll("input:not([type=hidden]),textarea,select")
      .forEach(function (field) {
        if (
          field.disabled ||
          field.type === "radio" ||
          field.dataset.menuSearch !== undefined
        )
          return;
        var help = document.createElement("small");
        help.className = "field-help";
        help.id = "field-help-" + ++index;
        help.textContent = fieldRules(field);
        var error = document.createElement("span");
        error.className = "field-error";
        error.id = "field-error-" + index;
        error.setAttribute("aria-live", "polite");
        field.setAttribute(
          "aria-describedby",
          [field.getAttribute("aria-describedby"), help.id, error.id]
            .filter(Boolean)
            .join(" "),
        );
        field.dataset.errorId = error.id;
        var parent = field.closest("label");
        if (parent && field.type !== "checkbox") parent.append(help, error);
        else {
          var anchor = parent || field.closest(".inline-form") || field;
          anchor.insertAdjacentElement("afterend", help);
          help.insertAdjacentElement("afterend", error);
        }
        field.addEventListener("blur", function () {
          showFieldError(field);
        });
        field.addEventListener("input", function () {
          if (field.getAttribute("aria-invalid") === "true")
            showFieldError(field);
        });
      });
  });
}

function fieldMessage(field) {
  if (field.validity.valid) return "";
  if (field.validity.valueMissing) return "กรุณากรอกข้อมูลนี้";
  if (field.validity.typeMismatch)
    return "กรุณากรอกอีเมลให้ครบ เช่น you@example.com";
  if (field.validity.patternMismatch)
    return field.name === "phone"
      ? "กรอกเบอร์โทรเป็นตัวเลข 9 ถึง 10 หลัก"
      : "รูปแบบข้อมูลไม่ถูกต้อง";
  if (field.validity.tooShort)
    return "กรอกอย่างน้อย " + field.minLength + " ตัวอักษร";
  if (field.validity.rangeUnderflow) return "ต้องไม่น้อยกว่า " + field.min;
  if (field.validity.rangeOverflow) return "ต้องไม่เกิน " + field.max;
  if (field.validity.stepMismatch) return "กรอกตัวเลขตามรูปแบบที่ระบุ";
  return "กรุณาตรวจสอบข้อมูลนี้";
}

function showFieldError(field, message) {
  message = message === undefined ? fieldMessage(field) : message;
  var error = document.getElementById(field.dataset.errorId);
  if (error) error.textContent = message;
  if (message) field.setAttribute("aria-invalid", "true");
  else field.removeAttribute("aria-invalid");
  return !message;
}

function validateForm(form) {
  var invalid = null;
  form.querySelectorAll("input,textarea,select").forEach(function (field) {
    if (!field.disabled && field.type !== "hidden" && !showFieldError(field))
      invalid = invalid || field;
  });
  if (invalid) {
    var details = invalid.closest("details");
    if (details) details.open = true;
    if (invalid.type === "radio") {
      showFormError(
        form,
        invalid.name === "tableId"
          ? "กรุณาเลือกโต๊ะที่ว่างหรือรูปแบบการรับอาหาร"
          : "กรุณาเลือกคะแนนก่อนส่งรีวิว",
      );
    }
    invalid.focus();
    return false;
  }
  return true;
}

function showFormError(form, message, errors) {
  var box = form.querySelector(".form-error");
  if (!box) {
    box = document.createElement("div");
    box.className = "form-error";
    box.setAttribute("role", "alert");
    box.tabIndex = -1;
    form.prepend(box);
  }
  box.textContent = message || "กรุณาตรวจสอบข้อมูล";
  if (errors)
    Object.entries(errors).forEach(function (pair) {
      var field = Array.from(form.elements).find(function (x) {
        return x.name.toLowerCase() === pair[0].toLowerCase();
      });
      if (field)
        showFieldError(
          field,
          Array.isArray(pair[1]) ? pair[1].join(" ") : String(pair[1]),
        );
    });
  box.focus();
}

async function submitInline(form, button) {
  if (form.dataset.sending === "true") return;
  var data = new FormData(form);
  if (button && button.name) data.append(button.name, button.value);
  form.dataset.sending = "true";
  form.setAttribute("aria-busy", "true");
  if (button) button.disabled = true;
  try {
    var response = await fetch(form.action, {
      method: "POST",
      body: data,
      headers: { "X-Requested-With": "XMLHttpRequest" },
    });
    var type = response.headers.get("content-type") || "";
    if (type.includes("application/json")) {
      var result = await response.json();
      if (!response.ok) {
        showFormError(form, result.message, result.errors);
        return;
      }
      location.assign(result.redirectUrl || response.url);
      return;
    }
    var html = new DOMParser().parseFromString(
      await response.text(),
      "text/html",
    );
    var error = html.querySelector(".validation-summary-errors,.toast.error");
    if (!response.ok && !error) error = html.querySelector('[role="alert"]');
    if (!response.ok || error) {
      showFormError(
        form,
        error
          ? error.textContent.trim()
          : "ไม่สามารถบันทึกได้ กรุณาตรวจสอบข้อมูล",
      );
      return;
    }
    location.assign(response.url);
  } catch (error) {
    showFormError(
      form,
      "การเชื่อมต่อขัดข้อง ข้อมูลที่กรอกยังอยู่ กรุณาตรวจสอบรายการก่อนลองใหม่",
    );
  } finally {
    delete form.dataset.sending;
    form.removeAttribute("aria-busy");
    if (button) button.disabled = form.dataset.paymentExpired === "true";
  }
}

function flyToCart(form) {
  var source = form.closest(".food-card,.pos-card,.detail-grid");
  var food = source ? source.querySelector("img") : null;
  var cart = document.querySelector(".cart-link");
  if (
    !food ||
    !cart ||
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  )
    return;
  var start = food.getBoundingClientRect();
  var end = cart.getBoundingClientRect();
  var clone = food.cloneNode();
  clone.className = "cart-flight";
  clone.alt = "";
  clone.setAttribute("aria-hidden", "true");
  clone.style.cssText =
    "left:" + start.left + "px;top:" + start.top + "px;width:72px;height:72px";
  document.body.append(clone);
  var animation = clone.animate(
    [
      { transform: "translate(0,0) scale(1)", opacity: 1 },
      {
        transform:
          "translate(" +
          (end.left - start.left) +
          "px," +
          (end.top - start.top) +
          "px) scale(.2)",
        opacity: 0.3,
      },
    ],
    { duration: 700, easing: "cubic-bezier(.3,.1,.3,1)" },
  );
  animation.finished.then(function () {
    clone.remove();
    cart.classList.add("cart-arrived");
    setTimeout(function () {
      cart.classList.remove("cart-arrived");
    }, 400);
  });
}

function initializeSearch() {
  var input = document.querySelector("[data-menu-search]");
  if (!input) return;
  var box = document.getElementById("search-suggestions");
  var timer;
  var request;
  // An old response must not replace suggestions for a newer search.
  var version = 0;
  var status = document.createElement("span");
  status.className = "sr-only";
  status.setAttribute("role", "status");
  input.form.append(status);
  function close() {
    clearTimeout(timer);
    if (request) request.abort();
    version++;
    box.hidden = true;
    input.setAttribute("aria-expanded", "false");
  }
  function loadSuggestions() {
    clearTimeout(timer);
    if (request) request.abort();
    var current = ++version;
    box.replaceChildren();
    var loading = document.createElement("p");
    loading.textContent = "กำลังค้นหาเมนู";
    box.append(loading);
    box.hidden = false;
    input.setAttribute("aria-expanded", "true");
    timer = setTimeout(async function () {
      request = new AbortController();
      try {
        var response = await fetch(
          "/Menu/Suggest?q=" + encodeURIComponent(input.value),
          { signal: request.signal },
        );
        if (!response.ok) throw new Error("Search unavailable");
        var rows = await response.json();
        if (current !== version) return;
        box.replaceChildren();
        rows.forEach(function (row) {
          var a = document.createElement("a");
          a.href = "/Menu/Details/" + row.id;
          var img = document.createElement("img");
          img.src = row.imageUrl;
          img.alt = "";
          var name = document.createElement("strong");
          name.textContent = row.name;
          var price = document.createElement("span");
          price.textContent = "฿" + Number(row.price).toFixed(2);
          a.append(img, name, price);
          box.append(a);
        });
        if (!rows.length) {
          var empty = document.createElement("p");
          empty.textContent = "ยังไม่พบเมนูใกล้เคียง ลองคำอื่น";
          box.append(empty);
        }
        box.hidden = false;
        input.setAttribute("aria-expanded", "true");
        status.textContent = "พบเมนูแนะนำ " + rows.length + " รายการ";
      } catch (error) {
        if (error.name !== "AbortError" && current === version) {
          box.replaceChildren();
          var failed = document.createElement("p");
          failed.textContent = "ค้นหาเมนูแนะนำไม่สำเร็จ กดค้นหาเพื่อดูรายการ";
          box.append(failed);
          status.textContent = failed.textContent;
        }
      }
    }, 180);
  }
  input.addEventListener("input", loadSuggestions);
  input.addEventListener("focus", loadSuggestions);
  input.form.addEventListener("keydown", function (event) {
    var links = Array.from(box.querySelectorAll("a"));
    var index = links.indexOf(document.activeElement);
    if (event.key === "Escape") {
      input.focus();
      close();
    } else if (
      (event.key === "ArrowDown" || event.key === "ArrowUp") &&
      !box.hidden &&
      links.length
    ) {
      event.preventDefault();
      var next;
      if (index < 0) {
        next = event.key === "ArrowDown" ? 0 : links.length - 1;
      } else {
        next = index + (event.key === "ArrowDown" ? 1 : -1);
        // Wrap around when the keyboard moves past either end of the list.
        if (next < 0) next = links.length - 1;
        if (next >= links.length) next = 0;
      }
      links[next].focus();
    }
  });
  document.addEventListener("click", function (event) {
    if (!input.form.contains(event.target)) close();
  });
}
initializeFields();
initializeSearch();

function initializeQuote() {
  var form = document.querySelector("[data-checkout]");
  if (!form) return;
  var promo = form.querySelector("[data-promo]");
  var apply = form.querySelector("[data-apply-promo]");
  var confirm = form.querySelector("[data-confirm-order]");
  var status = form.querySelector("[data-quote-status]");
  // Typing a new code invalidates a quote that is still being fetched.
  var version = 0;
  promo.addEventListener("input", function () {
    version++;
    confirm.disabled = true;
    form.dataset.quotePending = "true";
    status.textContent = "กดตรวจสอบส่วนลดเพื่ออัปเดตยอดก่อนยืนยัน";
  });
  apply.addEventListener("click", async function () {
    var current = ++version;
    form.dataset.quotePending = "true";
    confirm.disabled = true;
    apply.disabled = true;
    status.textContent = "กำลังคำนวณยอด";
    try {
      var response = await fetch(
        "/Cart/Quote?promo=" + encodeURIComponent(promo.value),
      );
      var quote = await response.json();
      if (current !== version) return;
      if (!response.ok) {
        status.textContent = quote.message || "ไม่สามารถใช้ส่วนลดนี้ได้";
        status.classList.add("field-error");
        confirm.disabled = true;
        return;
      }
      var values = {
        subtotal: quote.subtotal,
        discount: quote.discount,
        vat: quote.vatAmount,
        total: quote.total,
      };
      Object.keys(values).forEach(function (key) {
        form.querySelector("[data-quote-" + key + "]").textContent =
          "฿" +
          Number(values[key]).toLocaleString("th-TH", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          });
      });
      var mobileTotal = document.querySelector("[data-mobile-cart-total]");
      if (mobileTotal)
        mobileTotal.textContent =
          form.querySelector("[data-quote-total]").textContent;
      status.textContent = quote.message || "คำนวณส่วนลดและ VAT แล้ว";
      status.classList.remove("field-error");
      delete form.dataset.quotePending;
      confirm.disabled = false;
    } catch (error) {
      status.textContent = "ตรวจสอบยอดไม่สำเร็จ กรุณาลองใหม่";
      confirm.disabled = true;
    } finally {
      apply.disabled = false;
    }
  });
}
initializeQuote();

function initializePayment() {
  var panel = document.querySelector("[data-payment-deadline]");
  if (!panel) return;
  var deadline = Date.parse(panel.dataset.paymentDeadline);
  var form = panel.querySelector("[data-payment-form]");
  var display = panel.querySelector("[data-payment-countdown]");
  var status = panel.querySelector("[data-payment-status]");
  var submit = panel.querySelector("[data-payment-submit]");
  var file = panel.querySelector("[data-slip-input]");
  var preview = panel.querySelector("[data-slip-preview]");
  var previewUrl;

  function updateTime() {
    // Use the deadline because browsers slow down timers in background tabs.
    var remaining = Math.max(0, Math.floor((deadline - Date.now()) / 1000));
    if (!Number.isFinite(remaining)) remaining = 0;
    var minutes = Math.floor(remaining / 60);
    var seconds = remaining % 60;
    display.textContent =
      String(minutes).padStart(2, "0") + ":" + String(seconds).padStart(2, "0");
    if (remaining === 0 && form.dataset.paymentExpired !== "true") {
      form.dataset.paymentExpired = "true";
      submit.disabled = true;
      file.disabled = true;
      status.className = "field-error";
      status.textContent = "หมดเวลาชำระแล้ว กรุณาติดต่อพนักงานที่หน้าร้าน";
    }
  }

  file.addEventListener("change", function () {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    var image = file.files[0];
    preview.parentElement.hidden = true;
    if (!image) return;
    if (
      !["image/jpeg", "image/png", "image/webp"].includes(image.type) ||
      image.size > 5 * 1024 * 1024
    ) {
      showFieldError(file, "เลือกรูป JPEG, PNG หรือ WebP ขนาดไม่เกิน 5 MB");
      file.value = "";
      return;
    }
    previewUrl = URL.createObjectURL(image);
    preview.src = previewUrl;
    preview.parentElement.hidden = false;
    showFieldError(file, "");
  });
  updateTime();
  setInterval(updateTime, 1000);
}
initializePayment();
