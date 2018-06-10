const entityMap = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
    '/': '&#x2F;',
    '`': '&#x60;',
    '=': '&#x3D;'
};
function escapeHtml(string) {
    return String(string).replace(/[&<>"'`=\/]/g, function (s) {
        return entityMap[s];
    });
}

$(document).ready(function () {
    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(function (position) {
            initialLocation = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
            map.setCenter(initialLocation);
        });
    } 

    var options = {};
    var markers = [];
    var refreshInterval = null;
    var timeOut = null;
    var tripStops = {};
    var tripStopSigs = {};
    var availStops = new Set();
    var stopMarkers = [];
    var userMarker = null;
    var currentRadio = null;

    setInterval(function () {
        tripStops = {};
        tripStopSigs = {};
        availStops.clear();
    }, 1000 * 3600 * 24);

    document.getElementById('searchRoute').onclick = function () {
        event.preventDefault();
        for (let op in options)
            if (options.hasOwnProperty(op)) {
                let elem = document.getElementById(op);
                if (elem)
                    elem.parentElement.removeChild(elem);
            }
        options = {};
        getRoute();
    }

    document.getElementById('submit').onclick = function () {
        event.preventDefault();
        getLoc(true);
        refreshLoc();
    };

    document.getElementById("route").onfocus = function () {
        document.getElementById("directionForm").classList.add("fade");
        document.getElementById("map").classList.add("fade");
    };

    document.getElementById("route").onblur = function () {
        document.getElementById("directionForm").classList.remove("fade");
        document.getElementById("map").classList.remove("fade");
    };

    function busIcon() {
        var size = Math.min($(document).width(), $(document).height()) * 0.04;
        return {
            url: "/images/Bus.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size / 2, -size / 8)
        };
    }

    function userIcon() {
        var size = Math.min($(document).width(), $(document).height()) * 0.02;
        return {
            url: "/images/crosshair.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size * 0.75, -size / 6)
        };
    }

    function stopIcon() {
        var size = Math.min($(document).width(), $(document).height()) * 0.015;
        return {
            url: "/images/stop.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 3, size / 3)
        };
    }

    function refreshLoc() {
        refreshInterval = setInterval(function () {
            if (document.getElementById('map').classList.contains('hidden')) {
                if (refreshInterval)
                    clearInterval(refreshInterval);
                return;
            }
            else
                getLoc();
        }, 15000);
        if (timeOut)
            clearTimeout(timeOut);
        timeOut = setTimeout(function () {
            if (refreshInterval)
                clearInterval(refreshInterval);
        }, 600 * 1000);
    }

    function getRoute() {
        var routeElem = document.getElementById('route');
        var route = routeElem.value.trim();
        if (route != '') {
            document.getElementById('searchRoute').disabled = true;
            document.getElementById('map').classList.add('hidden');
            $.ajax({
                type: "GET",
                url: tripUrl,
                data: {
                    route: route
                },
                success: function (data) {
                    renderTrip(data);
                },
                error: function (msg) {
                    console.log(msg);
                },
                complete: function () {
                    document.getElementById('searchRoute').disabled = false;
                    waitingUpd = false;
                }
            });
        }
    }

    function getLoc(recenter = false) {
        let tripArr = null;
        for (let op in options)
            if (options.hasOwnProperty(op)) {
                var radID = options[op].radID;
                let elem = document.getElementById(radID);
                if (elem && elem.checked) {
                    tripArr = options[op].trips;
                    break;
                }
            }
        if (tripArr !== null && tripArr.length !== 0) {
            let tripStrArr = [];
            let tripsToUpd = [];
            for (let i = 0; i < tripArr.length; i++) {
                tripStrArr.push(tripArr[i].toString());
                if (!tripStops.hasOwnProperty(tripArr[i]))
                    tripsToUpd.push(tripArr[i]);
            }
            getStops(tripsToUpd, tripArr);
            let data = {
                tripIDs: tripStrArr.join(',')
            }
            if (recenter)
                document.getElementById('submit').disabled = true;
            $.ajax({
                type: "GET",
                url: locUrl,
                data: data,
                cache: false,
                success: function (data) {
                    renderMarkers(data, recenter);
                },
                error: function (msg) {
                    console.log(msg);
                },
                complete: function () {
                    document.getElementById('submit').disabled = false;
                }
            });
        }
    }

    function getStops(tripIDToUpd, tripArr) {
        var tripsToUpdStr = [];
        for (let i = 0; i < tripIDToUpd.length; i++) {
            tripsToUpdStr.push(tripIDToUpd[i].toString());
        }
        $.ajax({
            type: "GET",
            url: stopUrl,
            data: {
                tripIDs: tripsToUpdStr.join(',')
            },
            success: function (data) {
                if (!data || data.length === 0)
                    return;
                for (let trip in data)
                    if (data.hasOwnProperty(trip)) {
                        var coord = data[trip];
                        if (!tripStops.hasOwnProperty(trip))
                            tripStops[trip] = [];
                        if (!tripStopSigs.hasOwnProperty(trip))
                            tripStopSigs[trip] = new Set();
                        for (let i = 0; i < coord.length; i++) {
                            var coordSig = coord[i].latitude.toString() + "&" + coord[i].longitude.toString();
                            if (!tripStopSigs[trip].has(coordSig)) {
                                tripStops[trip].push(coord[i]);
                                tripStopSigs[trip].add(coordSig);
                            }
                        }
                    }
                renderStops(tripArr);
            },
            error: function (msg) {
                console.log(msg);
            }
        });
    }

    function renderStops(tripIDs) {
        for (let i = 0; i < stopMarkers.length; i++)
            stopMarkers[i].setMap(null);
        stopMarkers.length = 0;
        if (!tripIDs || tripIDs.length === 0)
            return;
        let usedStops = new Set();
        for (let i = 0; i < tripIDs.length; i++)
            if (tripStops.hasOwnProperty(tripIDs[i]) && tripStops[tripIDs[i]]) {
                var stops = tripStops[tripIDs[i]];
                for (let j = 0; j < stops.length; j++) {
                    let key = stops[j].latitude.toString() + "&" + stops[j].longitude.toString();
                    if (!usedStops.has(key)) {
                        let marker = new google.maps.Marker({
                            position: new google.maps.LatLng(stops[j].latitude, stops[j].longitude),
                            icon: stopIcon(),
                            map: map
                        });
                        stopMarkers.push(marker);
                        usedStops.add(key);
                    }
                }
            }
    }

    function renderTrip(data) {
        if (!data)
            return;
        let isEmpty = true;
        for (let trip in data)
            if (data.hasOwnProperty(trip)) {
                isEmpty = false;
                break;
            }
        if (isEmpty) {
            document.getElementById('directionForm').classList.add('hidden');
            return;
        }
        document.getElementById('directionForm').classList.remove('hidden');
        let count = 0;
        for (let trip in data)
            if (data.hasOwnProperty(trip)) {
                let lbl = document.createElement('label');
                lbl.classList.add("form-control");
                let radID = "directionRadio" + count.toString();
                let html = '<input type="radio" name="direction" id="' + radID + '"/> ' + escapeHtml(trip);
                if (count === 0)
                    html = '<input type="radio" name="direction" id="' + radID + '" checked/> ' + escapeHtml(trip);
                lbl.innerHTML = html;
                let id = "directionOption" + count.toString();
                lbl.id = id;
                document.getElementById('directionOptions').appendChild(lbl);
                options[id] = {
                    radID: radID,
                    trips: data[trip]
                };                   
                count++;
            }
        if (document.getElementById('directionRadio0'))
            document.getElementById('directionRadio0').focus();
    }

    function renderMarkers(data, recenter = false) {
        if (!data)
            return;
        let isEmpty = true;
        for (let trip in data)
            if (data.hasOwnProperty(trip)) {
                isEmpty = false;
                break;
            }
        if (isEmpty) {
            document.getElementById('prompt').classList.remove("hidden");
            document.getElementById('prompt').innerHTML = 'No vehicles active were found.';
            document.getElementById('map').classList.add('hidden');
            return;
        }
        document.getElementById('map').classList.remove('hidden');
        document.getElementById('prompt').classList.add("hidden");
        for (let i = 0; i < markers.length; i++)
            markers[i].setMap(null);
        markers.length = 0;

        let sumLat = 0, sumLon = 0, n = 0;

        for (let loc in data)
            if (data.hasOwnProperty(loc)) {
                let lat = data[loc].coordinate.latitude;
                let lon = data[loc].coordinate.longitude;
                let occu = data[loc].occupancy;
                if (!occu)
                    occu = ' ';

                if (recenter) {
                    sumLat += lat;
                    sumLon += lon;
                    n++;
                }

                let marker = new google.maps.Marker({
                    position: new google.maps.LatLng(lat, lon),
                    icon: busIcon(),
                    label: {
                        text: occu,
                        color: "#BE1616",
                        fontSize: "16px",
                        fontWeight: "bold"
                    },
                    map: map
                });
                markers.push(marker);
            }

        if (recenter) {
            let centerLat = sumLat / n;
            let centerLon = sumLon / n;
            let center = new google.maps.LatLng(centerLat, centerLon);
            map.setCenter(center);
        }

        if (navigator.geolocation) {
            navigator.geolocation.getCurrentPosition(function (position) {
                userLoc = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
                if (userMarker)
                    userMarker.setMap(null);
                userMarker = new google.maps.Marker({
                    position: userLoc,
                    icon: userIcon(),
                    label: {
                        text: 'I am here',
                        color: '#2916BE',
                        fontSize: "16px",
                        fontWeight: "bold"
                    },
                    map: map
                });
            });
        }
    }

    function latRad(lat) {
        var sin = Math.sin(lat * Math.PI / 180);
        var radX2 = Math.log((1 + sin) / (1 - sin)) / 2;
        return Math.max(Math.min(radX2, Math.PI), -Math.PI) / 2;
    }

    function zoom(mapPx, worldPx, fraction) {
        return Math.floor(Math.log(mapPx / worldPx / fraction) / Math.LN2);
    }

    function getBoundsZoomLevel(bounds, mapDim) {
        var WORLD_DIM = { height: 256, width: 256 };
        var ZOOM_MAX = 21;

        var ne = bounds.getNorthEast();
        var sw = bounds.getSouthWest();

        var latFraction = (latRad(ne.lat()) - latRad(sw.lat())) / Math.PI;

        var lngDiff = ne.lng() - sw.lng();
        var lngFraction = ((lngDiff < 0) ? (lngDiff + 360) : lngDiff) / 360;

        var latZoom = zoom(mapDim.height, WORLD_DIM.height, latFraction);
        var lngZoom = zoom(mapDim.width, WORLD_DIM.width, lngFraction);

        return Math.min(latZoom, lngZoom, ZOOM_MAX);
    }
});

