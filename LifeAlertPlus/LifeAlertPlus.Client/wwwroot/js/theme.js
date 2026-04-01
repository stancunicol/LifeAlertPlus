window.theme = (function(){
    const THEME_PREFIX = 'theme-';

    function applyTheme(themeName) {
        try {
            if (!themeName) return;
            // sanitize
            themeName = String(themeName).toLowerCase();

            // remove existing theme-* classes
            document.documentElement.classList.forEach(c => {
                if (c.startsWith(THEME_PREFIX)) document.documentElement.classList.remove(c);
            });

            const themeClass = THEME_PREFIX + themeName;
            document.documentElement.classList.add(themeClass);

            // persist
            try { localStorage.setItem('laf_theme', themeName); } catch (e) { }

            // update meta theme-color for mobile UI
            try {
                var meta = document.querySelector('meta[name="theme-color"]');
                if (!meta) {
                    meta = document.createElement('meta');
                    meta.name = 'theme-color';
                    document.head.appendChild(meta);
                }
                // Map a small set of theme -> color overrides (fallback to computed --accent)
                var colorMap = {
                    'pink': '#ff69b4',
                    'yellow': '#e6c87a',
                    'green': '#8fd08a',
                    'lilac': '#c9a8ff',
                    'blue': '#9fd9ff',
                    'light': '#cfe0ff',
                    'dark': '#0b1220'
                };
                var c = colorMap[themeName] || getComputedStyle(document.documentElement).getPropertyValue('--accent') || '#ff69b4';
                meta.setAttribute('content', c.trim());
            } catch (e) { }
        } catch (e) { console.error('applyTheme error', e); }
    }

    // auto apply stored theme on load
    try {
        var stored = null;
        try { stored = localStorage.getItem('laf_theme'); } catch (e) { }
        if (stored) applyTheme(stored);
    } catch (e) { }

    return {
        applyTheme: applyTheme,
        getAccentColor: function() {
            try {
                var v = getComputedStyle(document.documentElement).getPropertyValue('--accent');
                return (v || '').trim();
            } catch (e) { return ''; }
        }
    };
})();