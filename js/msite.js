

//var app = angular.module("MSite", []);

//var app = angular.module("MSite", ["ngRoute"]);

//app.config(function ($routeProvider) {
//    $routeProvider.when("/", { templateUrl: "/Home/Index" });
//    $routeProvider.when("/go", { templateUrl: "/Hotel" });
//    $routeProvider.when("/run", { templateUrl: "/Home/Hotel" });
//});

//app.controller("MSiteController", function ($scope) {
//});

//function compileAngularElement(elSelector) {

//    var elSelector = (typeof elSelector == 'string') ? elSelector : null;
//    // The new element to be added
//    if (elSelector != null) {
//        var $div = $(elSelector);

//        // The parent of the new element
//        var $target = $("[ng-app]");

//        angular.element($target).injector().invoke(['$compile', function ($compile) {
//            var $scope = angular.element($target).scope();
//            $compile($div)($scope);
//            // Finally, refresh the watch expressions in the new element
//            $scope.$apply();
//        }]);
//    }
//}

//$('.popup-btnSearch').magnificPopup({
//    closeOnContentClick: false,
//    closeOnBgClick: false,
//    removalDelay: 500,
//    callbacks: {
//        beforeOpen: function () {
//            console.log("magnificPopup");
//            this.st.mainClass = this.st.el.attr('data-effect');
//        }
//    }
//})

String.prototype.format = String.prototype.f = function () {
    var s = this,
        i = arguments.length;

    while (i--) {
        s = s.replace(new RegExp('\\{' + i + '\\}', 'gm'), arguments[i]);
    }
    return s;
}

var counter;
var varia = { sortBy: null, selectedRooms: [], timerDuration: 20, dontEnd: ['/Home/ASSignIn'], dontBack: ['/Home/HOLast'] };
var baseUrl = window.location.protocol + "//" + window.location.host + "/";

function getUrl(page) {
    var url = baseUrl + "Home/" + page;
    console.log(url);
    return url;
}

function getFormattedDate(e) {
    return moment($(e).datepicker('getDate')).format('DD/MM/YYYY');
}

function setParameters() {
    return;
    debugger;

    var re = [];
    if ($('#txtCache').val())
        re = $('#txtCache').val().split('|');

    if (re[0])
        $('#txtPlace').typeahead('val', re[0]);

    if (re[1])
        $("#txtCheckIn").datepicker('setDate', re[1]);
    else
        $("#txtCheckIn").datepicker('setDate', '+1d');

    if (re[2])
        $("#txtCheckOut").datepicker('setDate', re[2]);
    else
        $("#txtCheckOut").datepicker('setDate', '+2d');

    if (re[3])
        setOptionValue('#txtRooms', '#txtRooms2', 3, Number.parseInt(re[3]));
    if (re[4])
        setOptionValue('#txtAdults', '#txtAdults2', 3, Number.parseInt(re[4]));

    var fromRate = 0;
    var toRate = 1000;

    if (re[5] && re[6]) {
        fromRate = Number.parseInt(re[5]);
        toRate = Number.parseInt(re[6]);
    }
}

function getChildrenWithAge() {
    var text;
    text = $('#txtChild2').children('option:selected').text() + ';';
    $('.cmbChild2').each(function (i, e) {
        text += $(e).children('option:selected').text() + ";";
    });
    logToConsole(text);
    return text;
}

function getOptionValue(firstSelector, secondSelector) {
    var value = 1;
    if ($(firstSelector).hasClass('hidden'))
        value = Number.parseInt($(secondSelector).children('option:selected').val());
    else
        value = Number.parseInt($(firstSelector).children('label.active').text());
    return value;
}

function setOptionValue(firstSelector, secondSelector, limitValue, currentValue) {
    $(firstSelector).children('label').each(function (index, element) {
        $(element).removeClass('active');
    });

    if (currentValue <= limitValue) {
        $(firstSelector).removeClass('hidden');
        $(secondSelector).addClass('hidden');

        $(firstSelector).children('label').eq(currentValue - 1).addClass('active');
    } else {
        $(firstSelector).addClass('hidden');
        $(secondSelector).removeClass('hidden');

        $(secondSelector).children('option').eq(currentValue - 1).attr("selected", "selected");
    }
}

