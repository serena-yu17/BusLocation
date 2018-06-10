var entityMap = {
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

var map = new google.maps.Map(document.getElementById('map'), { zoom: 13 });

var radioToggle = null;

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

    var zindexBus = 10000;
    var zindexSelf = 99999;

    setInterval(function () {
        tripStops = {};
        tripStopSigs = {};
        availStops.clear();
    }, 1000 * 3600 * 24);

    document.getElementById('searchRoute').onclick = function (event) {
        event.preventDefault();
        for (var op in options)
            if (options.hasOwnProperty(op)) {
                var elem = document.getElementById(op);
                if (elem)
                    elem.parentElement.removeChild(elem);
            }
        options = {};
        getRoute();
    }

    document.getElementById("route").onfocus = function () {
        document.getElementById("directionForm").classList.add("fade");
        document.getElementById("map").classList.add("fade");
    };

    document.getElementById("route").onblur = function () {
        document.getElementById("directionForm").classList.remove("fade");
        document.getElementById("map").classList.remove("fade");
    };

    radioToggle = function (caller) {
        getLoc(true);
        refreshLoc();
    }

    function busIcon() {
        var size = Math.max($(document).width(), $(document).height()) * 0.015;
        return {
            url: "/images/Bus.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size / 2, -size / 8)
        };
    }

    function userIcon() {
        var size = Math.max($(document).width(), $(document).height()) * 0.015;
        return {
            url: "/images/crosshair.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size * 0.75, -size / 6)
        };
    }

    function stopIcon() {
        var size = Math.max($(document).width(), $(document).height()) * 0.006;
        return {
            url: "/images/dot-orange.svg",
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

    function getLoc(isFreshLoad) {
        var tripArr = null;
        for (var op in options)
            if (options.hasOwnProperty(op)) {
                var radID = options[op].radID;
                var elem = document.getElementById(radID);
                if (elem && elem.checked) {
                    tripArr = options[op].trips;
                    break;
                }
            }
        if (tripArr !== null && tripArr.length !== 0) {
            var tripStrArr = [];
            var tripsToUpd = [];
            for (var i = 0; i < tripArr.length; i++) {
                tripStrArr.push(tripArr[i].toString());
                if (!tripStops.hasOwnProperty(tripArr[i]) && isFreshLoad)
                    tripsToUpd.push(tripArr[i]);
            }
            if (isFreshLoad)
                getStops(tripsToUpd, tripArr);
            var data = {
                tripIDs: tripStrArr.join(',')
            }
            $.ajax({
                type: "GET",
                url: locUrl,
                data: data,
                cache: false,
                success: function (data) {
                    renderMarkers(data, isFreshLoad);
                },
                error: function (msg) {
                    console.log(msg);
                }
            });
        }
    }

    function getStops(tripIDToUpd, tripArr) {
        var tripsToUpdStr = [];
        for (var i = 0; i < tripIDToUpd.length; i++) {
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
                for (var trip in data)
                    if (data.hasOwnProperty(trip)) {
                        var coord = data[trip];
                        if (!tripStops.hasOwnProperty(trip))
                            tripStops[trip] = [];
                        if (!tripStopSigs.hasOwnProperty(trip))
                            tripStopSigs[trip] = new Set();
                        for (i = 0; i < coord.length; i++) {
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
        for (var i = 0; i < stopMarkers.length; i++)
            stopMarkers[i].setMap(null);
        stopMarkers.length = 0;
        if (!tripIDs || tripIDs.length === 0)
            return;
        var usedStops = new Set();
        for (i = 0; i < tripIDs.length; i++)
            if (tripStops.hasOwnProperty(tripIDs[i]) && tripStops[tripIDs[i]]) {
                var stops = tripStops[tripIDs[i]];
                for (var j = 0; j < stops.length; j++) {
                    var key = stops[j].latitude.toString() + "&" + stops[j].longitude.toString();
                    if (!usedStops.has(key)) {
                        var marker = new google.maps.Marker({
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
        var isEmpty = true;
        for (var trip in data)
            if (data.hasOwnProperty(trip)) {
                isEmpty = false;
                break;
            }
        if (isEmpty) {
            document.getElementById('directionForm').classList.add('hidden');
            return;
        }
        document.getElementById('directionForm').classList.remove('hidden');
        var count = 0;
        for (trip in data)
            if (data.hasOwnProperty(trip)) {
                var lbl = document.createElement('label');
                lbl.classList.add("form-control");
                var radID = "directionRadio" + count.toString();
                var html = '<input type="radio" name="direction" id="' + radID + '" onclick="radioToggle();"/> ' + escapeHtml(trip);
                if (count === 0)
                    html = '<input type="radio" name="direction" id="' + radID + '" onclick="radioToggle();" checked/> ' + escapeHtml(trip);
                lbl.innerHTML = html;
                var id = "directionOption" + count.toString();
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
        radioToggle();
    }

    function renderMarkers(data, isFreshLoad) {
        if (!data)
            return;
        var isEmpty = true;
        for (var trip in data)
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

        var newMarkers = [];

        var count = 0;
        var maxLat = -10000, minLat = 10000, maxLon = -10000, minLon = 10000;

        for (var loc in data)
            if (data.hasOwnProperty(loc)) {
                var lat = data[loc].coordinate.latitude;
                var lon = data[loc].coordinate.longitude;
                var occu = data[loc].occupancy;
                if (!occu)
                    occu = ' ';

                if (isFreshLoad === true) {
                    if (lat > maxLat)
                        maxLat = lat;
                    if (lat < minLat)
                        minLat = lat;
                    if (lon > maxLon)
                        maxLon = lon;
                    if (lon < minLon)
                        minLon = lon;
                }

                var marker = new google.maps.Marker({
                    position: new google.maps.LatLng(lat, lon),
                    icon: busIcon(),
                    label: {
                        text: occu,
                        color: "#BE1616",
                        fontSize: "16px",
                        fontWeight: "bold"
                    },
                    zIndex: zindexBus,
                    map: map
                });
                //remove old markers gradually to reduce visual lag
                if (count < markers.length)
                    markers[count].setMap(null);
                count++;
                zindexBus++;
                newMarkers.push(marker);
            }
        if (count < markers.length)
            for (var m = count; m < markers.length; m++)
                markers[m].setMap(null);
        markers = newMarkers;

        if (isFreshLoad === true) {
            var bound = new google.maps.LatLngBounds(
                new google.maps.LatLng(minLat, minLon),
                new google.maps.LatLng(maxLat, maxLon),
            );
            map.fitBounds(bound, Math.max($(document).width(), $(document).height()) * 0.02);
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
                    zindex: zindexSelf,
                    map: map
                });
            });
        }
    }

    //function latRad(lat) {
    //    var sin = Math.sin(lat * Math.PI / 180);
    //    var radX2 = Math.log((1 + sin) / (1 - sin)) / 2;
    //    return Math.max(Math.min(radX2, Math.PI), -Math.PI) / 2;
    //}

    //function zoom(mapPx, worldPx, fraction) {
    //    return Math.floor(Math.log(mapPx / worldPx / fraction) / Math.LN2);
    //}

    //function getBoundsZoomLevel(bounds, mapDim) {
    //    var WORLD_DIM = { height: 256, width: 256 };
    //    var ZOOM_MAX = 21;

    //    var ne = bounds.getNorthEast();
    //    var sw = bounds.getSouthWest();

    //    var latFraction = (latRad(ne.lat()) - latRad(sw.lat())) / Math.PI;

    //    var lngDiff = ne.lng() - sw.lng();
    //    var lngFraction = ((lngDiff < 0) ? (lngDiff + 360) : lngDiff) / 360;

    //    var latZoom = zoom(mapDim.height, WORLD_DIM.height, latFraction);
    //    var lngZoom = zoom(mapDim.width, WORLD_DIM.width, lngFraction);

    //    return Math.min(latZoom, lngZoom, ZOOM_MAX);
    //}
});

