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