function sendPost(path, parameters) {
    var form = $('<form></form>');

    if (parameters.blankTab)
        form = $('<form target="_blank"></form>');

    form.attr("method", "post");
    form.attr("action", path);
    form.addClass("hidden");

    var field = $('<input></input>');
    field.attr("type", "hidden");
    field.attr("name", "jsonString");
    field.attr("value", JSON.stringify(parameters));
    form.append(field);

    console.log(form.html());
    console.log(form);

    $(document.body).append(form);
    form.submit();
}

function fwLink(linkId) {
    logToConsole("fwLink {0}", linkId);

    var obj = {};

    switch (linkId) {
        case 101101:
            obj = { companyId: arguments[1] };
            break;
        case 101102:
            obj = {
                agentInfo: {
                    firstName: $("#txtFirstName").val(),
                    lastName: $("#txtLastName").val(),
                    email: $("#txtEmail").val(),
                    phoneNo: $("#txtPhoneNo").val()
                },
                companyInfo: {
                    city: $("#txtCity").val(),
                    country: $("#txtCountry").val(),
                    zipCode: $("#txtZipCode").val(),
                    creditLimit: $("#txtCreditLimit").val(),
                    profit: $("#txtProfit").val(),
                    isActive: $("#chkActive").is(":checked"),
                    isApproved: $('#chkApproved').is(":checked")
                },
                remarks: $("#txtRemarks").val(),
                isSendEmail: $("#chkSendEmail").is(":checked")
            };
            break;
        case 102101:
            obj = { userName: $("#txtUserName").val(), password: $("#txtPassword").val() };
            break;
        case 10210101:
            $.ajax({
                url: getUrl("FWLink"),
                type: 'POST',
                data: { jsonString: JSON.stringify({ linkId: linkId, userName: $('#txtUserName').val(), password: $('#txtPassword').val() }) },
                success: function (re) {
                    console.log('Success');
                    console.log(re);
                    if (JSON.parse(re).approved)
                        fwLink(102101);
                    else
                        showPopup('#mfpMsgWrong', true);
                },
                error: function (re) {
                    console.log('error');
                    console.log(re);
                    showPopup('#mfpMsgWrong', true);
                }
            });
            return;
        case 102103:
            obj = {
                agentInfo: {
                    firstName: $("#txtComName").val(),
                    email: $("#txtCEmail").val()
                },
                contactInfo: {
                    firstName: $("#txtCName").val(),
                    position: $("#txtCPosition").val(),
                    phoneNo: $("#txtCPhone").val(),
                    faxNo: $("#txtCFax").val(),
                    mobileNo: $("#txtCMobile").val(),
                    email: $("#txtCEmail").val()
                },
                accountantInfo: {
                    firstName: $("#txtAName").val(),
                    phoneNo: $("#txtAPhone").val(),
                    faxNo: $("#txtAFax").val(),
                    mobileNo: $("#txtAMobile").val(),
                    email: $("#txtAEmail").val()
                },
                companyInfo: {
                    name: $("#txtComName").val(),
                    shortName: $("#txtComShortName").val(),
                    regNo: $("#txtComRegNo").val(),
                    country: $("#txtComCountry").val(),
                    city: $("#txtComCity").val(),
                    pobNo: $("#txtComPOBNo").val(),
                    zipCode: $("#txtComZipCode").val(),
                    address: $("#txtComAddress").val(),
                    landLine: $("#txtComLandLine").val(),
                    fax: $("#txtComFax").val(),
                    website: $("#txtComWebsite").val()
                }
            };
            break;
        case 103101:
            var permissions = ";";
            $('.iPer:checked').each(function (i, e) {
                permissions += $(e).val() + ";";
            });
            obj = {
                firstName: $("#txtFirstName").val(),
                lastName: $("#txtLastName").val(),
                email: $("#txtEmail").val(),
                position: $('#txtPosition').val(),
                isActive: $('#chkActive').is(':checked'),
                lstPermission: permissions
            };
            break;
        case 103102:
            obj = {
                fromDate: getFormattedDate('#dtFrom'),
                toDate: getFormattedDate('#dtTo'),
                country: $('#txtCountry').val(),
                city: $('#txtCity').val(),
                firstName: $('#txtFirstName').val(),
                lastName: $('#txtLastName').val(),
                bRefNo: $("#txtBRefNo").val(),
                hotelName: $('#txtHotelName').val(),
                bStatus: $("#cmbBStatus option:selected").val()
            }
            break;
        case 10310201:
            obj = {
                bId: arguments[1],
                actionId: arguments[2]
            }
            break;
        case 10310401:
            obj = { userId: arguments[1] };
            break;
        case 104101:
            obj = {
                place: $("#txtPlace").typeahead('val'),
                checkIn: $("#txtCheckIn").val(),
                checkOut: $("#txtCheckOut").val(),
                roomsCount: getOptionValue('#txtRooms', '#txtRooms2'),
                adultsCount: getOptionValue('#txtAdults', '#txtAdults2'),
                children: getChildrenWithAge()
            };

            if (obj.place && obj.checkIn && obj.checkOut) {
                showPopup('#mfpMsgLoading', false);
                break;
            }
            else {
                showPopup('#mfpMsgValidate', true);
                return;
            }
        case 10410101:
            obj = {
                place: arguments[1],
                checkIn: moment().add(2, 'days').format("DD/MM/YYYY"),
                checkOut: moment().add(3, 'days').format("DD/MM/YYYY"),
                roomsCount: 1,
                adultsCount: 1,
                children: "0"
            };
            showPopup('#mfpMsgLoading', false);
            break;
        case 10410102:
            obj = {
                place: $("#txtPlace").typeahead('val'),
                checkIn: $("#txtCheckIn").val(),
                checkOut: $("#txtCheckOut").val(),
                roomsCount: getOptionValue('#txtRooms', '#txtRooms2'),
                adultsCount: getOptionValue('#txtAdults', '#txtAdults2'),
                children: getChildrenWithAge()
            };
            break;
        case 10410201:
            if (arguments[1])
                console.log("arg1: ", arguments[1]);

            if (arguments[2])
                console.log("arg2: ", arguments[2]);

            if (arguments[1])
                varia.sortBy = arguments[1];

            if (arguments[2])
                varia.pageIndex = arguments[2];
            else
                varia.pageIndex = 0;

            var doSearch = false;
            if (arguments[3])
                doSearch = arguments[3];

            var filters = "";
            $(".iFilter:checked").each(function (i, e) {
                filters += e.value;
            });
            console.log("Filter: " + filters);

            $.ajax({
                url: getUrl("FWLink"),
                type: 'POST',
                data: { jsonString: JSON.stringify({ linkId: linkId, filterBy: filters, fromPrice: $("#price-slider").val().split(";")[0], toPrice: $("#price-slider").val().split(";")[1], sortBy: varia.sortBy, pageIndex: varia.pageIndex, doSearch: doSearch }) },
                success: function (re) {
                    console.log("success, ");
                    $('#txtCCache').val(re)
                    $("#ulHotelsList").html(re);
                    // compileAngularElement("#ulHotelsList");

                    var sortValues = ['SPL', 'SPH', 'SSL', 'SSH'];
                    $('.booking-sort-title-bar').css({ "color": '', 'text-decoration': 'none' });
                    for (var i = 0; i < sortValues.length; i++)
                        if (varia.sortBy == sortValues[i])
                            $('.booking-sort-title-bar').eq(i).css({ 'color': '#c96810', 'text-decoration': 'underline' });
                },
                error: function (re) {
                    console.log("error, ");
                    $("#ulHotelsList").html(re);
                    // compileAngularElement("#ulHotelsList");
                }
            });
            return;
        case 104103:
            if (arguments[1] == "Erroneous")
                return;
            obj = { hotelId: arguments[1] };
            break;
        case 10410301:
            obj = {
                checkIn: $("#txtCheckIn").val(),
                checkOut: $("#txtCheckOut").val(),
                roomsCount: getOptionValue('#txtRooms', '#txtRooms2'),
                adultsCount: getOptionValue('#txtAdults', '#txtAdults2'),
                children: getChildrenWithAge()
            };
            showPopup('#mfpMsgLoading', false);
            break;
        case 10410302:
            if (varia.selectedRooms.length == 0) {
                showPopup('#mfpMsgSelectRoom', true);
                return;
            }
            obj = { selectedRates: JSON.stringify(varia.selectedRooms) };
            break;
        case 104104:
            obj = {
                bRefNo: $('#txtBRefNo').val(),
                customerDetails: {
                    title: $("#cmbCuTitle :selected").text(),
                    firstName: $("#txtCuFirstName").val(),
                    lastName: $("#txtCuLastName").val(),
                    nationality: $("#txtCuNationality").val(),
                    phone: $("#txtCuPhone").val(),
                    email: $("#txtCuEmail").val()
                },
                roomsDetails: [
                    {
                        title: $("#title").val(),
                        firstName: $("#txtFirstName").val(),
                        lastName: $("#txtLastName").val(),
                        phone: $("#txtPhone").val(),
                        email: $("#txtEmail").val()
                    }
                ]
            };

            if (obj.customerDetails.firstName && obj.customerDetails.lastName && obj.customerDetails.nationality && obj.customerDetails.phone && obj.customerDetails.email) {
                showPopup('#mfpMsgLoading', false);
                break;
            }
            else {
                showPopup('#mfpMsgValidate', true);
                return;
            }
        case 10410502:
            obj = {
                type: $('.i-radio').filter(':checked').val(),
                blankTab: true
            }
        default:
            break;
    }

    logToConsole(obj);
    obj.linkId = linkId;
    sendPost(getUrl("FWLink"), obj);
}

