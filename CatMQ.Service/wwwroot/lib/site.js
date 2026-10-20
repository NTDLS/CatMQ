// Replaces {0}, {1}, ... in a localized phrase with the given values.
function fmt(text, ...args) {
    return text.replace(/\{(\d+)\}/g, (match, index) => (args[index] !== undefined ? args[index] : match));
}
