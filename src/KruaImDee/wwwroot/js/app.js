var drawer = document.getElementById("navigation-drawer");
var drawerButton = document.querySelector("[data-open-drawer]");
var accountMenu = document.querySelector(".account-menu");
var confirmDialog = document.getElementById("confirm-dialog");
var pendingForm = null;
var pendingButton = null;
// Send cart changes one at a time so rapid clicks preserve their order.
var cartQueue = Promise.resolve();
var feedback = document.getElementById("cart-feedback");
var feedbackTimer = null;
var reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

function openDrawer() {
  drawer.showModal();
  drawerButton.setAttribute("aria-expanded", "true");
  document.body.classList.add("dialog-open");
}

function closeDrawer() {
  drawer.close();
}

function drawerClosed() {
  drawerButton.setAttribute("aria-expanded", "false");
  document.body.classList.remove("dialog-open");
  drawerButton.focus();
}

function closeAccount() {
  if (accountMenu) accountMenu.open = false;
}

function handleOutsideClick(event) {
  if (accountMenu && !accountMenu.contains(event.target)) closeAccount();
  if (event.target === drawer) {
    var box = drawer.getBoundingClientRect();
    if (
      event.clientX < box.left ||
      event.clientX > box.right ||
      event.clientY < box.top ||
      event.clientY > box.bottom
    )
      closeDrawer();
  }
}

function handleKeyboard(event) {
  if (event.key === "Escape" && accountMenu && accountMenu.open) {
    closeAccount();
    accountMenu.querySelector("summary").focus();
  }
  if (
    event.key === "ArrowDown" &&
    event.target.matches("[data-account-toggle]")
  ) {
    event.preventDefault();
    accountMenu.open = true;
    accountMenu.querySelector("a").focus();
  }
}

function confirmMessage(form) {
  var button = form.querySelector("[data-confirm]");
  if (button) return button.getAttribute("data-confirm");
  var path = new URL(form.action).pathname;
  if (path === "/Manage/Deactivate")
    return "ปิดใช้งานรายการนี้หรือไม่ ข้อมูลเดิมจะยังอยู่ในประวัติ";
  if (path === "/Reservations/Cancel") return "ยกเลิกการจองนี้หรือไม่";
  if (path === "/Staff/Void")
    return "ยืนยันยกเลิกออเดอร์และบันทึกเหตุผลหรือไม่";
  return "";
}

function showConfirmation(form, button, message) {
  pendingForm = form;
  pendingButton = button;
  document.getElementById("confirm-message").textContent = message;
  confirmDialog.showModal();
  document.body.classList.add("dialog-open");
}

function cancelConfirmation() {
  confirmDialog.close();
}

function acceptConfirmation() {
  var form = pendingForm;
  var button = pendingButton;
  confirmDialog.close();
  if (!form) return;
  form.dataset.confirmed = "true";
  if (button) form.requestSubmit(button);
  else form.requestSubmit();
}

function confirmationClosed() {
  document.body.classList.remove("dialog-open");
  if (pendingButton) pendingButton.focus();
  pendingForm = null;
  pendingButton = null;
}

function showFeedback(message, isError) {
  clearTimeout(feedbackTimer);
  feedback.querySelector("[data-feedback-message]").textContent = message;
  feedback.classList.toggle("is-error", isError);
  feedback.hidden = false;
  feedback.setAttribute("role", isError ? "alert" : "status");
  var icon = feedback.querySelector(".feedback-icon use");
  icon.setAttribute(
    "href",
    "/images/icons.svg#" + (isError ? "alert" : "check"),
  );
  if (!isError) feedbackTimer = setTimeout(hideFeedback, 7000);
}

function hideFeedback() {
  feedback.hidden = true;
}

function setCartCount(count) {
  var counters = document.querySelectorAll("[data-cart-count]");
  for (var counter of counters) {
    counter.textContent = count;
    counter.classList.remove("count-changed");
    // Force a layout read so adding the class restarts the CSS animation.
    void counter.offsetWidth;
    counter.classList.add("count-changed");
  }
}

async function addToCart(form, button, data, oldLabel) {
  var controller = new AbortController();
  var timeout = setTimeout(function () {
    controller.abort();
  }, 15000);
  try {
    var response = await fetch(form.action, {
      method: "POST",
      body: data,
      headers: { "X-Requested-With": "fetch" },
      signal: controller.signal,
    });
    if (!response.ok) {
      var message = "เพิ่มอาหารไม่สำเร็จ กรุณาตรวจสอบรายการและลองใหม่";
      var html = new DOMParser().parseFromString(
        await response.text(),
        "text/html",
      );
      var alert = html.querySelector('[role="alert"]');
      if (alert) message = alert.textContent;
      showFeedback(message, true);
      return;
    }
    var result = await response.json();
    setCartCount(result.count);
    document.getElementById("cart-status").textContent = result.message;
    flyToCart(form);
  } catch (error) {
    showFeedback("การเชื่อมต่อขัดข้อง กรุณาตรวจสอบตะกร้าก่อนลองใหม่", true);
  } finally {
    clearTimeout(timeout);
    form.removeAttribute("aria-busy");
    button.disabled = false;
    button.textContent = oldLabel;
  }
}

