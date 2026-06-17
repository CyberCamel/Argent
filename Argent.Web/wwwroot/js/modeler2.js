// Panel resize via event delegation — no Blazor involvement needed.
(function () {
    document.addEventListener('pointerdown', (e) => {
        const resizer = e.target.closest('.modeler2-resizer[data-resize-prop]');
        if (!resizer) return;

        const layout = resizer.closest('.modeler2-layout');
        if (!layout) return;

        e.preventDefault();

        const prop    = resizer.dataset.resizeProp;
        const axis    = resizer.dataset.resizeAxis || 'x';   // 'x' or 'y'
        const invert  = resizer.dataset.resizeInvert === 'true';
        const minSize = parseFloat(resizer.dataset.resizeMin || '80');

        const startCoord = axis === 'x' ? e.clientX : e.clientY;

        // Read current computed value (strips 'px')
        const rawVal = getComputedStyle(layout).getPropertyValue(prop).trim();
        const startSize = parseFloat(rawVal) || 0;

        document.body.style.userSelect = 'none';
        document.body.style.cursor = axis === 'x' ? 'col-resize' : 'row-resize';

        const onMove = (ev) => {
            const delta = (axis === 'x' ? ev.clientX : ev.clientY) - startCoord;
            const newSize = Math.max(minSize, startSize + (invert ? -delta : delta));
            layout.style.setProperty(prop, newSize + 'px');
        };

        const onUp = () => {
            document.body.style.userSelect = '';
            document.body.style.cursor = '';
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);
        };

        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
    });
})();

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
    },

    syncClocks() {
        const now  = new Date();
        const hDel = (now.getHours() % 12) * 3600 + now.getMinutes() * 60 + now.getSeconds();
        const mDel = now.getMinutes() * 60 + now.getSeconds();
        const sDel = now.getSeconds();

        let style = document.getElementById('modeler2-clock-sync');
        if (!style) {
            style = document.createElement('style');
            style.id = 'modeler2-clock-sync';
            document.head.appendChild(style);
        }
        style.textContent =
            `.modeler2-canvas .clock-hand-hour   { animation-delay: -${hDel}s; }` +
            `.modeler2-canvas .clock-hand-minute { animation-delay: -${mDel}s; }` +
            `.modeler2-canvas .clock-hand-second { animation-delay: -${sDel}s; }`;
    }
};
