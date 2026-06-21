// Web Push subscription helpers called from Blazor via JSInterop

window.webPushSubscribe = async function (apiBase, authToken) {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
        console.warn('Web Push: serviceWorker or PushManager not supported');
        return false;
    }

    try {
        console.log('Web Push: waiting for service worker...');
        const swReady = Promise.race([
            navigator.serviceWorker.ready,
            new Promise((_, reject) => setTimeout(() => reject(new Error('Service worker not ready after 8s')), 8000))
        ]);
        const reg = await swReady;
        console.log('Web Push: service worker ready', reg);

        // Get VAPID public key from API
        console.log('Web Push: fetching VAPID key from', apiBase);
        const res = await fetch(`${apiBase}/api/push/vapid-public-key`);
        if (!res.ok) {
            console.warn('Web Push: VAPID key fetch failed, status', res.status);
            return false;
        }
        const { publicKey } = await res.json();
        console.log('Web Push: got VAPID public key');

        const sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: urlBase64ToUint8Array(publicKey)
        });
        console.log('Web Push: subscribed', sub.endpoint);

        const keys = sub.toJSON().keys;
        const saveRes = await fetch(`${apiBase}/api/push/subscribe`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
            body: JSON.stringify({ endpoint: sub.endpoint, p256dh: keys.p256dh, auth: keys.auth })
        });
        console.log('Web Push: subscription saved, status', saveRes.status);
        return true;
    } catch (e) {
        console.warn('Web Push subscription failed:', e);
        return false;
    }
};

window.webPushUnsubscribe = async function (apiBase, authToken) {
    if (!('serviceWorker' in navigator)) return;
    try {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        if (!sub) return;
        const keys = sub.toJSON().keys;
        await fetch(`${apiBase}/api/push/subscribe`, {
            method: 'DELETE',
            headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
            body: JSON.stringify({ endpoint: sub.endpoint, p256dh: keys.p256dh, auth: keys.auth })
        });
        await sub.unsubscribe();
    } catch (e) { console.warn('Web Push unsubscribe failed:', e); }
};

window.webPushIsSupported = function () {
    return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
};

window.webPushGetPermission = function () {
    return Notification.permission; // 'default' | 'granted' | 'denied'
};

function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64  = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw     = window.atob(base64);
    return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
}
