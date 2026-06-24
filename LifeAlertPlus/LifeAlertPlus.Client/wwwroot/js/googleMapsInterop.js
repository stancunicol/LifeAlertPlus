// Interop pentru afișarea Google Maps într-un element DOM, apelat din Blazor via JSRuntime.
// Scriptul Google Maps se încarcă asincron (cheia API vine din backend prin App.razor → initGoogleMaps),
// deci ambele metode fac polling pentru disponibilitatea obiectului global `google.maps` înainte de a desena harta.
window.googleMapsInterop = {
    // Inițializează harta direct pe un element DOM deja referențiat (ex: ElementReference din Blazor)
    initMapOnElement: function (element, lat, lng) {
        try {
            if (!element) return;
            if (lat === undefined || lng === undefined || lat === null || lng === null) {
                element.innerHTML = '<div class="map-placeholder">No coordinates</div>'; // Pacientul nu are coordonate GPS înregistrate
                return;
            }

            var l = parseFloat(lat);
            var g = parseFloat(lng);
            if (isNaN(l) || isNaN(g)) {
                element.innerHTML = '<div class="map-placeholder">Invalid coordinates</div>'; // Date GPS corupte/neparsabile
                return;
            }

            var createMap = function () {
                try {
                    var center = { lat: l, lng: g };
                    var map = new google.maps.Map(element, {
                        center: center,
                        zoom: 15
                    });

                    var marker = new google.maps.Marker({
                        position: center,
                        map: map
                    });

                    // Salvăm referințele pe nodul DOM pentru o eventuală reutilizare ulterioară (ex: actualizare poziție fără a recrea harta)
                    element._googleMap = map;
                    element._googleMarker = marker;
                }
                catch (e) {
                    console.error('googleMapsInterop.createMap error', e);
                }
            };

            if (typeof google !== 'undefined' && google.maps) {
                createMap();
                return;
            }

            // Scriptul Google Maps nu s-a încărcat încă — facem polling la 500ms, timeout ~10 secunde
            var tries = 0;
            var maxTries = 20; // ~10 secunde
            var attempt = function () {
                try {
                    if (typeof google !== 'undefined' && google.maps) {
                        createMap();
                    } else {
                        tries++;
                        if (tries < maxTries) {
                            setTimeout(attempt, 500);
                        } else {
                            element.innerHTML = '<div class="map-placeholder">Google Maps failed to load</div>'; // Timeout — afișăm fallback în loc de hartă goală
                        }
                    }
                } catch (e) {
                    console.error('googleMapsInterop.attempt error', e);
                }
            };

            attempt();
        }
        catch (e) {
            console.error('googleMapsInterop.initMapOnElement error', e);
        }
    },

    // Variantă care caută elementul după ID în DOM — utilă când Blazor randează harta într-un container
    // identificat printr-un id static, fără ElementReference disponibilă imediat
    initMapOnElementById: function (elementId, lat, lng) {
        try {
            if (!elementId) return;
            var element = document.getElementById(elementId);
            if (!element) return;

            var self = this;
            var tries = 0;
            var maxTries = 20; // ~10 secunde

            // Polling separat (nu reutilizează direct initMapOnElement) pentru cazul în care elementul
            // există dar Google Maps nu s-a încărcat încă
            function attempt() {
                try {
                    if (typeof google !== 'undefined' && google.maps) {
                        self.initMapOnElement(element, lat, lng);
                    } else {
                        tries++;
                        if (tries < maxTries) {
                            setTimeout(attempt, 500);
                        } else {
                            element.innerHTML = '<div class="map-placeholder">Google Maps failed to load</div>';
                        }
                    }
                } catch (e) {
                    console.error('googleMapsInterop.attempt error', e);
                }
            }

            attempt();
        }
        catch (e) {
            console.error('googleMapsInterop.initMapOnElementById error', e);
        }
    }
};
