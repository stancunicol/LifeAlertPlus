// Declanșează click pe input-ul de fișier ascuns (folosit pentru upload poză de profil) — apelat din Blazor via JSRuntime
window.triggerProfileImageInput = function (inputId) {
    document.getElementById(inputId).click();
};

// Portabilitate date GDPR — descarcă un fișier primit ca Base64 din backend (ex: export date utilizator)
// Apelat din Blazor via JSRuntime, fără rotund-trip pe server pentru salvarea fișierului
window.downloadFileFromBase64 = function (fileName, mimeType, base64Data) {
    const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0)); // Decodare Base64 → bytes brute
    const blob = new Blob([bytes], { type: mimeType });
    const url = URL.createObjectURL(blob); // URL temporar în memorie pentru blob
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click(); // Declanșează descărcarea în browser
    setTimeout(() => { URL.revokeObjectURL(url); document.body.removeChild(a); }, 500); // Curățare după descărcare
};
