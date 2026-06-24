// Tooltip interactiv pentru graficele SVG desenate manual (linie verticală + punct + etichetă),
// folosit de paginile cu grafice (SelectedMonitored, ViewSelectedMonitored, InviteAcceptPage).
// Construiește elementele SVG ale tooltip-ului o singură dată (init) și le actualizează pe mousemove,
// evitând recrearea DOM-ului la fiecare mișcare a cursorului (performanță).
window.chartTooltip = {
    _instances: {}, // id grafic -> { element, grup SVG tooltip, handlere de eveniment } — permite mai multe grafice independente pe aceeași pagină

    // Atașează tooltip-ul pe elementul SVG dat. dataPoints = array de {x, y, value, label} în coordonate viewBox.
    init: function (svgEl, id, dataPoints, color, unit, decimals, prefix) {
        if (!svgEl || !dataPoints || !dataPoints.length) return;
        prefix = prefix || '';

        // Curățăm orice tooltip anterior pe acest element (re-render Blazor poate apela init din nou)
        if (this._instances[id]) this.dispose(id);

        var ns = 'http://www.w3.org/2000/svg';

        var g = document.createElementNS(ns, 'g');
        g.style.display = 'none';
        g.style.pointerEvents = 'none';

        var vline = document.createElementNS(ns, 'line');
        vline.setAttribute('y1', '15');
        vline.setAttribute('y2', '160');
        vline.setAttribute('stroke', '#ccc');
        vline.setAttribute('stroke-width', '1');
        vline.setAttribute('stroke-dasharray', '4 3');

        var dot = document.createElementNS(ns, 'circle');
        dot.setAttribute('r', '5');
        dot.setAttribute('fill', 'white');
        dot.setAttribute('stroke', color);
        dot.setAttribute('stroke-width', '2.5');

        var bg = document.createElementNS(ns, 'rect');
        bg.setAttribute('rx', '6');
        bg.setAttribute('fill', 'white');
        bg.setAttribute('stroke', '#e0e0e0');
        bg.setAttribute('stroke-width', '1');

        var label = document.createElementNS(ns, 'text');
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('fill', '#333');
        label.setAttribute('font-size', '12');
        label.setAttribute('font-weight', 'bold');
        label.setAttribute('font-family', "Inter, sans-serif");

        var timeLabel = document.createElementNS(ns, 'text');
        timeLabel.setAttribute('text-anchor', 'middle');
        timeLabel.setAttribute('fill', '#999');
        timeLabel.setAttribute('font-size', '10');
        timeLabel.setAttribute('font-family', "Inter, sans-serif");

        g.appendChild(vline);
        g.appendChild(bg);
        g.appendChild(label);
        g.appendChild(timeLabel);
        g.appendChild(dot);
        svgEl.appendChild(g);

        // Handler principal: la mișcarea cursorului, găsește punctul de date cel mai apropiat și actualizează tooltip-ul
        function onMove(e) {
            var rect = svgEl.getBoundingClientRect();
            // Citim viewBox-ul la fiecare hover (nu o singură dată la init), ca matematica să rămână corectă
            // după o schimbare de zoom (lățimea viewBox-ului crește odată cu factorul de zoom).
            var vb = svgEl.viewBox && svgEl.viewBox.baseVal;
            var vbWidth = (vb && vb.width) || 800;
            var rightEdge = vbWidth - 15;
            var scaleX = vbWidth / rect.width; // Conversie din pixeli CSS în coordonate viewBox SVG
            var svgX = (e.clientX - rect.left) * scaleX;

            if (svgX < 90 || svgX > rightEdge) {
                g.style.display = 'none'; // Cursorul e în afara zonei de desen a graficului (axa Y/margini) — ascundem tooltip-ul
                return;
            }

            // Căutare liniară a punctului cel mai apropiat pe axa X (seturile de date sunt mici — sub 200 puncte, nu necesită binary search)
            var nearest = null;
            var minDist = Infinity;
            for (var i = 0; i < dataPoints.length; i++) {
                var d = Math.abs(dataPoints[i].x - svgX);
                if (d < minDist) { minDist = d; nearest = dataPoints[i]; }
            }

            if (!nearest) { g.style.display = 'none'; return; }

            g.style.display = '';

            vline.setAttribute('x1', nearest.x);
            vline.setAttribute('x2', nearest.x);
            dot.setAttribute('cx', nearest.x);
            dot.setAttribute('cy', nearest.y);

            var valStr = prefix + (decimals === 0
                ? Math.round(nearest.value) + ' ' + unit
                : nearest.value.toFixed(decimals) + ' ' + unit);

            label.textContent = valStr;
            label.setAttribute('x', nearest.x);

            timeLabel.textContent = nearest.label;
            timeLabel.setAttribute('x', nearest.x);

            // Dacă punctul e în partea de jos a graficului, afișăm eticheta deasupra lui (și invers) — evită ca tooltip-ul să iasă din SVG
            var above = nearest.y > 55;
            if (above) {
                label.setAttribute('y', nearest.y - 20);
                timeLabel.setAttribute('y', nearest.y - 8);
            } else {
                label.setAttribute('y', nearest.y + 24);
                timeLabel.setAttribute('y', nearest.y + 36);
            }

            // Măsurăm textul randat ca să dimensionăm fundalul (bg) exact cât eticheta, nu o lățime fixă arbitrară
            var lBox = label.getBBox();
            var tBox = timeLabel.getBBox();
            var maxW = Math.max(lBox.width, tBox.width) + 14;
            var bgH = 32;
            var bgY = above ? nearest.y - 36 : nearest.y + 10;

            bg.setAttribute('width', maxW);
            bg.setAttribute('height', bgH);
            bg.setAttribute('x', nearest.x - maxW / 2);
            bg.setAttribute('y', bgY);
        }

        function onLeave() {
            g.style.display = 'none';
        }

        svgEl.addEventListener('mousemove', onMove);
        svgEl.addEventListener('mouseleave', onLeave);
        svgEl.style.cursor = 'crosshair';

        this._instances[id] = { el: svgEl, g: g, onMove: onMove, onLeave: onLeave };
    },

    // Elimină ascultătorii de evenimente și elementele SVG ale tooltip-ului — apelat la dispose componentă Blazor
    // sau înainte de re-inițializare, ca să nu rămână handlere "fantomă" atașate la elemente vechi
    dispose: function (id) {
        var inst = this._instances[id];
        if (!inst) return;
        inst.el.removeEventListener('mousemove', inst.onMove);
        inst.el.removeEventListener('mouseleave', inst.onLeave);
        inst.el.style.cursor = '';
        if (inst.g.parentNode) inst.g.parentNode.removeChild(inst.g);
        delete this._instances[id];
    }
};
