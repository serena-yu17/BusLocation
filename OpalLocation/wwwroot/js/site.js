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

var map;


function escapeHtml(string) {
    return String(string).replace(/[&<>"'`=\/]/g, function (s) {
        return entityMap[s];
    });
}

function initMap() {
    map = new google.maps.Map(document.getElementById('map'),
        {
            zoom: 13
        }
    );
    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(function (position) {
            initialLocation = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
            map.setCenter(initialLocation);
        });
    }
}

$(window).on('load', function () {
    const busIcon = {
        url: "/images/Bus.svg",
        scaledSize: new google.maps.Size(30, 30),
        origin: new google.maps.Point(0, 0),
        anchor: new google.maps.Point(10, 10),
        labelOrigin: new google.maps.Point(0, -5)
    }

    const userIcon = {
        url: "/images/crosshair.svg",
        scaledSize: new google.maps.Size(40, 40),
        origin: new google.maps.Point(0, 0),
        anchor: new google.maps.Point(5, 5),
        labelOrigin: new google.maps.Point(10, -5)
    }

    var options = {};
    var markers = [];
    var refreshInterval = null;
    var timeOut = null;
    var markersInUse = [];
    var tripStops = {};
    var availStops = new Set();

    setInterval(function () {
        tripStops = {};
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
        refreshLoc();
    };

    function refreshLoc() {
        getLoc();
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

    function getLoc() {
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
            for (let i = 0; i < tripArr.length; i++)
                tripStrArr.push(tripArr[i].toString());
            let data = {
                tripIDs: tripStrArr.join(",")
            }
            document.getElementById('submit').disabled = true;
            $.ajax({
                type: "GET",
                url: locUrl,
                data: data,
                success: function (data) {
                    if (!data)
                        return;
                    let isEmpty = true;
                    for (let trip in data)
                        if (data.hasOwnProperty(trip)) {
                            isEmpty = false;
                            break;
                        }
                    if (isEmpty) {
                        document.getElementById('map').classList.add('hidden');
                        return;
                    }
                    document.getElementById('map').classList.remove('hidden');

                    for (let i = 0; i < markers.length; i++)
                        markers[i].setMap(null);
                    markers.length = 0;

                    for (let loc in data)
                        if (data.hasOwnProperty(loc)) {
                            let lat = data[loc].coordinate.latitude;
                            let lon = data[loc].coordinate.longitude;
                            let occu = data[loc].occupancy;

                            let marker = new google.maps.Marker({
                                position: new google.maps.LatLng(lat, lon),
                                icon: busIcon,
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

                    if (navigator.geolocation) {
                        navigator.geolocation.getCurrentPosition(function (position) {
                            userLoc = new google.maps.LatLng(position.coords.latitude, position.coords.longitude);
                            let marker = new google.maps.Marker({
                                position: userLoc,
                                icon: userIcon,
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

    function getStops(tripID) {
        $.ajax({
            type: "GET",
            url: stopUrl,
            data: {
                tripID: tripID
            },
            success: function (data) {
                if (!data || data.length === 0)
                    return;
                for (let i = 0; i < data.length; i++) {
                    var coord = data[i];
                    var coordSig = coord.latitude.toString() + "^" + coord.longitude.toString();
                    if (!availStops.has(coordSig)) {
                        if (!tripStops.hasOwnProperty(tripID))
                            tripStops[tripID] = [];
                        tripStops[tripID].push(coord);
                        availStops.add(coordSig);
                    }
                }                
            },
            error: function (msg) {
                console.log(msg);
            }
        });
    }
});

