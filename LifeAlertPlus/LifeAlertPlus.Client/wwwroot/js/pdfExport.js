// PDF Export using jsPDF + autoTable (loaded from CDN on demand)
window.pdfExport = {
    _loaded: false,
    _loading: null,

    _loadLibraries: function () {
        if (this._loaded) return Promise.resolve();
        if (this._loading) return this._loading;

        const loadScript = (src) => new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = () => reject(new Error('Failed to load: ' + src));
            document.head.appendChild(s);
        });

        this._loading = loadScript('https://cdn.jsdelivr.net/npm/jspdf@2.5.2/dist/jspdf.umd.min.js')
            .then(() => loadScript('https://cdn.jsdelivr.net/npm/jspdf-autotable@3.8.4/dist/jspdf.plugin.autotable.min.js'))
            .then(() => { this._loaded = true; })
            .catch(err => { this._loading = null; throw err; });

        return this._loading;
    },

    generateMedicalReport: async function (data) {
        await this._loadLibraries();

        function normalizeRo(val) {
            if (typeof val === 'string') {
                return val
                    .replace(/[ăÃ¤]/g, 'a').replace(/[ĂÃ]/g, 'A')
                    .replace(/[â]/g, 'a').replace(/[Â]/g, 'A')
                    .replace(/[î]/g, 'i').replace(/[Î]/g, 'I')
                    .replace(/[șş]/g, 's').replace(/[ȘŞ]/g, 'S')
                    .replace(/[țţ]/g, 't').replace(/[ȚŢ]/g, 'T');
            }
            if (Array.isArray(val)) return val.map(normalizeRo);
            if (val && typeof val === 'object') {
                const out = {};
                for (const k of Object.keys(val)) out[k] = normalizeRo(val[k]);
                return out;
            }
            return val;
        }
        data = normalizeRo(data);

        const { jsPDF } = window.jspdf;
        const doc = new jsPDF('p', 'mm', 'a4');
        const pw = doc.internal.pageSize.getWidth();   // 210
        const ph = doc.internal.pageSize.getHeight();   // 297
        const m = 14;                                    // margin
        const cw = pw - 2 * m;                          // content width
        let y = 0;

        // ── Palette (pale / pastel) ──
        const C = {
            green:   [129, 199, 132],
            greenLt: [241, 248, 241],
            greenMd: [200, 230, 201],
            white:   [255, 255, 255],
            dark:    [80, 80, 85],
            body:    [100, 100, 105],
            muted:   [160, 160, 168],
            bg:      [252, 252, 252],
            amber:   [255, 213, 140],
            amberLt: [255, 251, 237],
            red:     [239, 130, 130],
            redLt:   [255, 243, 243],
            blue:    [130, 177, 255],
            blueLt:  [237, 246, 255],
            orange:  [255, 190, 100],
            orangeLt:[255, 248, 237],
            yellow:  [255, 218, 100],
            yellowLt:[255, 252, 230],
        };

        // ── Helpers ──
        function rgb(c) { doc.setTextColor(c[0], c[1], c[2]); }
        function fill(c) { doc.setFillColor(c[0], c[1], c[2]); }
        function draw(c) { doc.setDrawColor(c[0], c[1], c[2]); }

        function ensureSpace(need) {
            if (y + need > ph - 18) { doc.addPage(); y = 18; }
        }

        function sectionHeader(num, title, accent) {
            ensureSpace(50);
            y += 4;
            const ac = accent || C.green;
            // Number badge
            fill(ac);
            doc.roundedRect(m, y - 4.5, 7, 7, 1.5, 1.5, 'F');
            doc.setFontSize(8);
            doc.setFont('helvetica', 'bold');
            rgb(C.white);
            doc.text(String(num), m + 3.5, y + 0.5, { align: 'center' });
            // Title
            doc.setFontSize(12);
            doc.setFont('helvetica', 'bold');
            rgb(C.dark);
            doc.text(title, m + 10, y + 0.5);
            y += 4;
            // Accent line
            draw(ac);
            doc.setLineWidth(0.6);
            doc.line(m, y, pw - m, y);
            y += 6;
        }

        function drawCard(x, w, h, label, value, sub, bgColor) {
            fill(bgColor || C.bg);
            doc.roundedRect(x, y, w, h, 2.5, 2.5, 'F');
            // Label
            doc.setFontSize(7.5);
            doc.setFont('helvetica', 'normal');
            rgb(C.muted);
            doc.text(label, x + w / 2, y + 6, { align: 'center' });
            // Value
            doc.setFontSize(16);
            doc.setFont('helvetica', 'bold');
            rgb(C.dark);
            doc.text(String(value), x + w / 2, y + 15, { align: 'center' });
            // Sub
            if (sub) {
                doc.setFontSize(7);
                doc.setFont('helvetica', 'normal');
                rgb(C.muted);
                doc.text(sub, x + w / 2, y + 20.5, { align: 'center' });
            }
        }

        // ======================================================================
        //  HEADER
        // ======================================================================
        // Gradient-style header (two rects)
        fill(C.green);
        doc.rect(0, 0, pw, 28, 'F');
        fill([160, 215, 163]);
        doc.rect(0, 28, pw, 4, 'F');

        doc.setFontSize(20);
        doc.setFont('helvetica', 'bold');
        rgb(C.white);
        doc.text('Life Alert +', m, 13);

        doc.setFontSize(10);
        doc.setFont('helvetica', 'normal');
        doc.text(data.reportTitle || 'Medical Report', m, 20);

        doc.setFontSize(8);
        doc.text(data.generatedAt || '', pw - m, 20, { align: 'right' });

        y = 38;

        // ======================================================================
        //  1. PATIENT INFORMATION
        // ======================================================================
        sectionHeader(1, data.patientSectionTitle || 'Patient Information');

        // Two-column patient info with light background
        fill(C.bg);
        doc.roundedRect(m, y, cw, 24, 3, 3, 'F');

        const col1x = m + 5;
        const col2x = m + cw / 2 + 5;
        const rowH = 10;
        let py = y + 7;

        function infoRow(x, label, value) {
            doc.setFontSize(8);
            doc.setFont('helvetica', 'normal');
            rgb(C.muted);
            doc.text(label, x, py);
            doc.setFontSize(10);
            doc.setFont('helvetica', 'bold');
            rgb(C.dark);
            doc.text(value || '-', x + 30, py);
        }

        infoRow(col1x, data.firstNameLabel || 'First Name', data.patientFirstName);
        infoRow(col2x, data.lastNameLabel || 'Last Name', data.patientLastName);
        py += rowH;
        infoRow(col1x, data.ageLabel || 'Age', data.patientAge);
        infoRow(col2x, data.addressLabel || 'Address', data.address);

        y += 28;

        // ======================================================================
        //  2. SELECTED PERIOD
        // ======================================================================
        sectionHeader(2, data.periodSectionTitle || 'Selected Period');

        fill(C.blueLt);
        doc.roundedRect(m, y, cw, 10, 2.5, 2.5, 'F');
        doc.setFontSize(10);
        doc.setFont('helvetica', 'bold');
        rgb(C.blue);
        doc.text(data.period || '-', m + cw / 2, y + 6.5, { align: 'center' });
        y += 14;

        // ======================================================================
        //  3. SUMMARY  (stat cards + table)
        // ======================================================================
        if (data.summary) {
            sectionHeader(3, data.summarySectionTitle || 'Summary');
            const s = data.summary;

            // Total badge
            fill(C.greenLt);
            doc.roundedRect(m, y, cw, 8, 2, 2, 'F');
            doc.setFontSize(9);
            doc.setFont('helvetica', 'bold');
            rgb(C.green);
            doc.text('Total: ' + s.totalMeasurements + ' measurements', m + cw / 2, y + 5.5, { align: 'center' });
            y += 12;

            // Summary table - clean grid
            const summaryHead = [[
                data.hMetric || 'Metric',
                data.hAvg || 'Avg',
                data.hMin || 'Min',
                data.hMax || 'Max',
                data.hStdDev || 'Std Dev'
            ]];
            const summaryBody = [
                [data.hPulse || 'Heart Rate', s.pulseAvg, s.pulseMin, s.pulseMax, s.pulseStdDev],
                [data.hTemp || 'Temperature', s.tempAvg, s.tempMin, s.tempMax, s.tempStdDev],
                [data.hSpo2 || 'SpO2', s.spo2Avg, s.spo2Min, s.spo2Max, s.spo2StdDev]
            ];
            doc.autoTable({
                startY: y,
                head: summaryHead,
                body: summaryBody,
                theme: 'grid',
                margin: { left: m, right: m },
                headStyles: { fillColor: C.green, textColor: C.white, fontStyle: 'bold', fontSize: 9, halign: 'center', cellPadding: 3 },
                bodyStyles: { fontSize: 9, halign: 'center', cellPadding: 3, textColor: C.body },
                columnStyles: { 0: { fontStyle: 'bold', halign: 'left', textColor: C.dark } },
                alternateRowStyles: { fillColor: C.greenLt },
                tableLineColor: [220, 220, 220],
                tableLineWidth: 0.2
            });
            y = doc.lastAutoTable.finalY + 8;
        }

        // ======================================================================
        //  4. WEEKLY BREAKDOWN  — one card per week
        // ======================================================================
        if (data.weeklyBreakdown && data.weeklyBreakdown.length > 0) {
            sectionHeader(4, data.weeklySectionTitle || 'Weekly Breakdown');

            data.weeklyBreakdown.forEach((w, idx) => {
                ensureSpace(32);
                // Week header bar
                fill(C.greenLt);
                doc.roundedRect(m, y, cw, 7, 2, 2, 'F');
                doc.setFontSize(8.5);
                doc.setFont('helvetica', 'bold');
                rgb(C.green);
                doc.text(w.weekLabel + '  (' + w.count + ' records)', m + 4, y + 5);
                y += 9;

                const wHead = [['', 'Avg', 'Min', 'Max', 'Dev']];
                const wBody = [
                    ['Heart Rate', w.pulseAvg, w.pulseMin, w.pulseMax, w.pulseStdDev],
                    ['Temperature', w.tempAvg, w.tempMin, w.tempMax, w.tempStdDev],
                    ['SpO2', w.spo2Avg, w.spo2Min, w.spo2Max, w.spo2StdDev]
                ];
                doc.autoTable({
                    startY: y,
                    head: wHead,
                    body: wBody,
                    theme: 'grid',
                    margin: { left: m + 2, right: m + 2 },
                    headStyles: { fillColor: C.greenMd, textColor: C.white, fontSize: 7.5, halign: 'center', cellPadding: 2 },
                    bodyStyles: { fontSize: 8, halign: 'center', cellPadding: 2, textColor: C.body },
                    columnStyles: { 0: { fontStyle: 'bold', halign: 'left', cellWidth: 30, textColor: C.dark } },
                    alternateRowStyles: { fillColor: [248, 253, 248] },
                    tableLineColor: [230, 230, 230],
                    tableLineWidth: 0.15
                });
                y = doc.lastAutoTable.finalY + 5;
            });
            y += 3;
        }

        // ======================================================================
        //  5. DAILY BREAKDOWN
        // ======================================================================
        if (data.dailyBreakdown && data.dailyBreakdown.length > 0) {
            sectionHeader(5, data.dailySectionTitle || 'Daily Breakdown');

            // Split into 3 sub-tables for readability
            // -- 5a Heart Rate daily
            const dayDates = data.dailyBreakdown.map(d => d.date);
            const dayCount = data.dailyBreakdown.map(d => d.count);
            const hrBody = data.dailyBreakdown.map(d => [d.date, d.count, d.pulseAvg, d.pulseMin, d.pulseMax]);
            const tmpBody = data.dailyBreakdown.map(d => [d.date, d.count, d.tempAvg, d.tempMin, d.tempMax]);
            const spoBody = data.dailyBreakdown.map(d => [d.date, d.count, d.spo2Avg, d.spo2Min, d.spo2Max]);

            function miniTable(label, head, body, accentColor) {
                ensureSpace(20);
                doc.setFontSize(8.5);
                doc.setFont('helvetica', 'bold');
                rgb(accentColor);
                doc.text(label, m + 2, y);
                y += 2;
                doc.autoTable({
                    startY: y,
                    head: [head],
                    body: body,
                    theme: 'grid',
                    margin: { left: m, right: m },
                    headStyles: { fillColor: accentColor, textColor: C.white, fontSize: 7.5, halign: 'center', cellPadding: 2 },
                    bodyStyles: { fontSize: 7.5, halign: 'center', cellPadding: 2, textColor: C.body },
                    columnStyles: { 0: { halign: 'left', fontStyle: 'bold', textColor: C.dark } },
                    alternateRowStyles: { fillColor: C.bg },
                    tableLineColor: [225, 225, 225],
                    tableLineWidth: 0.15
                });
                y = doc.lastAutoTable.finalY + 5;
            }

            miniTable('Heart Rate', ['Date', '#', 'Avg', 'Min', 'Max'], hrBody, [239, 154, 154]);
            miniTable('Temperature', ['Date', '#', 'Avg', 'Min', 'Max'], tmpBody, [255, 190, 100]);
            miniTable('SpO2', ['Date', '#', 'Avg', 'Min', 'Max'], spoBody, C.blue);

            y += 3;
        }

        // ======================================================================
        //  6. ALERTS
        // ======================================================================
        if (data.alerts && data.alerts.length > 0) {
            sectionHeader(6, data.alertsSectionTitle || 'Alerts', C.amber);

            // Count badge
            fill(C.amberLt);
            doc.roundedRect(m, y, cw, 7, 2, 2, 'F');
            doc.setFontSize(8.5);
            doc.setFont('helvetica', 'bold');
            rgb(C.amber);
            doc.text(data.alerts.length + ' alert(s) detected', m + cw / 2, y + 4.8, { align: 'center' });
            y += 10;

            const alertBody = data.alerts.map(a => [a.date, a.reason, a.pulse, a.temperature, a.spo2 || '-']);
            doc.autoTable({
                startY: y,
                head: [['Date', 'Reason', 'HR', 'Temp', 'SpO2']],
                body: alertBody,
                theme: 'grid',
                margin: { left: m, right: m },
                headStyles: { fillColor: C.amber, textColor: [60, 60, 60], fontStyle: 'bold', fontSize: 8, halign: 'center', cellPadding: 2.5 },
                bodyStyles: { fontSize: 8, cellPadding: 2.5, textColor: C.body },
                columnStyles: {
                    0: { cellWidth: 36 },
                    1: { cellWidth: 60, textColor: C.amber, fontStyle: 'bold' }
                },
                alternateRowStyles: { fillColor: C.amberLt },
                tableLineColor: [240, 220, 180],
                tableLineWidth: 0.2
            });
            y = doc.lastAutoTable.finalY + 8;
        }

        // ======================================================================
        //  7. CRITICAL EVENTS  —  heavily highlighted
        // ======================================================================
        if (data.criticals && data.criticals.length > 0) {
            sectionHeader(7, data.criticalsSectionTitle || 'Critical Events', C.red);

            // Red warning banner
            fill(C.red);
            doc.roundedRect(m, y, cw, 9, 2.5, 2.5, 'F');
            doc.setFontSize(10);
            doc.setFont('helvetica', 'bold');
            rgb(C.white);
            doc.text(data.criticals.length + ' critical event(s) detected', m + cw / 2, y + 6, { align: 'center' });
            y += 13;

            // Each critical event as an individual highlighted card
            data.criticals.forEach((c, idx) => {
                ensureSpace(22);
                // Red left border card
                fill(C.redLt);
                doc.roundedRect(m, y, cw, 18, 2, 2, 'F');
                fill(C.red);
                doc.rect(m, y, 3, 18, 'F');
                // Date + reason
                doc.setFontSize(9);
                doc.setFont('helvetica', 'bold');
                rgb(C.red);
                doc.text(c.date, m + 6, y + 6);
                doc.setFontSize(8.5);
                rgb(C.dark);
                doc.text(c.reason, m + 50, y + 6);
                // Values
                doc.setFontSize(8);
                doc.setFont('helvetica', 'normal');
                rgb(C.body);
                doc.text('HR: ' + c.pulse + '   |   Temp: ' + c.temperature + '   |   SpO2: ' + (c.spo2 || '-'), m + 6, y + 13);
                y += 20;
            });
            y += 5;
        }

        // ======================================================================
        //  8. RAW DATA
        // ======================================================================
        if (data.rawData && data.rawData.length > 0) {
            sectionHeader(8, data.rawDataSectionTitle || 'Raw Data');

            const rawBody = data.rawData.map(r => [r.date, r.pulse, r.temperature, r.spo2 || '-', r.activity, r.fall]);
            doc.autoTable({
                startY: y,
                head: [['Date', 'HR', 'Temp', 'SpO2', 'Activity', 'Fall']],
                body: rawBody,
                theme: 'grid',
                margin: { left: m, right: m },
                headStyles: { fillColor: C.green, textColor: C.white, fontStyle: 'bold', fontSize: 8, halign: 'center', cellPadding: 2.5 },
                bodyStyles: { fontSize: 7.5, halign: 'center', cellPadding: 2, textColor: C.body },
                columnStyles: {
                    0: { halign: 'left', cellWidth: 34 },
                    4: { halign: 'left' }
                },
                alternateRowStyles: { fillColor: C.greenLt },
                tableLineColor: [220, 220, 220],
                tableLineWidth: 0.15
            });
            y = doc.lastAutoTable.finalY + 8;
        }

        // ======================================================================
        //  9. INTERPRETATION  — risk score, breakdown, confidence, top concerns, severity items
        // ======================================================================
        if (data.interpretations && data.interpretations.length > 0) {
            sectionHeader(9, data.interpretationSectionTitle || 'Interpretation', C.blue);

            var sevColors = {
                low:    { dot: C.green,  bg: C.greenLt, label: 'LOW' },
                medium: { dot: C.orange, bg: C.orangeLt, label: 'MEDIUM' },
                high:   { dot: C.red,    bg: C.redLt,   label: 'HIGH' }
            };

            // ── Risk Score gauge + confidence ──
            if (data.riskScore !== undefined) {
                ensureSpace(30);
                var rs = data.riskScore;
                var rl = data.riskLevel || 'LOW';
                var gc = rl === 'HIGH' ? C.red : rl === 'MEDIUM' ? C.orange : C.green;
                var gbg = rl === 'HIGH' ? C.redLt : rl === 'MEDIUM' ? C.orangeLt : C.greenLt;

                // Score card
                fill(gbg);
                doc.roundedRect(m, y, cw, 22, 3, 3, 'F');
                // Score text
                doc.setFontSize(12);
                doc.setFont('helvetica', 'bold');
                rgb(gc);
                doc.text((data.riskScoreLabel || 'Risk Score') + ':  ' + rs + ' / 100  (' + rl + ')', m + 7, y + 8);
                // Progress bar
                fill([230, 230, 230]);
                doc.roundedRect(m + 7, y + 13, cw - 14, 5, 2.5, 2.5, 'F');
                var barW = Math.max((rs / 100) * (cw - 14), 3);
                fill(gc);
                doc.roundedRect(m + 7, y + 13, barW, 5, 2.5, 2.5, 'F');
                y += 25;

                // ── Data Confidence badge ──
                if (data.dataConfidence) {
                    ensureSpace(14);
                    var dc = data.dataConfidence;
                    var dcc = dc === 'HIGH' ? C.green : dc === 'MEDIUM' ? C.orange : C.red;
                    var dcbg = dc === 'HIGH' ? C.greenLt : dc === 'MEDIUM' ? C.orangeLt : C.redLt;
                    fill(dcbg);
                    doc.roundedRect(m, y, cw, 10, 2, 2, 'F');
                    doc.setFontSize(8);
                    doc.setFont('helvetica', 'bold');
                    rgb(dcc);
                    doc.text((data.dataConfidenceLabel || 'Data Confidence') + ': ' + dc, m + 5, y + 4.5);
                    if (data.dataConfidenceNote) {
                        doc.setFont('helvetica', 'normal');
                        doc.setFontSize(7.5);
                        rgb(C.muted);
                        doc.text(' — ' + data.dataConfidenceNote, m + 5 + doc.getTextWidth((data.dataConfidenceLabel || 'Data Confidence') + ': ' + dc), y + 4.5);
                    }
                    y += 13;
                }

                // ── Risk Breakdown table ──
                if (data.riskBreakdown && data.riskBreakdown.length > 0) {
                    ensureSpace(14);
                    doc.setFontSize(8.5);
                    doc.setFont('helvetica', 'bold');
                    rgb(C.dark);
                    doc.text(data.riskBreakdownTitle || 'Score Breakdown', m + 2, y);
                    y += 3;
                    var brkBody = data.riskBreakdown.map(function(b) { return [b.factor, '+' + b.points]; });
                    doc.autoTable({
                        startY: y,
                        body: brkBody,
                        theme: 'plain',
                        margin: { left: m + 2, right: m + 2 },
                        columnStyles: {
                            0: { fontSize: 7.5, textColor: C.body, cellPadding: 1.5 },
                            1: { fontSize: 7.5, fontStyle: 'bold', textColor: gc, halign: 'right', cellWidth: 18, cellPadding: 1.5 }
                        },
                        alternateRowStyles: { fillColor: [248, 248, 248] }
                    });
                    y = doc.lastAutoTable.finalY + 5;
                }
            }

            // ── Top Concerns ──
            if (data.topConcerns && data.topConcerns.length > 0) {
                ensureSpace(14);
                doc.setFontSize(9);
                doc.setFont('helvetica', 'bold');
                rgb(C.red);
                doc.text(data.topConcernsTitle || 'Top Concerns', m + 2, y);
                y += 5;
                data.topConcerns.forEach(function(tc) {
                    ensureSpace(12);
                    var tsc = sevColors[tc.severity] || sevColors.high;
                    fill(tsc.bg);
                    doc.roundedRect(m, y, cw, 9, 2, 2, 'F');
                    fill(tsc.dot);
                    doc.rect(m, y, 3, 9, 'F');
                    // Rank circle
                    fill(tsc.dot);
                    doc.circle(m + 8, y + 4.5, 3, 'F');
                    doc.setFontSize(8);
                    doc.setFont('helvetica', 'bold');
                    rgb(C.white);
                    doc.text(String(tc.rank), m + 8, y + 5.5, { align: 'center' });
                    // Text
                    doc.setFontSize(8.5);
                    doc.setFont('helvetica', 'bold');
                    rgb(C.dark);
                    doc.text(tc.text, m + 14, y + 6);
                    y += 11;
                });
                y += 4;
            }

            // ── Interpretation items (dual-layer) ──
            data.interpretations.forEach(function (item) {
                var txt = typeof item === 'string' ? item : item.text;
                var plain = (typeof item === 'object' && item.plain) ? item.plain : '';
                var sev = (typeof item === 'object' && item.severity) ? item.severity : 'low';
                var sc = sevColors[sev] || sevColors.low;

                // Calculate card height with both layers
                var medLines = doc.splitTextToSize(txt, cw - 28);
                var plainLines = plain ? doc.splitTextToSize(plain, cw - 28) : [];
                var cardH = medLines.length * 4.2 + (plainLines.length > 0 ? plainLines.length * 4 + 5 : 0) + 8;
                cardH = Math.max(cardH, 14);

                ensureSpace(cardH + 3);
                // Card bg
                fill(sc.bg);
                doc.roundedRect(m, y, cw, cardH, 2, 2, 'F');
                // Left accent
                fill(sc.dot);
                doc.rect(m, y, 3, cardH, 'F');
                // Severity badge
                fill(sc.dot);
                var bw = Math.max(doc.getTextWidth(sc.label) + 6, 18);
                doc.roundedRect(m + 5, y + 2, bw, 5.5, 1.5, 1.5, 'F');
                doc.setFontSize(6.5);
                doc.setFont('helvetica', 'bold');
                rgb(C.white);
                doc.text(sc.label, m + 5 + bw / 2, y + 5.8, { align: 'center' });
                // Medical text
                doc.setFontSize(8);
                doc.setFont('helvetica', 'normal');
                rgb(C.dark);
                doc.text(medLines, m + 24, y + 5.5);
                // Plain text (simpler explanation)
                if (plainLines.length > 0) {
                    var plainY = y + medLines.length * 4.2 + 7;
                    doc.setFontSize(7.5);
                    doc.setFont('helvetica', 'italic');
                    rgb(C.muted);
                    doc.text(plainLines, m + 24, plainY);
                }
                y += cardH + 2.5;
            });
            y += 5;
        }

        // ======================================================================
        //  10. CONCLUSION  — clinical synthesis
        // ======================================================================
        if (data.conclusion && data.conclusion.length > 0) {
            sectionHeader(10, data.conclusionSectionTitle || 'Conclusion', [129, 199, 132]);

            // Box color based on risk level
            var rl = data.riskLevel || 'LOW';
            var boxBg = rl === 'HIGH' ? C.redLt : rl === 'MEDIUM' ? C.orangeLt : C.greenLt;
            var boxAccent = rl === 'HIGH' ? C.red : rl === 'MEDIUM' ? C.orange : C.green;

            ensureSpace(20);
            var allText = data.conclusion.join('\n\n');
            var cLines = doc.splitTextToSize(allText, cw - 12);
            var boxH = cLines.length * 4.5 + 12;
            ensureSpace(boxH);
            fill(boxBg);
            doc.roundedRect(m, y, cw, boxH, 3, 3, 'F');
            // Left accent
            fill(boxAccent);
            doc.rect(m, y, 3, boxH, 'F');
            doc.setFontSize(9);
            doc.setFont('helvetica', 'normal');
            rgb(C.dark);
            doc.text(cLines, m + 8, y + 7);
            y += boxH + 8;
        }

        // ======================================================================
        //  FOOTER — on every page
        // ======================================================================
        const pageCount = doc.internal.getNumberOfPages();
        for (let p = 1; p <= pageCount; p++) {
            doc.setPage(p);
            const pH = doc.internal.pageSize.getHeight();
            // Subtle line
            draw([220, 220, 220]);
            doc.setLineWidth(0.3);
            doc.line(m, pH - 14, pw - m, pH - 14);
            // Disclaimer
            doc.setFontSize(7);
            doc.setFont('helvetica', 'italic');
            rgb(C.muted);
            doc.text(data.footerDisclaimer || '', m, pH - 9);
            // Page number
            doc.setFont('helvetica', 'normal');
            doc.text('Page ' + p + ' / ' + pageCount, pw - m, pH - 9, { align: 'right' });
            // Brand watermark
            doc.setFontSize(7);
            rgb([210, 210, 210]);
            doc.text('Life Alert +', pw - m, pH - 5, { align: 'right' });
        }

        // Preview
        const pdfBlob = doc.output('blob');
        this._lastPdfBlob = pdfBlob;
        const url = URL.createObjectURL(pdfBlob);
        this._showPreview(url, data.patientName || 'patient');
    },

    _lastPdfBlob: null,

    getPdfBase64: async function () {
        if (!this._lastPdfBlob) return null;
        const buf = await this._lastPdfBlob.arrayBuffer();
        const bytes = new Uint8Array(buf);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary);
    },

    _showPreview: function (blobUrl, patientName) {
        // Remove existing overlay if any
        const existing = document.getElementById('pdf-preview-overlay');
        if (existing) existing.remove();

        const overlay = document.createElement('div');
        overlay.id = 'pdf-preview-overlay';
        overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.7);z-index:99999;display:flex;flex-direction:column;align-items:center;justify-content:center;';

        const toolbar = document.createElement('div');
        toolbar.style.cssText = 'display:flex;gap:12px;margin-bottom:10px;';

        const btnStyle = 'padding:10px 24px;border:none;border-radius:8px;font-size:14px;font-weight:600;cursor:pointer;';

        const downloadBtn = document.createElement('button');
        downloadBtn.textContent = '⬇ Download';
        downloadBtn.style.cssText = btnStyle + 'background:#4CAF50;color:#fff;';
        downloadBtn.onclick = function () {
            const a = document.createElement('a');
            a.href = blobUrl;
            a.download = 'medical-report-' + patientName.replace(/\s+/g, '-') + '.pdf';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        };

        const emailBtn = document.createElement('button');
        emailBtn.textContent = '📧 Email to Doctor';
        emailBtn.style.cssText = btnStyle + 'background:#2196F3;color:#fff;';
        emailBtn.onclick = function () {
            overlay.remove();
            URL.revokeObjectURL(blobUrl);
            DotNet.invokeMethodAsync('LifeAlertPlus.Client', 'OpenEmailModal');
        };

        const closeBtn = document.createElement('button');
        closeBtn.textContent = '✕ Close';
        closeBtn.style.cssText = btnStyle + 'background:#f44336;color:#fff;';
        closeBtn.onclick = function () {
            overlay.remove();
            URL.revokeObjectURL(blobUrl);
        };

        toolbar.appendChild(downloadBtn);
        toolbar.appendChild(emailBtn);
        toolbar.appendChild(closeBtn);

        const iframe = document.createElement('iframe');
        iframe.src = blobUrl;
        iframe.style.cssText = 'width:90%;max-width:900px;height:85vh;border:none;border-radius:8px;background:#fff;box-shadow:0 8px 32px rgba(0,0,0,0.4);';

        overlay.appendChild(toolbar);
        overlay.appendChild(iframe);

        // Close on background click
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                overlay.remove();
                URL.revokeObjectURL(blobUrl);
            }
        });

        document.body.appendChild(overlay);
    }
};

