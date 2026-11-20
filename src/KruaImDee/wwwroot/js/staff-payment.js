document.querySelectorAll("[data-staff-payment]").forEach(function (form) {
  var method = form.querySelector("[name=method]");
  var cash = form.querySelector("[data-staff-cash]");
  var transfer = form.querySelector("[data-staff-transfer]");
  var received = form.querySelector("[name=received]");
  var confirmed = form.querySelector("[name=transferConfirmed]");
  var total = Number(form.dataset.paymentTotal);

  function update() {
    var isTransfer = method.value === "Transfer";
    cash.hidden = isTransfer;
    received.disabled = isTransfer;
    transfer.hidden = !isTransfer;
    confirmed.disabled = !isTransfer;
    if (!isTransfer) confirmed.checked = false;
    var change = Math.max(0, Number(received.value) - total);
    form.querySelector("[data-staff-change]").textContent =
      "฿" +
      change.toLocaleString("th-TH", {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      });
  }
  method.addEventListener("change", update);
  received.addEventListener("input", update);
  update();
});
