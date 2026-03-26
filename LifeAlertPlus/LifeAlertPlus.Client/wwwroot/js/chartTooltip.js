window.chartTooltip = {
    _instances: {},

    init: function (svgEl, id, dataPoints, color, unit, decimals, prefix) {
        if (!svgEl || !dataPoints || !dataPoints.length) return;
        prefix = prefix || '';

        // Clean up any existing tooltip on this element
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
        label.setAttribute('font-family', "'Jua', sans-serif");

        var timeLabel = document.createElementNS(ns, 'text');
        timeLabel.setAttribute('text-anchor', 'middle');
        timeLabel.setAttribute('fill', '#999');
        timeLabel.setAttribute('font-size', '10');
        timeLabel.setAttribute('font-family', "'Jua', sans-serif");

        g.appendChild(vline);
        g.appendChild(bg);
        g.appendChild(label);
        g.appendChild(timeLabel);
        g.appendChild(dot);
        svgEl.appendChild(g);

        function onMove(e) {
            var rect = svgEl.getBoundingClientRect();
            var scaleX = 800 / rect.width;
            var svgX = (e.clientX - rect.left) * scaleX;

            if (svgX < 90 || svgX > 785) {
                g.style.display = 'none';
                return;
            }

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

            var above = nearest.y > 55;
            if (above) {
                label.setAttribute('y', nearest.y - 20);
                timeLabel.setAttribute('y', nearest.y - 8);
            } else {
                label.setAttribute('y', nearest.y + 24);
                timeLabel.setAttribute('y', nearest.y + 36);
            }

            // Measure text to size the background
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
