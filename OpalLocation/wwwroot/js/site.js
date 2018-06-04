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

    document.getElementById('route').onclick = function () {
        if (!waitingUpd) {
            waitingUpd = true;
            document.getElementById('direction').innerHTML = '';            
            getRoute();
        }
    };

    document.getElementById('submit').onclick = function () {

    };

    function getRoute() {
        var routeElem = document.getElementById('route');
        var route = routeElem.value.trim();
        if (route != '') {
            $.ajax({
                type: "GET",
                url: tripUrl,
                data: {
                    route: route
                },
                success: function (msg) {
                    document.getElementById('direction').innerHTML = ''; 
                    for (let trip in msg) {
                        let option = document.createElement('option');
                        option.value = trip.id;
                        option.innerHTML = escapeHtml(trip.desc);
                        document.getElementById('direction').appendChild(option);
                    }
                },
                error: function (msg) {
                    console.log(msg);
                },
                complete: function () {
                    waitingUpd = false;
                }
            });
        }
    }
})();

