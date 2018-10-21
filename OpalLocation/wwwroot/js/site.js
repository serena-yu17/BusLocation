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

var map = new google.maps.Map(document.getElementById('map'), {
    zoom: 13,
    panControl: true,
    zoomControl: true,
    mapTypeControl: true,
    scaleControl: true,
    streetViewControl: true,
    overviewMapControl: true,
    rotateControl: true
});

var radioToggle = null;

$(document).ready(function () {
    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(function (position) {
            initialLocation = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
            map.setCenter(initialLocation);
        });
    }

    var options = {};
    var vehicleMarkers = [];
    var refreshInterval = null;
    var timeOut = null;
    var tripStops = {};
    var tripStopSigs = {};
    var availStops = new Set();
    var stopMarkers = [];
    var userMarker = null;
    var currentRadio = null;
    var trafficLayer = null;

    var iconSizes = [
        [16, 1.8],
        [15, 1.5],
        [14, 1.2]
    ];

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
    };

    document.getElementById("route").onfocus = function () {
        document.getElementById("directionForm").classList.add("fade");
        document.getElementById("map").classList.add("fade");
        document.getElementById('btn-traffic').classList.add("fade");
    };

    document.getElementById("route").onblur = function () {
        document.getElementById("directionForm").classList.remove("fade");
        document.getElementById("map").classList.remove("fade");
        document.getElementById('btn-traffic').classList.remove("fade");
    };

    document.getElementById('btn-traffic').onclick = function () {
        if (trafficLayer === null) {
            trafficLayer = new google.maps.TrafficLayer();
            trafficLayer.setMap(map);
            this.classList.remove('btn-outline-success');
            this.classList.add('btn-success');
        }
        else {
            trafficLayer.setMap(null);
            trafficLayer = null;
            this.classList.add('btn-outline-success');
            this.classList.remove('btn-success');
        }
    };

    google.maps.event.addListener(map, 'zoom_changed', function () {
        resizeIcons(getIconSize());
    });

    radioToggle = function (caller) {
        getLoc(true);
        refreshLoc();
    }

    function getIconSize() {
        var zoom = map.getZoom();
        for (var i = 0; i < iconSizes.length; i++) {
            if (zoom >= iconSizes[i][0])
                return iconSizes[i][1];
        }
        return 1;
    }

    function resizeIcons(factor) {
        if (vehicleMarkers)
            for (var i = 0; i < vehicleMarkers.length; i++)
                vehicleMarkers[i].setIcon(busIcon(factor));
        if (stopMarkers)
            for (i = 0; i < stopMarkers.length; i++)
                stopMarkers[i].setIcon(stopIcon(factor));
        if (userMarker)
            userMarker.setIcon(userIcon(factor));
    }

    function busIcon(factor) {
        var size = Math.max($(document).width(), $(document).height()) * 0.015;
        if (factor !== undefined)
            size *= factor;
        return {
            url: "/images/Bus.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size / 2, -size / 8)
        };
    }

    function userIcon(factor) {
        var size = Math.max($(document).width(), $(document).height()) * 0.015;
        if (factor !== undefined)
            size *= factor;
        return {
            url: "/images/crosshair.svg",
            scaledSize: new google.maps.Size(size, size),
            origin: new google.maps.Point(0, 0),
            anchor: new google.maps.Point(size / 2, size / 2),
            labelOrigin: new google.maps.Point(size * 0.75, -size / 6)
        };
    }

    function stopIcon(factor) {
        var size = Math.max($(document).width(), $(document).height()) * 0.006;
        if (factor !== undefined)
            size *= factor;
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
            document.getElementById('map').classList.add("hidden");
            document.getElementById('btn-traffic').classList.add("hidden");
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
                            icon: stopIcon(getIconSize()),
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
            document.getElementById('prompt').classList.remove("hidden");
            document.getElementById('prompt').innerHTML = 'No vehicles active were found.';
            return;
        }
        document.getElementById('directionForm').classList.remove('hidden');
        document.getElementById('prompt').classList.add("hidden");
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
            document.getElementById('btn-traffic').classList.add('hidden');
            return;
        }
        document.getElementById('map').classList.remove('hidden');
        document.getElementById('btn-traffic').classList.remove('hidden')
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
                    icon: busIcon(getIconSize()),
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
                if (count < vehicleMarkers.length)
                    vehicleMarkers[count].setMap(null);
                count++;
                zindexBus++;
                newMarkers.push(marker);
            }
        if (count < vehicleMarkers.length)
            for (var m = count; m < vehicleMarkers.length; m++)
                vehicleMarkers[m].setMap(null);
        vehicleMarkers = newMarkers;

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
                    icon: userIcon(getIconSize()),
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
});

