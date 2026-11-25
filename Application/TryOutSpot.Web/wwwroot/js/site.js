// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-copy-text]");
    if (!button) {
        return;
    }

    const text = button.getAttribute("data-copy-text");
    if (!text) {
        return;
    }

    const statusSelector = button.getAttribute("data-copy-status");
    const status = statusSelector ? document.querySelector(statusSelector) : null;

    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
        } else {
            const textarea = document.createElement("textarea");
            textarea.value = text;
            textarea.setAttribute("readonly", "");
            textarea.style.position = "fixed";
            textarea.style.left = "-9999px";
            document.body.appendChild(textarea);
            textarea.select();
            document.execCommand("copy");
            textarea.remove();
        }

        if (status) {
            status.textContent = "Copied.";
        }
    } catch {
        if (status) {
            status.textContent = "Copy failed. Select the link and copy it manually.";
        }
    }
});

document.addEventListener("submit", (event) => {
    const submitter = event.submitter;
    const message = submitter?.getAttribute("data-confirm")
        || event.target.getAttribute("data-confirm");
    if (message && !window.confirm(message)) {
        event.preventDefault();
    }
});

document.addEventListener("change", (event) => {
    const select = event.target.closest("select[data-player-autofill]");
    if (!select) {
        return;
    }

    const option = select.selectedOptions[0];
    const form = select.form;
    if (!option || !form) {
        return;
    }

    const fields = {
        "RegistrationForm.PlayerName": "playerName",
        "RegistrationForm.PlayerBirthDate": "playerBirthdate",
        "RegistrationForm.PlayerSchool": "playerSchool",
        "RegistrationForm.PlayerPhone": "playerPhone",
        "RegistrationForm.PlayerEmail": "playerEmail"
    };
    for (const [name, key] of Object.entries(fields)) {
        const input = form.elements.namedItem(name);
        if (input) {
            input.value = option.dataset[key] || "";
        }
    }
});
