(() => {
    const searchInput = document.querySelector("[data-checkin-search]");
    const countLabel = document.querySelector("[data-checkin-count]");
    const statusLabel = document.querySelector("[data-checkin-status]");
    const rows = Array.from(document.querySelectorAll("[data-checkin-row]"));

    const updateCount = () => {
        if (!countLabel) {
            return;
        }

        const present = rows.filter((row) => row.classList.contains("is-present")).length;
        countLabel.textContent = `${present} of ${countLabel.dataset.total} checked in`;
    };

    if (searchInput) {
        searchInput.addEventListener("input", () => {
            const term = searchInput.value.trim().toLowerCase();
            rows.forEach((row) => {
                row.hidden = term.length > 0 && !row.dataset.search.includes(term);
            });
        });
    }

    const applyToggleState = (form, isOn) => {
        const kind = form.dataset.checkinToggle;
        const button = form.querySelector("button");
        const nextValueInput = form.querySelector(kind === "attendance" ? "input[name=isPresent]" : "input[name=waiverReceived]");
        button.classList.toggle("is-on", isOn);
        button.setAttribute("aria-pressed", isOn ? "true" : "false");
        nextValueInput.value = isOn ? "false" : "true";
        if (kind === "attendance") {
            button.textContent = isOn ? "Arrived" : "Check in";
            form.closest("tr")?.classList.toggle("is-present", isOn);
            updateCount();
        } else {
            button.textContent = isOn ? "Waiver in" : "Waiver missing";
        }
    };

    document.addEventListener("submit", async (event) => {
        const form = event.target.closest("form[data-checkin-toggle]");
        if (!form || !window.fetch) {
            return;
        }

        event.preventDefault();
        const button = form.querySelector("button");
        button.disabled = true;
        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                headers: { "X-Requested-With": "fetch", Accept: "application/json" },
                credentials: "same-origin"
            });
            const contentType = response.headers.get("content-type") || "";
            if (!response.ok || !contentType.includes("application/json")) {
                throw new Error("Unexpected response");
            }

            const result = await response.json();
            const isOn = form.dataset.checkinToggle === "attendance" ? result.isPresent : result.waiverReceived;
            applyToggleState(form, isOn);
            if (statusLabel) {
                const playerName = form.closest("tr")?.querySelector(".player-name")?.textContent.trim() ?? "Player";
                statusLabel.textContent = form.dataset.checkinToggle === "attendance"
                    ? `${playerName}: ${isOn ? `checked in${result.checkedInAt ? ` at ${result.checkedInAt}` : ""}` : "check-in undone"}`
                    : `${playerName}: waiver ${isOn ? "received" : "marked missing"}`;
            }

            if (searchInput && searchInput.value) {
                searchInput.select();
            }
        } catch {
            form.submit();
        } finally {
            button.disabled = false;
        }
    });
})();