function doClick(event, id) {
    if (event.keyCode == 13)
        fwLink(10210101);
}

function showPopup(e, bgClose) {
    $.magnificPopup.open({
        items: {
            src: e,
            type: 'inline'
        },
        closeOnBgClick: bgClose ? true : false,
        showCloseBtn: bgClose ? false : true
    });
}

function onChildChange() {
    console.log("onChildChange");

    var count = $('#txtChild2').val();
    console.log(count);
    $('#divChild').empty();
    for (var i = 0; i < count; i++) {
        $('#divChild').append('<div class="col-md-2 form-group form-group form-group-select-plus"><label>Child {0} Age</label><select class="form-control cmbChild2"><option>1</option><option>2</option><option>3</option><option>4</option><option>5</option><option>6</option><option>7</option><option>8</option><option>9</option></select></div>'.format(i + 1));
    }
}

// roomsArray = [{ no: 1, text: "Room 1" }, { no: 2, text: "2" }, { no: 3, text: "3" }, { no: 4, text: "4" }, { no: 5, text: "5" }, { no: 6, text: "6" }];
function onRoomChanged() {
    temp = [];

    $('.cmbCount').each(function (i, e) {
        rData = JSON.parse($(e).next().val());
        rCount = Number.parseInt($(e).find(":selected").text()) || 0;

        console.log($(e).next().val());

        if (rCount > 0)
            temp.push({ rateCode: rData.rId, count: rCount, price: rData.rPrice });
    });

    varia.selectedRooms = temp;
    var roomsCount = 0, totalPrice = 0;
    temp.forEach(function (v) {
        roomsCount += v.count;
        totalPrice += Math.round(v.count * v.price);
    });

    $('#txtRoomsCount').text("Rooms Count: {0}".format(roomsCount));
    $('#txtTotalPrice').text("Total Price: ${0}".format(totalPrice));
}

