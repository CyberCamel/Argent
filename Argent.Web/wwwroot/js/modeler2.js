window.Modeler2 = {
    instances: new Map(),

    init(canvasElement, dotNetRef) {
        if (this.instances.has(canvasElement)) {
            this.destroy(canvasElement);
        }

        const observer = new ResizeObserver((entries) => {
            for (const entry of entries) {
                const rect = entry.contentRect;
                dotNetRef.invokeMethodAsync('OnCanvasResized', rect.width, rect.height);
            }
        });

        observer.observe(canvasElement);

        this.instances.set(canvasElement, { observer, dotNetRef });

        // Initial dimensions
        const rect = canvasElement.getBoundingClientRect();
        dotNetRef.invokeMethodAsync('OnCanvasResized', rect.width, rect.height);
    },

    destroy(canvasElement) {
        const instance = this.instances.get(canvasElement);
        if (instance) {
            instance.observer.disconnect();
            this.instances.delete(canvasElement);
        }
    },

    getDimensions(canvasElement) {
        const rect = canvasElement.getBoundingClientRect();
        return {
            left: rect.left,
            top: rect.top,
            width: rect.width,
            height: rect.height
        };
    }
};
