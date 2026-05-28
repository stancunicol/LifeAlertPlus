// chartSync — keep horizontal scroll position in sync between paired chart wrappers.
// Used by SelectedMonitored to make the HR chart and Temperature chart pan together.
window.chartSync = (function () {
    const registry = new Map(); // key -> { wrappers, handlers, syncing }

    function attach(key, wrappers) {
        if (!Array.isArray(wrappers) || wrappers.length < 2) return;
        detach(key);

        const state = { wrappers, handlers: [], syncing: false };

        wrappers.forEach((source) => {
            if (!source) return;
            const handler = function () {
                if (state.syncing) return;
                state.syncing = true;
                try {
                    const left = source.scrollLeft;
                    state.wrappers.forEach((target) => {
                        if (target && target !== source && target.scrollLeft !== left) {
                            target.scrollLeft = left;
                        }
                    });
                } finally {
                    // Release after the next frame so the mirrored scroll event
                    // (which fires synchronously when we set scrollLeft) doesn't
                    // bounce the scroll back and cancel the user's gesture.
                    requestAnimationFrame(() => { state.syncing = false; });
                }
            };
            source.addEventListener('scroll', handler, { passive: true });
            state.handlers.push({ element: source, handler });
        });

        registry.set(key, state);
    }

    function detach(key) {
        const state = registry.get(key);
        if (!state) return;
        state.handlers.forEach(({ element, handler }) => {
            element.removeEventListener('scroll', handler);
        });
        registry.delete(key);
    }

    return { attach, detach };
})();
