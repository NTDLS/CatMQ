// Replaces {0}, {1}, ... in a localized phrase with the given values.
function fmt(text, ...args) {
    return text.replace(/\{(\d+)\}/g, (match, index) => (args[index] !== undefined ? args[index] : match));
}

// Color theme: "auto" follows the browser (prefers-color-scheme), otherwise "light" or "dark" is the user's explicit choice.
const themeStorageKey = "catmq-theme";
const themeMediaQuery = window.matchMedia("(prefers-color-scheme: dark)");

function getThemePreference() {
    try {
        const stored = localStorage.getItem(themeStorageKey);
        if (stored === "light" || stored === "dark") return stored;
    } catch (e) { /* Storage can be blocked; fall back to following the browser. */ }
    return "auto";
}

function getEffectiveTheme(preference) {
    return preference === "auto" ? (themeMediaQuery.matches ? "dark" : "light") : preference;
}

function applyTheme() {
    const preference = getThemePreference();
    const theme = getEffectiveTheme(preference);
    document.documentElement.setAttribute("data-bs-theme", theme);

    // Reflect the choice in the toggle (it may not exist yet when this first runs from the head).
    document.querySelectorAll("[data-theme-choice]").forEach(el => {
        const active = el.getAttribute("data-theme-choice") === preference;
        el.classList.toggle("active", active);
        el.setAttribute("aria-pressed", active ? "true" : "false");
    });
    const icon = document.getElementById("themeToggleIcon");
    if (icon) {
        icon.className = "bi " + (preference === "auto" ? "bi-circle-half" : theme === "dark" ? "bi-moon-stars-fill" : "bi-sun-fill");
    }

    document.dispatchEvent(new CustomEvent("themechange", { detail: { theme: theme, preference: preference } }));
}

function setThemePreference(preference) {
    try {
        if (preference === "auto") localStorage.removeItem(themeStorageKey);
        else localStorage.setItem(themeStorageKey, preference);
    } catch (e) { /* Not persisted, but still applied for this page. */ }
    applyTheme();
}

// Apply right away (this file is loaded in the head) so the page never flashes the wrong theme.
document.documentElement.setAttribute("data-bs-theme", getEffectiveTheme(getThemePreference()));

// Follow the browser's setting while the preference is "auto".
themeMediaQuery.addEventListener("change", () => { if (getThemePreference() === "auto") applyTheme(); });

// Keep other tabs in step.
window.addEventListener("storage", e => { if (e.key === themeStorageKey) applyTheme(); });

document.addEventListener("DOMContentLoaded", applyTheme);