(() => {
    "use strict";
    const root = document.querySelector("[data-notifications]");
    if (!root) {
        return;
    }
    const toggle = root.querySelector("[data-notification-toggle]");
    const spriteUrl = toggle.querySelector("use").getAttribute("href").split("#")[0];
    const panel = root.querySelector("#notification-panel");
    const list = root.querySelector("[data-notification-list]");
    const unreadBadge = root.querySelector("[data-notification-unread]");
    const error = root.querySelector("[data-notification-error]");
    const status = root.querySelector("[data-notification-status]");
    const token = root.querySelector("[name='__RequestVerificationToken']").value;
    let timer;
    let loading = false;
    let saving = false;
    let revision = 0;
    let previousUnread = null;
    let previousItems = "";
    for (const badge of document.querySelectorAll("[data-notification-count='refunds']")) {
        badge.dataset.countLabel = root.dataset.refundCountLabel;
        badge.classList.toggle("is-work-count", root.dataset.refundWork === "true");
    }

    function closePanel(restoreFocus) {
        panel.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
        if (restoreFocus) {
            toggle.focus();
        }
    }

    function iconFor(kind) {
        const name = String(kind).toLowerCase();
        if (name.includes("refund")) {
            return "refund";
        }
        if (name.includes("reservation")) {
            return "calendar";
        }
        if (name.includes("order")) {
            return "receipt";
        }
        return "bell";
    }

    function addText(parent, tag, text, className) {
        const element = document.createElement(tag);
        element.textContent = text;
        if (className) {
            element.className = className;
        }
        parent.append(element);
        return element;
    }

    function renderItems(items) {
        const fingerprint = JSON.stringify(items);
        if (fingerprint === previousItems) {
            return;
        }
        // Keep keyboard focus on the same item when a poll changes the list.
        const focusedId = document.activeElement?.dataset.notificationId;
        list.replaceChildren();
        for (const item of items.slice(0, 8)) {
            const link = document.createElement("a");
            const destination = new URL(item.url, window.location.origin);
            if (destination.origin !== window.location.origin) {
                continue;
            }
            link.href = destination.href;
            link.dataset.notificationId = item.id;
            link.className = "notification-item";
            if (!item.isRead) {
                link.classList.add("is-unread");
            }
            const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
            svg.classList.add("icon");
            svg.setAttribute("aria-hidden", "true");
            const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
            use.setAttribute("href", spriteUrl + "#" + iconFor(item.kind));
            svg.append(use);
            link.append(svg);
            const content = addText(link, "span", "", "notification-content");
            addText(content, "strong", item.title);
            addText(content, "span", item.message);
            const date = new Date(item.createdAt);
            const time = addText(content, "time", date.toLocaleString("th-TH", {
                timeZone: "Asia/Bangkok", dateStyle: "short", timeStyle: "short"
            }));
            time.dateTime = date.toISOString();
            addText(content, "span", "ดูรายละเอียด", "notification-action");
            if (!item.isRead) {
                addText(content, "span", "ยังไม่อ่าน", "notification-read-state");
            }
            list.append(link);
        }
        if (items.length === 0) {
            addText(list, "p", "ยังไม่มีการแจ้งเตือน", "notification-empty");
        }
        previousItems = fingerprint;
        if (focusedId) {
            for (const link of list.querySelectorAll("[data-notification-id]")) {
                if (link.dataset.notificationId === focusedId) {
                    link.focus();
                }
            }
        }
    }

    function renderSummary(summary) {
        const unread = summary.unreadCount;
        unreadBadge.hidden = unread === 0;
        unreadBadge.textContent = unread > 99 ? "99+" : String(unread);
        toggle.setAttribute("aria-label", "การแจ้งเตือน ยังไม่อ่าน " + unread + " รายการ");
        for (const badge of document.querySelectorAll("[data-notification-count]")) {
            const count = summary.counts[badge.dataset.notificationCount];
            badge.hidden = count === 0;
            badge.textContent = count > 99 ? "99+" : String(count);
            const label = badge.dataset.countLabel + " " + count + " รายการ";
            badge.setAttribute("aria-label", label);
            badge.title = label;
        }
        if (previousUnread !== unread) {
            status.textContent = "การแจ้งเตือนที่ยังไม่อ่าน " + unread + " รายการ";
        }
        previousUnread = unread;
        renderItems(summary.items);
    }

    function scheduleRefresh() {
        window.clearTimeout(timer);
        if (!document.hidden) {
            timer = window.setTimeout(refresh, 10000);
        }
    }

    async function refresh() {
        window.clearTimeout(timer);
        if (document.hidden || loading || saving) {
            return;
        }
        loading = true;
        const requestRevision = revision;
        try {
            const response = await fetch("/Notifications/Summary", {
                credentials: "same-origin", cache: "no-store", headers: { "Accept": "application/json" }
            });
            if (!response.ok) {
                throw new Error("Summary failed");
            }
            const summary = await response.json();
            if (requestRevision === revision) {
                renderSummary(summary);
                error.hidden = true;
            }
        } catch {
            error.querySelector("span").textContent = "โหลดการแจ้งเตือนไม่สำเร็จ ข้อมูลล่าสุดยังคงแสดงอยู่";
            error.hidden = false;
            if (previousUnread === null) {
                list.replaceChildren();
            }
            status.textContent = "โหลดการแจ้งเตือนไม่สำเร็จ กรุณาลองอีกครั้ง";
        } finally {
            loading = false;
            scheduleRefresh();
        }
    }

    async function markRead(id, destination) {
        if (saving) {
            return;
        }
        saving = true;
        revision += 1;
        const buttons = document.querySelectorAll("[data-notification-read-all]");
        for (const button of buttons) {
            button.disabled = true;
        }
        const body = new URLSearchParams({ "__RequestVerificationToken": token });
        let succeeded = false;
        let endpoint = "/Notifications/ReadAll";
        if (id) {
            endpoint = "/Notifications/Read";
            body.set("id", id);
        }
        try {
            const response = await fetch(endpoint, { method: "POST", credentials: "same-origin", body });
            if (!response.ok) {
                throw new Error("Read failed");
            }
            succeeded = true;
            if (destination) {
                window.location.assign(destination);
                return;
            }
            for (const item of document.querySelectorAll(".notification-history .is-unread")) {
                item.classList.remove("is-unread");
                item.querySelector(".notification-read-state")?.remove();
            }
            const pageError = document.querySelector("[data-notification-page-error]");
            if (pageError) {
                pageError.hidden = true;
            }
        } catch {
            error.hidden = false;
            error.querySelector("span").textContent = "บันทึกการอ่านไม่สำเร็จ กรุณาลองอีกครั้ง";
            const pageError = document.querySelector("[data-notification-page-error]");
            if (pageError) {
                pageError.hidden = false;
            }
            status.textContent = "บันทึกการอ่านไม่สำเร็จ กรุณาลองอีกครั้ง";
        } finally {
            saving = false;
            for (const button of buttons) {
                button.disabled = false;
            }
            scheduleRefresh();
        }
        if (succeeded) {
            await refresh();
        }
    }

    toggle.addEventListener("click", () => {
        const opening = panel.hidden;
        panel.hidden = !opening;
        toggle.setAttribute("aria-expanded", String(opening));
        if (opening) {
            document.querySelector(".account-menu")?.removeAttribute("open");
        }
    });
    document.addEventListener("click", event => {
        if (!root.contains(event.target)) {
            closePanel(false);
        }
        const readAll = event.target.closest("[data-notification-read-all]");
        if (readAll) {
            markRead(null, null);
            return;
        }
        const item = event.target.closest("[data-notification-id]");
        if (item && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) {
            event.preventDefault();
            markRead(item.dataset.notificationId, item.href);
        }
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !panel.hidden) {
            event.preventDefault();
            closePanel(true);
        }
    });
    root.querySelector("[data-notification-retry]").addEventListener("click", refresh);
    document.addEventListener("visibilitychange", () => {
        window.clearTimeout(timer);
        if (!document.hidden) {
            refresh();
        }
    });
    refresh();
})();
