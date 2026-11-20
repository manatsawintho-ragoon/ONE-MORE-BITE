/* Native scrolling also supports touch and trackpads. */
document.querySelectorAll("[data-carousel]").forEach(function (carousel) {
  var track = carousel.querySelector("[data-carousel-track]");
  var previous = carousel.querySelector("[data-carousel-prev]");
  var next = carousel.querySelector("[data-carousel-next]");
  var status = carousel.querySelector("[data-carousel-status]");
  var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

  function update() {
    var step = track.firstElementChild
      ? track.firstElementChild.getBoundingClientRect().width + 24
      : 1;
    var first = Math.round(track.scrollLeft / step) + 1;
    previous.disabled = track.scrollLeft < 4;
    next.disabled =
      track.scrollLeft + track.clientWidth >= track.scrollWidth - 4;
    status.textContent = "เมนู " + first + " จาก " + track.children.length;
  }
  function move(direction) {
    track.scrollBy({
      left: direction * track.clientWidth,
      behavior: reduceMotion.matches ? "instant" : "smooth",
    });
  }
  previous.addEventListener("click", function () {
    move(-1);
  });
  next.addEventListener("click", function () {
    move(1);
  });
  track.addEventListener("keydown", function (event) {
    if (event.target !== track) return;
    if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
      event.preventDefault();
      move(event.key === "ArrowRight" ? 1 : -1);
    }
  });
  track.addEventListener("scroll", update);
  window.addEventListener("resize", update);
  update();
});

document.querySelectorAll(".desktop-nav a").forEach(function (link) {
  var path = new URL(link.href).pathname;
  var current =
    path === "/"
      ? location.pathname === "/"
      : location.pathname.startsWith(path);
  if (current) link.setAttribute("aria-current", "page");
});

function initializeCompactCart() {
  var form = document.querySelector("[data-checkout]");
  if (!form) return;
  var chooser = form.querySelector("[data-table-option]");
  var choices = Array.from(chooser.querySelectorAll("input[name=tableId]"));
  var label = form.querySelector("[data-service-label]");
  var buttons = form.querySelectorAll("[data-service]");

  function updateChoice() {
    var choice = choices.find(function (input) {
      return input.checked && !input.disabled;
    });
    var dineIn = choice && choice.value !== "";
    var name = choice
      ? choice.closest("label").querySelector(".table-drawing b")
      : null;
    if (!choice) {
      label.textContent = "กรุณาเลือกโต๊ะ";
    } else if (dineIn) {
      label.textContent = "ทานที่ร้าน, " + name.textContent;
    } else {
      label.textContent = "รับกลับบ้าน";
    }
    buttons.forEach(function (button) {
      var selected = false;
      if (choice) {
        var isDineInButton = button.dataset.service === "dinein";
        selected = isDineInButton === Boolean(dineIn);
      }
      button.setAttribute("aria-pressed", String(selected));
    });
  }
  choices.forEach(function (choice) {
    choice.addEventListener("change", updateChoice);
    choice.addEventListener("click", function () {
      chooser.open = false;
      chooser.querySelector("summary").focus({ preventScroll: true });
    });
  });
  buttons.forEach(function (button) {
    button.addEventListener("click", function () {
      if (button.dataset.service === "takeaway") {
        choices.find(function (input) {
          return input.value === "";
        }).checked = true;
        chooser.open = false;
        updateChoice();
      } else {
        var selected = choices.find(function (input) {
          return input.checked && input.value !== "" && !input.disabled;
        });
        if (!selected)
          choices.forEach(function (input) {
            input.checked = false;
          });
        chooser.open = true;
        updateChoice();
        button.setAttribute("aria-pressed", "true");
        var first =
          selected ||
          choices.find(function (input) {
            return input.value !== "" && !input.disabled;
          });
        if (first) first.focus({ preventScroll: true });
      }
    });
  });
  if (
    !choices.some(function (input) {
      return input.checked && !input.disabled;
    })
  )
    chooser.open = true;
  updateChoice();

  var drawer = document.getElementById("cart-items-drawer");
  var open = document.querySelector("[data-open-items]");
  open.addEventListener("click", function () {
    drawer.showModal();
    document.body.classList.add("dialog-open");
  });
  drawer
    .querySelector("[data-close-items]")
    .addEventListener("click", function () {
      drawer.close();
    });
  drawer
    .querySelector("[data-edit-items]")
    .addEventListener("click", function () {
      drawer.close();
      document.getElementById("cart-items").scrollIntoView({ block: "start" });
    });
  drawer.addEventListener("close", function () {
    document.body.classList.remove("dialog-open");
    open.focus({ preventScroll: true });
  });
  drawer.addEventListener("click", function (event) {
    if (event.target !== drawer) return;
    var rect = drawer.getBoundingClientRect();
    if (
      event.clientY < rect.top ||
      event.clientX < rect.left ||
      event.clientX > rect.right
    )
      drawer.close();
  });
}
initializeCompactCart();