function onNatFocus() {
    logToConsole("good");
}

function getSource(linkId, q, cb) {
    return $.ajax({
        dataType: 'json',
        type: 'get',
        url: getUrl('FWLink?jsonString={ "linkId": {0}, "ddlQuery": "{1}" }'.format(linkId, q)),
        chache: false,
        success: function (data) {
            var result = [];
            $.each(data, function (index, val) {
                result.push({
                    value: val
                });
            });
            cb(result);
        }
    });
}

function doTick() {
    varia.timerDuration--;
    if (varia.timerDuration <= 0) {
        clearInterval(counter);
        fwLink(102106);
    } else
        console.log(varia.timerDuration);
};

function logToConsole(obj) {
    console.log(obj);
}

$(document).ready(function () {
    logToConsole(window.location.host);
    logToConsole(window.location.hostname);
    logToConsole(window.location.protocol);
    logToConsole(window.location.port);
    logToConsole(window.location.pathname);
    logToConsole(window.location.href);
    logToConsole(window.location.origin);

    if (varia.dontEnd.indexOf(window.location.pathname) == -1)
        counter = setInterval(doTick, 60 * 1000);
    else
        logToConsole('dontEnd');

    if (varia.dontBack.indexOf(window.location.pathname) >= 0) {
        history.pushState(null, null, document.URL);
        window.addEventListener('popstate', function () {
            history.pushState(null, null, document.URL);
        });
    }

    if (window.location.pathname == "/Home/HOHotel")
        onRoomChanged();
    else if (window.location.pathname == "/Home/HOHotels") {
        //TODO: there is two or more calls to server for each request, almost here the error which generate them.
        if ($("#txtCCache").val().length > 0)
            $("#ulHotelsList").html($("#txtCCache").val());
        else
            fwLink(10410201, null, null, true);
    }

    $('#txtCheckIn').datepicker('setStartDate', 'today');
    $('#txtCheckOut').datepicker('setStartDate', 'tomorrow');

    if ($("#divSignUp").size() > 0) {
        $("#divMenu").addClass("hidden");
        $("#divUserInfo").addClass("hidden");
    }

    $('.iFilter').on("ifChanged", function (event) {
        console.log(event);
        if ($(this).is('.i-check') || $(this).is('.i-radio:checked'))
            fwLink(10410201);
    });

    $('#txtCheckIn').datepicker().on('changeDate', function (e) {

        $('#txtCheckOut').datepicker('setStartDate', new Date(e.date.getTime() + 86400000));
        $('#txtCheckOut').datepicker('setDate', new Date(e.date.getTime() + 86400000));

        $('#txtCheckIn').datepicker('hide');
    });

    $("#price-slider").ionRangeSlider({
        min: 0,
        max: 1000,
        type: 'double',
        prefix: "$",
        prettify: false,
        hasGrid: true,
        keyboard: true,
        onFinish: function (data) {
            fwLink(10410201);
        }
    });

    $("#divDetails").scrollToFixed({
        marginTop: 20
        // limit: $('#main-footer').offset().top - $(this).outerHeight(true) - 10
    });

    // $('#txtPassword').on('keyup', { id: 102101 }, doClick);

    setParameters();
})

