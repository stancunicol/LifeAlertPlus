// Funcții helper pentru abonarea la Web Push (notificări push de browser), apelate din Blazor via JSInterop

// Abonează browserul curent la notificări push: înregistrează service worker-ul, cere cheia publică VAPID
// de la backend, creează subscripția push și o salvează în DB prin API
window.webPushSubscribe = async function (apiBase, authToken) {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
        console.warn('Web Push: serviceWorker or PushManager not supported');
        return false;
    }

    try {
        console.log('Web Push: waiting for service worker...');
        // Timeout de 8s — service worker-ul poate întârzia la activare; nu blocăm UI-ul la nesfârșit
        const swReady = Promise.race([
            navigator.serviceWorker.ready,
            new Promise((_, reject) => setTimeout(() => reject(new Error('Service worker not ready after 8s')), 8000))
        ]);
        const reg = await swReady;
        console.log('Web Push: service worker ready', reg);

        // Cerem cheia publică VAPID din API (identifică serverul ca expeditor autorizat de push-uri)
        console.log('Web Push: fetching VAPID key from', apiBase);
        const res = await fetch(`${apiBase}/api/push/vapid-public-key`);
        if (!res.ok) {
            console.warn('Web Push: VAPID key fetch failed, status', res.status);
            return false;
        }
        const { publicKey } = await res.json();
        console.log('Web Push: got VAPID public key');

        // Creăm subscripția push a browserului — generează endpoint unic + cheile de criptare p256dh/auth
        const sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: urlBase64ToUint8Array(publicKey)
        });
        console.log('Web Push: subscribed', sub.endpoint);

        // Trimitem subscripția la backend pentru a fi salvată (PushSubscription) — folosită ulterior la trimiterea alertelor
        const keys = sub.toJSON().keys;
        const saveRes = await fetch(`${apiBase}/api/push/subscribe`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
            body: JSON.stringify({ endpoint: sub.endpoint, p256dh: keys.p256dh, auth: keys.auth })
        });
        console.log('Web Push: subscription saved, status', saveRes.status);
        if (!saveRes.ok) {
            const body = await saveRes.text().catch(() => '');
            console.warn('Web Push: server error saving subscription:', body);
            return false;
        }
        return true;
    } catch (e) {
        console.warn('Web Push subscription failed:', e);
        return false;
    }
};

// Dezabonează browserul curent: șterge subscripția din DB, apoi anulează abonamentul push local
window.webPushUnsubscribe = async function (apiBase, authToken) {
    if (!('serviceWorker' in navigator)) return;
    try {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        if (!sub) return; // Nu există subscripție activă — nimic de dezabonat
        const keys = sub.toJSON().keys;
        await fetch(`${apiBase}/api/push/subscribe`, {
            method: 'DELETE',
            headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
            body: JSON.stringify({ endpoint: sub.endpoint, p256dh: keys.p256dh, auth: keys.auth })
        });
        await sub.unsubscribe();
    } catch (e) { console.warn('Web Push unsubscribe failed:', e); }
};

// Verifică dacă browserul suportă Web Push (service worker + PushManager + Notification API)
window.webPushIsSupported = function () {
    return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
};

// Returnează starea curentă a permisiunii de notificări (fără să o ceară)
window.webPushGetPermission = function () {
    return Notification.permission; // 'default' | 'granted' | 'denied'
};

// Conversie cheie VAPID din Base64 URL-safe în Uint8Array — format cerut de PushManager.subscribe()
function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64  = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw     = window.atob(base64);
    return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
}
