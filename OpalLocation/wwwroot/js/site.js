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

(function () {
    var waitingUpd = false;

    var tripDirections = {};

    document.getElementById('route').onclick = function () {
        if (!waitingUpd) {
            waitingUpd = true;
            document.getElementById('direction').innerHTML = '';
            getRoute();
        }
    };

    document.getElementById('searchRoute').onclick = function () {
        getRoute();
    }

    document.getElementById('submit').onclick = function () {

    };

    function getRoute() {
        var routeElem = document.getElementById('route');
        var route = routeElem.value.trim();
        if (route != '') {
            document.getElementById('searchRoute').disabled = true;
            $.ajax({
                type: "GET",
                url: tripUrl,
                data: {
                    route: route
                },
                success: function (msg) {
                    console.log(msg);
                    document.getElementById('direction').innerHTML = '';
                    tripDirections = msg;
                    for (let trip in msg)
                        if (msg.hasOwnProperty(trip)){
                            let option = document.createElement('option');
                            option.value = trip;
                            option.innerHTML = escapeHtml(trip);
                            document.getElementById('direction').appendChild(option);
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
})();