function handleSubmit(event) {
  var form = event.target;
  if (form.matches("[data-pos-form]")) return;
  var button = event.submitter;
  if (form.dataset.paymentExpired === "true") {
    event.preventDefault();
    showFormError(form, "หมดเวลาชำระแล้ว กรุณาติดต่อพนักงาน");
    return;
  }
  if (form.dataset.quotePending === "true") {
    event.preventDefault();
    showFormError(form, "กรุณาตรวจสอบส่วนลดและยอดรวมก่อนยืนยัน");
    return;
  }
  if (!validateForm(form)) {
    event.preventDefault();
    return;
  }
  var message = confirmMessage(form);
  if (message && form.dataset.confirmed !== "true") {
    event.preventDefault();
    showConfirmation(form, button, message);
    return;
  }
  delete form.dataset.confirmed;
  if (new URL(form.action).pathname === "/Cart/Add" && window.fetch && button) {
    event.preventDefault();
    var data = new FormData(form);
    var label = button.textContent;
    button.disabled = true;
    button.textContent = "กำลังเพิ่ม";
    form.setAttribute("aria-busy", "true");
    cartQueue = cartQueue.then(function () {
      return addToCart(form, button, data, label);
    });
    return;
  }
  // A payment input named "method" hides the form.method property.
  var formMethod = form.getAttribute("method") || "get";
  if (formMethod.toLowerCase() === "post" && window.fetch) {
    event.preventDefault();
    submitInline(form, button);
    return;
  }
  document.body.classList.add("page-busy");
}

function initializeImages() {
  var images = document.querySelectorAll(".food-photo img,.hero-image img");
  for (var image of images) {
    if (!image.complete) {
      image.parentElement.classList.add("image-loading");
      image.addEventListener("load", imageFinished, { once: true });
      image.addEventListener("error", imageFinished, { once: true });
    }
  }
}

function imageFinished(event) {
  event.target.parentElement.classList.remove("image-loading");
}

function revealSections(entries, observer) {
  for (var entry of entries) {
    if (entry.isIntersecting) {
      entry.target.classList.remove("await-reveal");
      observer.unobserve(entry.target);
    }
  }
}

function initializeMotion() {
  if (reducedMotion.matches || !window.IntersectionObserver) return;
  var observer = new IntersectionObserver(revealSections, { threshold: 0.08 });
  for (var section of document.querySelectorAll(".reveal")) {
    section.classList.add("await-reveal");
    observer.observe(section);
  }
}

async function refreshKitchen() {
  var grid = document.querySelector("[data-refresh]");
  var active = document.activeElement;
  if (
    !grid ||
    document.hidden ||
    document.querySelector("dialog[open]") ||
    active.matches("input,select,textarea,button")
  )
    return;
  try {
    var response = await fetch(location.href);
    if (!response.ok) return;
    var next = new DOMParser().parseFromString(
      await response.text(),
      "text/html",
    );
    var nextGrid = next.querySelector("[data-refresh]");
    if (nextGrid) grid.replaceWith(nextGrid);
  } catch (error) {
    // Keep the current kitchen queue when the network is unavailable.
  }
}

drawerButton.addEventListener("click", openDrawer);
drawer
  .querySelector("[data-close-drawer]")
  .addEventListener("click", closeDrawer);
drawer.addEventListener("close", drawerClosed);
confirmDialog
  .querySelector("[data-cancel-confirm]")
  .addEventListener("click", cancelConfirmation);
confirmDialog
  .querySelector("[data-accept-confirm]")
  .addEventListener("click", acceptConfirmation);
confirmDialog.addEventListener("close", confirmationClosed);
feedback
  .querySelector("[data-dismiss-feedback]")
  .addEventListener("click", hideFeedback);
document.addEventListener("click", handleOutsideClick);
document.addEventListener("keydown", handleKeyboard);
document.addEventListener("submit", handleSubmit);
window.addEventListener("pageshow", function () {
  document.body.classList.remove("page-busy");
});
for (var link of document.querySelectorAll(".sidebar>a")) {
  if (new URL(link.href).pathname === location.pathname)
    link.classList.add("active");
}
initializeImages();
initializeMotion();
if (document.querySelector("[data-refresh]")) setInterval(refreshKitchen, 5000);
