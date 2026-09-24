// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
console.log("Hello from site.js!");


window.getDimensions = (element) => {
    const rect = element.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height
    };
};

window.argentFormPreview = {
    async render(element, definitionJson) {
        if (!element) {
            throw new Error("The preview host element is unavailable.");
        }
        await Promise.race([
            customElements.whenDefined('argent-form'),
            new Promise((_, reject) => setTimeout(
                () => reject(new Error("The argent-form component did not register within 8 seconds. Check that /js/argent-form.js loaded successfully.")),
                8000))
        ]);
        const definition = JSON.parse(definitionJson);
        element.definition = definition;
        element.values = {};
    }
};