//$("#price-slider").ionRangeSlider({
//    min: 0,
//    max: 1000,
//    type: 'double',
//    prefix: "$",
//    prettify: false,
//    hasGrid: true,
//    keyboard: true,
//    onFinish: function (data) {
//        // console.log(data.fromNumber + ", " + data.toNumber);
//        $scope.btnHOFilterAndSort();
//    }
//});

// $('#txtStart').datepicker('update', new Date(2010, 01, 11));
//window.location.href = "Home/Hotels";

//$('popup-loading').magnificPopup({
//    items: {
//        src: $('<div class="white-popup">Dynamically created popup</div>'),
//        type: 'inline'
//    }
//});

//$('#popupOnSearch').magnificPopup({
//    removalDelay: 500,
//    closeBtnInside: false,
//    callbacks: {
//        beforeOpen: function () {
//            this.st.mainClass = this.st.el.attr('data-effect');
//        }
//    },
//    midClick: true
//});

//$scope.onGetHotels = function () {
//    console.log("onGetHotels");
//    $.magnificPopup.open({
//        items: {
//            src: '<div class="form-group form-group-lg form-group-icon-left">Dynamically created popup</div>', // can be a HTML string, jQuery object, or CSS selector
//            type: 'inline'
//        }
//    });

//    $("#divMain").addClass("hidden");
//    $("#divLoading").removeClass("hidden");

//     post("/Home/Hotels", $scope.getParameters());
//};

//$scope.onSearch3 = function () {
//    //$('#txtStart').datepicker('update', new Date(2010, 01, 11));
//    //$('#txtEnd').datepicker('update', new Date(2010, 01, 25));
//    //alert($('#txtStart').val());
//    //$('#price-slider').ionRangeSlider('update', { min: 10, max: 90, from: 30, to: 60, step: 5 });

//    console.log($('#txtChild .active').text() + ' ' + $('#txtChildPlus option:selected').text());

//    $.ajax({
//        url: 'GetHotels',
//        success: function (result) {
//            console.log("success");
//            $('.booking-list').html(result);
//        },
//        error: function (result) {
//            console.log("error");
//        }
//    });
//};

