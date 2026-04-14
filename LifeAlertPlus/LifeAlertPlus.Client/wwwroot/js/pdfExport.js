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

        this._loading = loadScript('https://cdnjs.cloudflare.com/ajax/libs/jspdf/2.5.2/jspdf.umd.min.js')
            .then(() => loadScript('https://cdnjs.cloudflare.com/ajax/libs/jspdf-autotable/3.8.4/jspdf.plugin.autotable.min.js'))
            .then(() => { this._loaded = true; })
            .catch(err => { this._loading = null; throw err; });

        return this._loading;
    },

    generateMedicalReport: async function (data) {
        await this._loadLibraries();

        const { jsPDF } = window.jspdf;
        const doc = new jsPDF('p', 'mm', 'a4');
        const pageWidth = doc.internal.pageSize.getWidth();
        const margin = 15;
        let y = 15;

        // --- Header bar ---
        doc.setFillColor(165, 214, 167); // #A5D6A7
        doc.rect(0, 0, pageWidth, 32, 'F');
        doc.setFontSize(22);
        doc.setTextColor(255, 255, 255);
        doc.setFont('helvetica', 'bold');
        doc.text('Life Alert +', margin, 15);
        doc.setFontSize(11);
        doc.setFont('helvetica', 'normal');
        doc.text(data.reportTitle || 'Medical Report', margin, 23);
        doc.setFontSize(9);
        doc.text(data.generatedAt || '', pageWidth - margin, 23, { align: 'right' });
        y = 40;

        // --- Patient info section ---
        doc.setTextColor(60, 60, 60);
        doc.setFontSize(14);
        doc.setFont('helvetica', 'bold');
        doc.text(data.patientSectionTitle || 'Patient Information', margin, y);
        y += 2;
        doc.setDrawColor(165, 214, 167);
        doc.setLineWidth(0.8);
        doc.line(margin, y, pageWidth - margin, y);
        y += 8;

        doc.setFontSize(10);
        doc.setFont('helvetica', 'normal');

        const patientRows = [
            [data.nameLabel || 'Name', data.patientName || '-'],
            [data.ageLabel || 'Age', data.patientAge || '-'],
            [data.deviceLabel || 'Device', data.deviceSerial || '-'],
            [data.statusLabel || 'Status', data.status || '-'],
            [data.locationLabel || 'Location', data.location || '-'],
            [data.lastUpdateLabel || 'Last Update', data.lastUpdate || '-']
        ];

        doc.autoTable({
            startY: y,
            body: patientRows,
            theme: 'plain',
            margin: { left: margin, right: margin },
            columnStyles: {
                0: { fontStyle: 'bold', cellWidth: 45, textColor: [100, 100, 100] },
                1: { textColor: [40, 40, 40] }
            },
            styles: { fontSize: 10, cellPadding: 2.5 }
        });
        y = doc.lastAutoTable.finalY + 10;

        // --- Current Vitals ---
        doc.setFontSize(14);
        doc.setFont('helvetica', 'bold');
        doc.text(data.vitalsSectionTitle || 'Current Vital Signs', margin, y);
        y += 2;
        doc.line(margin, y, pageWidth - margin, y);
        y += 6;

        // Vitals cards as colored boxes
        const vitals = data.vitals || [];
        const cardWidth = (pageWidth - 2 * margin - 9) / 4; // 4 cards
        const cardHeight = 28;

        vitals.forEach((v, i) => {
            const x = margin + i * (cardWidth + 3);
            // Background
            const colors = {
                'heart': [255, 235, 238],
                'spo2': [227, 242, 253],
                'temp': [255, 243, 224],
                'gps': [232, 245, 233]
            };
            const bg = colors[v.type] || [240, 240, 240];
            doc.setFillColor(bg[0], bg[1], bg[2]);
            doc.roundedRect(x, y, cardWidth, cardHeight, 3, 3, 'F');

            doc.setFontSize(8);
            doc.setFont('helvetica', 'normal');
            doc.setTextColor(120, 120, 120);
            doc.text(v.label || '', x + cardWidth / 2, y + 7, { align: 'center' });

            doc.setFontSize(14);
            doc.setFont('helvetica', 'bold');
            doc.setTextColor(40, 40, 40);
            doc.text(v.value || '-', x + cardWidth / 2, y + 17, { align: 'center' });

            doc.setFontSize(7);
            doc.setFont('helvetica', 'normal');
            const statusColor = v.statusColor === 'good' ? [76, 175, 80]
                : v.statusColor === 'warning' ? [255, 152, 0]
                    : v.statusColor === 'danger' ? [244, 67, 54]
                        : [150, 150, 150];
            doc.setTextColor(statusColor[0], statusColor[1], statusColor[2]);
            doc.text(v.statusText || '', x + cardWidth / 2, y + 24, { align: 'center' });
        });

        y += cardHeight + 10;
        doc.setTextColor(60, 60, 60);

        // --- AI Prediction ---
        if (data.aiPrediction) {
            doc.setFontSize(14);
            doc.setFont('helvetica', 'bold');
            doc.text(data.aiSectionTitle || 'AI Analysis', margin, y);
            y += 2;
            doc.line(margin, y, pageWidth - margin, y);
            y += 8;

            const ai = data.aiPrediction;
            const aiRows = [
                [data.aiStateLabel || 'State', ai.prediction || '-'],
                [data.aiRiskLabel || 'Risk Level', ai.riskLevel || '-'],
                [data.aiConfidenceLabel || 'Confidence', ai.confidence || '-'],
                [data.aiHealthScoreLabel || 'Health Score', ai.healthScore || '-']
            ];

            doc.autoTable({
                startY: y,
                body: aiRows,
                theme: 'plain',
                margin: { left: margin, right: margin },
                columnStyles: {
                    0: { fontStyle: 'bold', cellWidth: 45, textColor: [100, 100, 100] },
                    1: { textColor: [40, 40, 40] }
                },
                styles: { fontSize: 10, cellPadding: 2.5 }
            });
            y = doc.lastAutoTable.finalY + 4;

            if (ai.details) {
                doc.setFontSize(9);
                doc.setFont('helvetica', 'italic');
                doc.setTextColor(80, 80, 80);
                const lines = doc.splitTextToSize(ai.details, pageWidth - 2 * margin);
                doc.text(lines, margin, y);
                y += lines.length * 4.5 + 6;
            }
        }

        // --- Recent Measurements Table ---
        if (data.measurements && data.measurements.length > 0) {
            // Check if we need a new page
            if (y > 230) {
                doc.addPage();
                y = 20;
            }

            doc.setTextColor(60, 60, 60);
            doc.setFontSize(14);
            doc.setFont('helvetica', 'bold');
            doc.text(data.measurementsSectionTitle || 'Recent Measurements', margin, y);
            y += 2;
            doc.line(margin, y, pageWidth - margin, y);
            y += 6;

            const mHead = [[
                data.mDateHeader || 'Date',
                data.mPulseHeader || 'Pulse',
                data.mTempHeader || 'Temperature',
                data.mActivityHeader || 'Activity'
            ]];

            const mBody = data.measurements.map(m => [
                m.date || '-',
                m.pulse || '-',
                m.temperature || '-',
                m.activity || '-'
            ]);

            doc.autoTable({
                startY: y,
                head: mHead,
                body: mBody,
                theme: 'striped',
                margin: { left: margin, right: margin },
                headStyles: {
                    fillColor: [165, 214, 167],
                    textColor: [255, 255, 255],
                    fontStyle: 'bold',
                    fontSize: 9
                },
                bodyStyles: { fontSize: 9 },
                alternateRowStyles: { fillColor: [245, 250, 245] }
            });
            y = doc.lastAutoTable.finalY + 10;
        }

        // --- Footer ---
        const pageCount = doc.internal.getNumberOfPages();
        for (let p = 1; p <= pageCount; p++) {
            doc.setPage(p);
            doc.setFontSize(8);
            doc.setTextColor(160, 160, 160);
            doc.setFont('helvetica', 'normal');
            const pageH = doc.internal.pageSize.getHeight();
            doc.text(data.footerDisclaimer || 'This report is automatically generated and should not replace professional medical evaluation.',
                margin, pageH - 10);
            doc.text(`${p} / ${pageCount}`, pageWidth - margin, pageH - 10, { align: 'right' });
        }

        // Download PDF
        const pdfBlob = doc.output('blob');
        const url = URL.createObjectURL(pdfBlob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'medical-report.pdf';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    }
};

// Preload libraries eagerly so they're ready when user clicks export
try { window.pdfExport._loadLibraries(); } catch(e) { console.warn('PDF lib preload failed:', e); }
