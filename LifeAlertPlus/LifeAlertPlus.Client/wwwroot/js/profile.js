window.triggerProfileImageInput = function (inputId) {
    document.getElementById(inputId).click();
};
// GDPR data portability — triggered by Blazor via JSRuntime
window.downloadFileFromBase64 = function (fileName, mimeType, base64Data) {
    const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(url); document.body.removeChild(a); }, 500);
};
