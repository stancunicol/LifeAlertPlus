window.googleMapsInterop = {
    initMapOnElement: function (element, lat, lng) {
        try {
            if (!element) return;
            if (lat === undefined || lng === undefined || lat === null || lng === null) {
                element.innerHTML = '<div class="map-placeholder">No coordinates</div>';
                return;
            }

            var l = parseFloat(lat);
            var g = parseFloat(lng);
            if (isNaN(l) || isNaN(g)) {
                element.innerHTML = '<div class="map-placeholder">Invalid coordinates</div>';
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

                    // store references in the DOM node for potential future use
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

            // Poll for google.maps availability (retry for a few seconds)
            var tries = 0;
            var maxTries = 20; // ~10 seconds
            var attempt = function () {
                try {
                    if (typeof google !== 'undefined' && google.maps) {
                        createMap();
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
            };

            attempt();
        }
        catch (e) {
            console.error('googleMapsInterop.initMapOnElement error', e);
        }
    },

    initMapOnElementById: function (elementId, lat, lng) {
        try {
            if (!elementId) return;
            var element = document.getElementById(elementId);
            if (!element) return;

            var self = this;
            var tries = 0;
            var maxTries = 20; // try for ~10 seconds

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
