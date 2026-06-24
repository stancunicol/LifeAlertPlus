// chartSync — păstrează poziția de scroll orizontal sincronizată între containere de grafice asociate.
// Folosit de SelectedMonitored pentru ca graficul de puls și cel de temperatură să se deruleze împreună.
window.chartSync = (function () {
    const registry = new Map(); // cheie -> { wrappers, handlers, syncing }

    // Înregistrează un grup de containere care trebuie să se sincronizeze la scroll
    function attach(key, wrappers) {
        if (!Array.isArray(wrappers) || wrappers.length < 2) return;
        detach(key); // Eliminăm orice ascultători vechi pentru aceeași cheie (evită duplicare la re-render)

        const state = { wrappers, handlers: [], syncing: false };

        wrappers.forEach((source) => {
            if (!source) return;
            const handler = function () {
                if (state.syncing) return; // Evită bucle infinite (un scroll declanșat de sincronizare nu mai re-declanșează sincronizarea)
                state.syncing = true;
                try {
                    const left = source.scrollLeft;
                    state.wrappers.forEach((target) => {
                        if (target && target !== source && target.scrollLeft !== left) {
                            target.scrollLeft = left; // Oglindim scroll-ul pe celelalte containere
                        }
                    });
                } finally {
                    // Eliberăm flag-ul abia la următorul frame, ca evenimentul de scroll oglindit
                    // (declanșat sincron când setăm scrollLeft) să nu anuleze gestul utilizatorului.
                    requestAnimationFrame(() => { state.syncing = false; });
                }
            };
            source.addEventListener('scroll', handler, { passive: true });
            state.handlers.push({ element: source, handler });
        });

        registry.set(key, state);
    }

    // Elimină ascultătorii de scroll înregistrați pentru o cheie (apelat la attach repetat sau la dispose componentă)
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
