using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Xml.Linq;
using System.Data;
using System.Net.Mail;
using System.Net;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Dynamic;
using System.Collections;
using System.Security.Cryptography;
using System.IO.Compression;
using Newtonsoft.Json;
using Excel = Microsoft.Office.Interop.Excel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Xml;
using System.Runtime.Serialization.Formatters.Binary;

namespace MSite
{
    public enum SortBy { Default, PriceAsc, PriceDesc, StarsAsc, StarsDesc }

    [Flags]
    public enum BookingStatus
    {
        None = 0,
        Pending = 1,
        Confirmed = 2,
        ReConfirmed = 4,
        Cancelled = 8,
        OnTimeLimit = Pending | Confirmed,
        All = Pending | Confirmed | ReConfirmed | Cancelled,
        PendingOrConfirmed = Pending | Confirmed,
        ConfirmedReConfirmed = Confirmed | ReConfirmed
    }

    [Flags]
    public enum BookingAction
    {
        None = 0,
        Confirm = 1,
        ReConfirm = 2,
        Cancel = 4
    }

    [Flags]
    public enum UserType
    {
        Guest = 0,
        Admin = 1,
        Agent = 2,
        AgentSubUser = 4
    }

    [Flags]
    public enum PermissionType
    {
        None = 0,
        ViewBookings = 1,
        CanConfirm = 2,
        CanReConfirm = 4,
        CanCancel = 8
    }

    public interface IProvider
    {
        Task<List<Hotel>> GetHotelsAsync();
        List<Hotel> GetHotels();
        Hotel GetHotel();

        void UpdateRooms(Hotel hotel);
        void UpdateRates(Hotel hotel);
        void UpdateInfo(Hotel hotel);
    }

    public class Prog
    {
        public static Prog Current
        {
            get
            {
                if (HttpContext.Current.Application["_prog"] == null)
                    HttpContext.Current.Application["_prog"] = new Prog();

                return HttpContext.Current.Application["_prog"] as Prog;
            }
        }

        public static Terminal Terminal
        {
            get
            {
                if (HttpContext.Current.Session["_terminal"] == null)
                    HttpContext.Current.Session["_terminal"] = new Terminal();
                return HttpContext.Current.Session["_terminal"] as Terminal;
            }
        }

        public static List<string> Log
        {
            get
            {
                if (HttpContext.Current.Application["_log"] == null)
                    HttpContext.Current.Application["_log"] = new List<string>();
                return HttpContext.Current.Application["_log"] as List<string>;
            }
        }

        public static ContentProvider Content
        {
            get { return Prog.Database.Content; }
        }

        private Database database { get; set; }
        public static Database Database
        {
            get { return Prog.Current.database; }
        }

        public List<Terminal> Terminals { get; set; }

        private Settings settings;
        public static Settings Settings { get { return Prog.Current.settings; } }

        //public static dynamic ESCache
        //{
        //    get
        //    {
        //        if (HttpContext.Current.Session["esCache"] == null)
        //            HttpContext.Current.Session["esCache"] = new ExpandoObject();
        //        return HttpContext.Current.Session["esCache"];
        //    }
        //}

        private Prog()
        {
            this.Terminals = new List<Terminal>();

            this.settings = Settings.GetSettings();
            this.database = new Database();
        }

        internal void LoopTasks()
        {
            Task.Factory.StartNew(new Action(() =>
            {
                while (true)
                {
                    try
                    {
                        DBBooking.Selects(BookingStatus.PendingOrConfirmed, DateTime.Now.AddHours(48));
                    }
                    catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); }
                }
            }));
        }
    }

    public class Terminal
    {
        public dynamic RequestCache { get; set; }
        public dynamic LastRequestCache { get; set; }

        public User User { get; private set; }
        public Booking BasicBooking { get; private set; }
        public Booking FinalBooking { get; private set; }
        public BookingInfo BookingInfo { get; set; }

        // public dynamic BookingParameters { get; set; }
        public Company CompanyUnderEdit { get; set; }
        public User SubUserEditMode { get; set; }

        public string Message { get; set; }
        internal IPInfo IPInfo;
        private string sessionInfo;
        public DateTime CreationTime { get; set; }

        public bool IsAuthenticated { get; private set; }
        public string Info
        {
            get { return $"Terminal info: {this.CreationTime} - SignIn: {this.User.FullName} - IsAuthenticated: {this.IsAuthenticated} - {this.sessionInfo}"; }
        }

        public bool IsCompany { get { return this.User.IsAgent || this.User.IsSubuser; } }

        public Terminal()
        {
            this.User = User.Guest;
            this.CreationTime = DateTime.Now;
            this.sessionInfo = $"IP: {HttpContext.Current.Request.UserHostAddress} - SessionId: {HttpContext.Current.Session.SessionID} - UserAgent: {HttpContext.Current.Request.UserAgent}";

            this.IPInfo = new IPInfo();

            Prog.Current.Terminals.Add(this);
            Misc.Log(this.Info);
        }

        internal void Reset(User user)
        {
            Misc.Log("Reset");

            if (user == null)
            {
                this.User = User.Guest;
                this.IsAuthenticated = false;
                this.BasicBooking = null;
                this.FinalBooking = null;
                this.BookingInfo = null;
            }
            else
            {
                this.User = user;
                this.IsAuthenticated = true;
                this.BasicBooking = new Booking(user);
                this.FinalBooking = null;
                this.BookingInfo = null;
            }
        }

        internal void MoveToFinal()
        {
            this.FinalBooking = this.BasicBooking.ShallowCopy();
        }

        internal bool SignIn(string email, string password)
        {
            User user = Prog.Database.SignIn(email, password);

            if (user == null)
                return false;

            this.Reset(user);

            Misc.Log(this.Info);
            return true;
        }

        internal void SignOut()
        {
            this.Reset(null);
            Prog.Current.Terminals.Remove(this);
            HttpContext.Current.Session.Abandon();
        }

        internal void EndBooking()
        {
            this.BookingInfo = new BookingInfo(this.FinalBooking);

            //TODO: set them to null, but first remove any usage in the next pages like HOLast
            //this.BasicBooking = null;
            //this.FinalBooking = null;
        }
    }

    public class Booking
    {
        public long? Id { get; internal set; }

        public string Place { get; set; }
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
        public int RoomsCount { get; set; }
        public int AdultsCount { get; set; }
        public int ChildrenCount { get; set; }
        public List<int> ChildrenAge { get; set; }

        public string FormattedCheckIn { get { return this.CheckIn.ToString("dd/MM/yyyy"); } }
        public string FormattedCheckOut { get { return this.CheckOut.ToString("dd/MM/yyyy"); } }
        public string FormattedCheckIn2 { get { return this.CheckIn.ToString("MMM, dd"); } }
        public string FormattedCheckOut2 { get { return this.CheckOut.ToString("MMM, dd"); } }

        public string CheckInMMDDYYYY { get { return this.CheckIn.ToString("MM/dd/yyyy"); } }
        public string CheckOutMMDDYYYY { get { return this.CheckOut.ToString("MM/dd/yyyy"); } }

        public Hotel SelectedHotel { get; set; }
        public List<BookingRate> SelectedRates { get; private set; }

        public float TotalPrice
        {
            get { return (float)Math.Round(this.SelectedRates.Sum(a => a.TotalPrice) * this.TotalNights); }
        }

        public BookingStatus Status { get; internal set; }
        public string BRefNo { get; set; }

        public bool Cancellable
        {
            get { return this.SelectedRates?.Where(a => a.Rate.Cancellable == false).Count() == 0; }
        }

        public Cancellation FirstActiveCancellation
        {
            get { return this.SelectedRates?.Select(a => a.Rate.FirstCancellation).Where(a => a.IsActive).OrderBy(a => a.From).FirstOrDefault(); }
        }

        public DateTime CreationTime { get; set; }
        public User User { get; private set; }

        public int TotalNights
        {
            get { return (int)(this.CheckOut - this.CheckIn).TotalDays; }
        }

        public string FormattedRoomsTypes
        {
            get { return this.SelectedRates.Aggregate(new StringBuilder(), (sb, r) => sb.AppendFormat("{0}\t\t{1}", r.Rate.Room.Name, r.TotalPrice).AppendLine()).ToString(); }
        }

        public string AsText { get { return $"Place: {this.Place} - CheckIn: {this.CheckIn.ToString()} - CheckOut: {this.CheckOut.ToString()}"; } }

        // TODO:
        public string City { get { return Prog.Content.City; } }

        public List<Hotel> BaseHotels { get; set; }
        public List<Hotel> FilteredHotels { get; private set; }

        public List<Rate> BaseRates
        {
            get { return this.BaseHotels.SelectMany(a => a.Rates).OrderBy(a => a.LastNet).ToList(); }
        }

        public Info MainCustomerInfo
        {
            get { return this.SelectedRates?[0].CustomerInfo; }
        }

        public bool HasDone
        {
            get { return !(this.Status == BookingStatus.None); }
        }

        public string IsRoomCirclesHidden { get { return ((this.RoomsCount > 3) ? "hidden" : ""); } }
        public string IsRoomListHidden { get { return ((this.RoomsCount > 3) ? "" : "hidden"); } }
        public string IsAdultsCirclesHidden { get { return ((this.AdultsCount > 3) ? "hidden" : ""); } }
        public string IsAdultsListHidden { get { return ((this.AdultsCount > 3) ? "" : "hidden"); } }

        internal Booking() { }

        public Booking(User user)
        {
            this.Place = null;
            this.CheckIn = DateTime.Now.AddDays(2);
            this.CheckOut = DateTime.Now.AddDays(3);
            this.RoomsCount = 1;
            this.AdultsCount = 1;
            this.ChildrenCount = 0;

            this.Status = BookingStatus.None;
            this.CreationTime = DateTime.Now;
            this.BaseHotels = new List<Hotel>();

            this.User = user;
        }

        public Booking UpdateParameters()
        {
            if (!(this.Status == BookingStatus.None))
                return this;

            if ((int)Prog.Terminal.RequestCache.linkId == 104101 ||
                (int)Prog.Terminal.RequestCache.linkId == 10410101 ||
                (int)Prog.Terminal.RequestCache.linkId == 10410301 ||
                (int)Prog.Terminal.RequestCache.linkId == 10410102)
            {
                if (!(Prog.Terminal.RequestCache.place == null))
                {
                    if (this.Place == (string)Prog.Terminal.RequestCache.place) ;
                    else
                    {
                        this.Place = (string)Prog.Terminal.RequestCache.place;
                        Prog.Content.LoadCity(this.Place);
                    }
                }


                this.CheckIn = ((string)Prog.Terminal.RequestCache.checkIn).DateTimeOrDefault().Value;
                this.CheckOut = ((string)Prog.Terminal.RequestCache.checkOut).DateTimeOrDefault().Value;
                this.RoomsCount = (int)Prog.Terminal.RequestCache.roomsCount;
                this.AdultsCount = (int)Prog.Terminal.RequestCache.adultsCount;

                string[] children = ((string)Prog.Terminal.RequestCache.children).Trim(';').Split(';');
                if (children.Length > 0)
                {
                    this.ChildrenCount = children[0].IntOrDefault();
                    this.ChildrenAge = children.Skip(1).Select(a => a.IntOrDefault(1)).ToList();
                }
            }
            else if ((int)Prog.Terminal.RequestCache.linkId == 104103)
            {
                if (!(Prog.Terminal.RequestCache.hotelId == null))
                    this.SelectedHotel = this.BaseHotels.Where(a => a.Code.ToString() == (string)Prog.Terminal.RequestCache.hotelId).FirstOrDefault();
            }
            else if ((int)Prog.Terminal.RequestCache.linkId == 10410302)
            {
                JArray selectedRates = JArray.Parse((string)Prog.Terminal.RequestCache.selectedRates);
                if (selectedRates.Count > 0)
                {
                    this.SelectedRates = new List<BookingRate>();
                    for (int i = 0; i < selectedRates.Count; i++)
                    {
                        JToken jsRate = selectedRates.Value<JToken>(i);
                        Rate rate = this.SelectedHotel.Rates.Where(a => a.Key == jsRate.Value<string>("rateCode")).FirstOrDefault();
                        this.SelectedRates.Add(new BookingRate(this, rate, jsRate.Value<int>("count")));
                    }
                }
            }
            else if ((int)Prog.Terminal.RequestCache.linkId == 104104)
            {
                this.BRefNo = (string)Prog.Terminal.RequestCache.bRefNo;

                JObject details = (JObject)Prog.Terminal.RequestCache.customerDetails;
                // List<Info> iCol = new List<Info>();
                // foreach (var detail in details)
                // iCol.Add(
                Info info = new Info()
                {
                    Title = (string)Prog.Terminal.RequestCache.customerDetails.title,
                    FirstName = (string)Prog.Terminal.RequestCache.customerDetails.firstName,
                    LastName = (string)Prog.Terminal.RequestCache.customerDetails.lastName,
                    Nationality = (string)Prog.Terminal.RequestCache.customerDetails.nationality,
                    Phone = (string)Prog.Terminal.RequestCache.customerDetails.phone,
                    Email = (string)Prog.Terminal.RequestCache.customerDetails.email
                };
                //TODO: apply specific info for each rate.
                // if (iCol.Count > 0)
                foreach (BookingRate bRate in this.SelectedRates)
                    bRate.CustomerInfo = info;
            }

            return this;
        }

        internal void Search()
        {
            this.BaseHotels = XmlDataSource.FetchHotels(this);
        }

        internal void SearchSelectedHotel()
        {
            this.SelectedHotel = XmlDataSource.FetchHotel(this);
        }

        internal void LoadHotels()
        {
            List<Hotel> hCol = new List<Hotel>();

            foreach (JToken jToken in Prog.Content.GetHotels())
                hCol.Add(new Hotel(jToken));

            this.BaseHotels = hCol;
            this.FilteredHotels = hCol;
            Misc.Log("LoadHotels {0}", this.FilteredHotels.Count);
        }

        internal List<Hotel> FilterAndSort(string filterBy, string sortBy, int? fromPrice, int? toPrice)
        {
            Misc.Log("Filters: {0} - {1} - {2} - {3}", filterBy, sortBy, fromPrice, toPrice);

            try
            {
                List<Hotel> hCol = new List<Hotel>();
                List<Hotel> temp = new List<Hotel>();

                if (string.IsNullOrEmpty(filterBy))
                    hCol.AddRange(this.BaseHotels);
                else
                {
                    if (filterBy.Contains("5S") ||
                        filterBy.Contains("4S") ||
                        filterBy.Contains("3S") ||
                        filterBy.Contains("2S") ||
                        filterBy.Contains("1S"))
                    {
                        if (filterBy.Contains("5S")) hCol.AddRange(this.BaseHotels.Where(a => a.Stars == 5));
                        if (filterBy.Contains("4S")) hCol.AddRange(this.BaseHotels.Where(a => a.Stars == 4));
                        if (filterBy.Contains("3S")) hCol.AddRange(this.BaseHotels.Where(a => a.Stars == 3));
                        if (filterBy.Contains("2S")) hCol.AddRange(this.BaseHotels.Where(a => a.Stars == 2));
                        if (filterBy.Contains("1S")) hCol.AddRange(this.BaseHotels.Where(a => a.Stars == 1));
                    }
                    else
                        hCol.AddRange(this.BaseHotels);

                    if (filterBy.Contains("mpRO") ||
                        filterBy.Contains("mpBB") ||
                        filterBy.Contains("mpHB") ||
                        filterBy.Contains("mpFB") ||
                        filterBy.Contains("mpAI"))
                    {
                        //if (!filterBy.Contains("mpRO")) hCol.RemoveAll(a => a.MealType == "RO");
                        //if (!filterBy.Contains("mpBB")) hCol.RemoveAll(a => a.MealType == "BB");
                        //if (!filterBy.Contains("mpHB")) hCol.RemoveAll(a => a.MealType == "HB");
                        //if (!filterBy.Contains("mpFB")) hCol.RemoveAll(a => a.MealType == "FB");
                        //if (!filterBy.Contains("mpAI")) hCol.RemoveAll(a => a.MealType == "AI");

                        temp = new List<Hotel>();
                        if (filterBy.Contains("mpRO")) temp.AddRange(hCol.Where(a => a.HasBoardType("RO")));
                        if (filterBy.Contains("mpBB")) temp.AddRange(hCol.Where(a => a.HasBoardType("BB")));
                        if (filterBy.Contains("mpHB")) temp.AddRange(hCol.Where(a => a.HasBoardType("HB")));
                        if (filterBy.Contains("mpFB")) temp.AddRange(hCol.Where(a => a.HasBoardType("FB")));
                        if (filterBy.Contains("mpAI")) temp.AddRange(hCol.Where(a => a.HasBoardType("AI")));

                        hCol = temp.Distinct().ToList();
                    }

                    if (filterBy.Contains("di_"))
                        if (filterBy.Contains("di_All")) ;
                        else
                        {
                            string value = new Regex("di_(?'re'.*?)_").Match(filterBy).Groups["re"].Value.ToLower();
                            Misc.Log(value);
                            temp = new List<Hotel>();
                            temp.AddRange(hCol.Where(a => a.District.ToLower().Contains(value)));

                            hCol = temp.ToList();
                        }

                    //if (hCol.Count == 0)
                    //    hCol.Add(Hotel.ErroneousHotel);
                }

                if (fromPrice == 0 && toPrice == 1000)
                    ;
                else if (fromPrice.HasValue && toPrice.HasValue)
                    hCol = hCol.Where(a => a.LowestRate.LastNet >= fromPrice && a.LowestRate.LastNet <= toPrice).ToList();

                if (string.IsNullOrEmpty(sortBy))
                    this.FilteredHotels = hCol.OrderBy(a => a.LowestRate.LastNet).ToList();
                else
                {
                    if (sortBy == "SPL")
                        this.FilteredHotels = hCol.OrderBy(a => a.LowestRate.LastNet).ToList();
                    else if (sortBy == "SPH")
                        this.FilteredHotels = hCol.OrderByDescending(a => a.LowestRate.LastNet).ToList();
                    else if (sortBy == "SSL")
                        this.FilteredHotels = hCol.OrderBy(a => a.Stars).ToList();
                    else if (sortBy == "SSH")
                        this.FilteredHotels = hCol.OrderByDescending(a => a.Stars).ToList();
                    //else if (sortBy == "SRH")
                    //    this.FilteredHotels = hCol.OrderByDescending(a => a.ReviewsCount).ToList();
                }

                //TODO
                if (Prog.Content.CityOrHotel(this.Place) == 1)
                {
                    Hotel hotel = this.FilteredHotels.Where(a => a.Name.ToLower() == this.Place.ToLower()).FirstOrDefault();
                    if (hotel == null)
                        hotel = new Hotel(Prog.Content.GetContentHotel(this.Place));
                    this.FilteredHotels.Insert(0, hotel);
                }
            }
            catch (Exception ex)
            {
                Misc.Log(ex.GetBaseException().ToString());
                this.FilteredHotels = new List<Hotel>();
            }
            return this.FilteredHotels;
        }

        internal void DBSave(bool withBookingRates)
        {
            DBBooking.Save(this);
            if (withBookingRates)
                foreach (BookingRate bRate in this.SelectedRates)
                    DBBookingRate.Save(bRate);
        }

        internal Booking ShallowCopy()
        {
            return (Booking)this.MemberwiseClone();
        }
    }

    public class Hotel
    {
        public int Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Stars { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public string Address { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }

        public List<string> Images { get; set; }
        public string DefaultImage { get; set; }

        public List<Room> Rooms { get; private set; }
        public List<Rate> Rates { get { return this.Rooms.SelectMany(a => a.Rates).OrderBy(a => a.LastNet).ToList(); } }

        public Rate LowestRate
        {
            get { return this.Rates.OrderBy(a => a.LastNet).FirstOrDefault(); }
        }

        public bool HasSoldOut { get { return !this.HasRates; } }
        public bool HasRates { get { return this.Rooms.SelectMany(a => a.Rates).Count() > 0; } }

        public JArray RoomsRaw { get; set; }
        public JArray ImagesRaw { get; set; }

        //public int ReviewsCount { get; set; }

        //public float TARating { get; internal set; }
        //public string TARatingImage { get; internal set; }

        internal Hotel()
        {
            this.Rooms = new List<Room>();
        }

        internal Hotel(JToken jHotel) : this()
        {
            this.Code = jHotel.Value<int>("code");
            this.Name = jHotel.Value<JToken>("name").Value<string>("content");
            this.Description = jHotel.Value<JToken>("description")?.Value<string>("content");
            this.Stars = Prog.Content.GetStars(jHotel.Value<string>("categoryCode"));
            this.City = Prog.Content.GetCity(jHotel.Value<string>("destinationCode"));
            this.District = Prog.Content.GetDistrict(jHotel.Value<string>("destinationCode"), jHotel.Value<int>("zoneCode"));
            this.Address = jHotel.Value<JToken>("address").Value<string>("content");
            this.Email = jHotel.Value<string>("email");
            this.Website = jHotel.Value<string>("web");
            this.Latitude = jHotel.Value<JToken>("coordinates")?.Value<float>("latitude") ?? 0;
            this.Longitude = jHotel.Value<JToken>("coordinates")?.Value<float>("longitude") ?? 0;

            this.RoomsRaw = jHotel.Value<JArray>("rooms");
            this.ImagesRaw = jHotel.Value<JArray>("images");
            this.Images = this.ImagesRaw?.OrderBy(a => a.Value<int>("order")).Values<string>("path").ToList();

            {
                JToken jImage = this.ImagesRaw?.Where(a => a.Value<string>("imageTypeCode") == "GEN").OrderBy(a => a.Value<int>("order")).FirstOrDefault();
                if (jImage == null)
                    jImage = this.ImagesRaw?.OrderBy(a => a.Value<int>("order")).FirstOrDefault();

                if (jImage == null)
                    this.DefaultImage = Misc.GetRandomImage(true);
                else
                    this.DefaultImage = jImage.Value<string>("path");
            }
        }

        internal bool HasBoardType(string boardCode)
        {
            //TODO: get new board codes
            return this.Rates.Where(r => r.BoardCode == boardCode).Count() > 0;
        }

        internal Hotel ShallowCopy(bool resetRooms)
        {
            Hotel hotel = (Hotel)this.MemberwiseClone();
            if (resetRooms)
                hotel.Rooms = new List<Room>();
            return hotel;
        }
    }

    public class Room
    {
        public Hotel Hotel { get; private set; }

        public string Code { get; set; }
        public string Name { get; set; }

        public List<Facility> Facilities { get; set; }
        public bool HasFacilities { get { return !(this.Facilities == null) && this.Facilities.Count > 0; } }

        public string Image
        {
            get
            {
                JToken jImage = this.Hotel.ImagesRaw?.Where(a => a.Value<string>("imageTypeCode") == "HAB" && a.Value<string>("roomCode") == this.Code).FirstOrDefault();
                if (jImage == null)
                    jImage = this.Hotel.ImagesRaw?.Where(a => a.Value<string>("imageTypeCode") == "HAB").FirstOrDefault();
                if (jImage == null)
                    return Misc.GetRandomImage(false);
                return jImage.Value<string>("path");
            }
        }

        public List<Rate> Rates { get; set; }
        public List<Rate> DistinctRates
        {
            get { return this.Rates.GroupBy(a => a.BoardName, (b, c) => c.OrderBy(d => d.LastNet).First()).ToList(); }
        }

        internal Room(Hotel hotel)
        {
            this.Hotel = hotel;
            this.Rates = new List<Rate>();
        }

        internal Room(Hotel hotel, string roomCode) : this(hotel)
        {
            JToken jContent = hotel.RoomsRaw.Where(a => a.Value<string>("roomCode") == roomCode).First();

            this.Code = jContent.Value<string>("roomCode");
            this.Name = Prog.Content.GetRoomName(jContent.Value<string>("roomCode"));

            //TODO: the array may exist, but has no items, so this object won't be null also it has 0 items, it may affect HasFacilities property.
            //          fixed it by checking Count > 0 in HasFacilities
            this.Facilities = jContent.Value<JArray>("roomFacilities")
                        ?.Select(a => new Facility(Prog.Content.GetTypeFacility(a.Value<int>("facilityCode"), a.Value<int>("facilityGroupCode"))))
                        .ToList();
        }
    }

    public class Rate
    {
        public Room Room { get; private set; }

        public string Key { get; set; }
        public string PaymentType { get; set; }
        public string BoardCode { get; set; }
        public int Allotment { get; set; }
        public float Net { get; set; }

        public List<Offer> Offers { get; internal set; }
        public List<Promotion> Promotions { get; internal set; }
        public List<Cancellation> Cancellation { get; set; }

        public string BoardName { get; set; }

        public bool HasOffers { get { return this.Offers?.Count > 0; } }
        public bool HasPromotions { get { return this.Promotions?.Count > 0; } }

        public float SumOfAppliedOffers
        {
            get { return (float)Math.Round(Math.Abs(this.Offers?.Where(a => a.Amount < 0).Sum(a => a.Amount) ?? 0)); }
        }

        public float SumOfProvidedOffers
        {
            get { return (float)Math.Round(this.Offers?.Where(a => a.Amount > 0).Sum(a => a.Amount) ?? 0); }
        }

        public float CancelledNet
        {
            get { return (float)Math.Round(this.Net + this.SumOfAppliedOffers); }
        }

        public float LastNet
        {
            get { return (float)Math.Round(this.Net - this.SumOfProvidedOffers); }
        }

        public string FormattedHtmlOffers
        {
            get { return this.Offers?.Aggregate(new StringBuilder(), (sb, i) => sb.AppendFormat("<label>{0}</label>", i.Name)).ToString(); }
        }

        public bool Cancellable
        {
            get { return this.FirstCancellation?.IsActive ?? false; }
        }

        public Cancellation FirstCancellation
        {
            get { return this.Cancellation?.OrderBy(a => a.From).FirstOrDefault(); }
        }

        //public bool HasCancellation
        //{
        //    get { return this.Cancellation?.Count > 0; }
        //}

        public Rate(Room room)
        {
            this.Room = room;
        }
    }

    public class BookingRate
    {
        internal long? bookingId { get; set; }
        public Booking Booking { get; internal set; }

        public long? Id { get; internal set; }

        public Rate Rate { get; private set; }

        public int Count { get; set; }
        public Info CustomerInfo { get; internal set; }

        public float TotalPrice
        {
            get { return (float)Math.Round(this.Rate.LastNet * this.Count); }
        }

        internal BookingRate() { }

        public BookingRate(Booking booking, Rate rate, int count)
        {
            this.Booking = booking;
            this.Rate = rate;
            this.Count = count;
        }
    }

    public class Cancellation
    {
        public float Amount { get; set; }
        public DateTime From { get; set; }

        public bool IsActive { get { return this.From > DateTime.UtcNow; } }

        public string FormattedFrom { get { return this.From.ToString("dd/MM/yyyy h:mm tt"); } }
        public string FormattedNote
        {
            get
            {
                if (this.IsActive)
                    return $"Free Cancellation until {this.From.ToString("ddd, MMM dd")}";
                return Misc.NonRefundableShort;
            }
        }

        public Cancellation(DateTime from, float amount)
        {
            this.From = from;
            this.Amount = amount;
        }
    }

    public class Promotion
    {
        public int Code { get; private set; }
        public string Name { get; private set; }
        public string Remark { get; private set; }

        public Promotion(int code, string name, string remark)
        {
            this.Code = code;
            this.Name = name;
            this.Remark = remark;
        }
    }

    public class Offer
    {
        public int Code { get; private set; }
        public string Name { get; private set; }
        public float Amount { get; private set; }

        public Offer(int code, string name, float amount)
        {
            this.Code = code;
            this.Name = name;
            this.Amount = amount;
        }
    }

    public class Facility
    {
        internal JToken JContent { get; private set; }

        public int Code { get { return this.JContent.Value<int>("code"); } }
        public int GroupCode { get { return this.JContent.Value<int>("facilityGroupCode"); } }
        public string Name { get { return this.JContent.Value<JToken>("description").Value<string>("content"); } }

        public string FormattedName
        {
            get
            {
                if (this.Code == 295 && this.GroupCode == 60)
                    return this.Name;
                return this.Name;
            }
        }

        public string Icon
        {
            get
            {
                if ((this.Code == 261 && this.GroupCode == 60) || (this.Code == 100 && this.GroupCode == 60))
                    return "fa fa-rss";
                else if ((this.Code == 295 && this.GroupCode == 70) || (this.Code == 250 && this.GroupCode == 60))
                    return "fa fa-wheelchair";
                else if (this.Code == 10 && this.GroupCode == 60)
                    return "im im-bathtub";
                else if (this.Code == 160 && this.GroupCode == 60)
                    return "fa fa-bars";
                else if (this.Code == 55 && this.GroupCode == 60)
                    return "im im-tv";
                else if (this.Code == 302 && this.GroupCode == 60)
                    return "im im-children";
                else if ((this.Code == 110 && this.GroupCode == 60) || (this.Code == 111 && this.GroupCode == 60) || (this.Code == 115 && this.GroupCode == 60))
                    return "im im-kitchen";
                else if (this.Code == 295 && this.GroupCode == 60)
                    return "im im-width";
                else if (this.Code == 220 && this.GroupCode == 60)
                    return "fa fa-users";
                return "fa fa-bath";
            }
        }

        public Facility(JToken jContent)
        {
            this.JContent = jContent;
        }
    }

    public class Info
    {
        public string Title { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Nationality { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }

        public string FullName { get { return $"{this.FirstName} {this.LastName}"; } }
        public string FullNameReversed { get { return $"{this.LastName}, {this.FirstName}"; } }

        public Info() { }
    }

    public class BookingInfo
    {
        internal long? userId;

        public long? Id { get; internal set; }

        public DateTime CheckIn { get; internal set; }
        public DateTime CheckOut { get; internal set; }
        public int RoomsCount { get; internal set; }
        public int AdultsCount { get; internal set; }
        public int ChildrenCount { get; internal set; }

        public string HotelName { get; internal set; }
        public string City { get; internal set; }
        public string BRefNo { get; internal set; }

        public float TotalPrice { get; internal set; }

        public BookingStatus BookingStatus { get; internal set; }
        public DateTime? CancellationDate { get; internal set; }

        public DateTime CreationDate { get; internal set; }

        public string MFirstName { get; internal set; }
        public string MLastName { get; internal set; }
        public string MEmail { get; internal set; }

        public string FormattedCheckIn { get { return this.CheckIn.ToString("dd/MM/yyyy"); } }
        public string FormattedCheckOut { get { return this.CheckOut.ToString("dd/MM/yyyy"); } }

        public List<BookingRateInfo> Rates { get; internal set; }
        public User User
        {
            get { return DBUser.Select(this.userId.Value).AsUser(); }
        }

        public string FormattedRoomsTypes
        {
            get { return this.Rates.Aggregate(new StringBuilder(), (sb, r) => sb.AppendFormat("{0}\t\t{1}", r.RoomName, r.Net).AppendLine()).ToString(); }
        }

        internal BookingInfo() { }

        internal BookingInfo(Booking booking)
        {
            this.FillFromBooking(booking);
        }

        public static BookingInfo GetBookingInfo(long bookingId, bool loadRates)
        {
            BookingInfo bookingInfo = DBBooking.Select(bookingId).AsBookingInfo();
            if (loadRates)
                bookingInfo.Rates = BookingRateInfo.GetRateInfo(bookingId, bookingInfo);
            return bookingInfo;
        }

        private void FillFromBooking(Booking booking)
        {
            this.CheckIn = booking.CheckIn;
            this.CheckOut = booking.CheckOut;
            this.RoomsCount = booking.RoomsCount;
            this.AdultsCount = booking.AdultsCount;
            this.ChildrenCount = booking.ChildrenCount;

            this.HotelName = booking.SelectedHotel.Name;
            this.City = booking.City;
            this.BRefNo = booking.BRefNo;

            this.TotalPrice = booking.TotalPrice;

            this.BookingStatus = booking.Status;
            if (booking.Cancellable) this.CancellationDate = booking.FirstActiveCancellation.From;

            this.CreationDate = booking.CreationTime;
            this.userId = booking.User.Id;

            this.MFirstName = booking.MainCustomerInfo.FirstName;
            this.MLastName = booking.MainCustomerInfo.LastName;
            this.MEmail = booking.MainCustomerInfo.Email;

            this.Rates = BookingRateInfo.GetRateInfo(booking.SelectedRates, this);
        }

        internal bool Perform(int bookingActionId)
        {
            Misc.Log($"Perform - {this.Id} - {bookingActionId}");
            Prog.Terminal.User.AssertAction(bookingActionId);

            if (bookingActionId == ActionPermission.ABookingConfirm)
            {
                if (Prog.Terminal.User.Company.ReservedBalance >= this.TotalPrice)
                    this.BookingStatus = BookingStatus.Confirmed;
            }
            else if (bookingActionId == ActionPermission.ABookingReConfirm)
            {
                if (Prog.Terminal.User.Company.ActualBalance >= this.TotalPrice)
                    this.BookingStatus = BookingStatus.ReConfirmed;
            }
            else if (bookingActionId == ActionPermission.ABookingCancel)
                this.BookingStatus = BookingStatus.Cancelled;

            this.DBSave(true);
            return true;
        }

        internal void SendEmail()
        {
            string body = $"Dear {Prog.Terminal.User.FirstName}, \n\nWe would like to inform you that you've just booked the following rooms\nHotel: {this.HotelName}\nRoom Type: {this.FormattedRoomsTypes}\nCheck in: {this.CheckIn.ToLongDateString()}\nCheck out: {this.CheckOut.ToLongDateString()}\nTotal Cost: ${this.TotalPrice}";
            body += $"\nBooking State: {this.BookingStatus.ToString()}";
            body += "\n\nThank you for using our services.";

            Mail.Send("ALTI - Booking Notification", body, this.MEmail, Prog.Terminal.User.Email);
        }

        internal bool ProceedByBalance()
        {
            if (this.BookingStatus == BookingStatus.None)
            {
                if (this.CancellationDate.HasValue)
                {
                    if (Prog.Terminal.User.Company.ReservedBalance >= this.TotalPrice)
                        this.BookingStatus = BookingStatus.Confirmed;
                    else
                        this.BookingStatus = BookingStatus.Pending;
                }
                else if (Prog.Terminal.User.Company.ActualBalance >= this.TotalPrice)
                    this.BookingStatus = BookingStatus.ReConfirmed;
                else
                    return false;


                //TODO: set User.CurrentBooking as null after saving in order to prevent saving it many times.
                this.DBSave(true);
                this.SendEmail();
                return true;
            }
            return false;
        }

        internal void ExportToPDF(string html, bool portrait, bool openInBrowser)
        {
            //Misc.SaveToFile(html);
            //string path = Misc.GetFilePath($"content/files/{Guid.NewGuid()}.pdf");

            SelectPdf.HtmlToPdf converter = new SelectPdf.HtmlToPdf();
            if (portrait)
                converter.Options.PdfPageOrientation = SelectPdf.PdfPageOrientation.Portrait;
            else
                converter.Options.PdfPageOrientation = SelectPdf.PdfPageOrientation.Landscape;

            converter.Options.MarginLeft = 10;
            converter.Options.MarginTop = 10;
            converter.Options.MarginRight = 10;
            converter.Options.MarginBottom = 10;

            SelectPdf.PdfDocument doc = converter.ConvertHtmlString(html, HttpContext.Current.Request.Url.AbsoluteUri);
            doc.Save(HttpContext.Current.Response, openInBrowser, "report.pdf");
            //doc.Save(path);
            doc.Close();
        }

        internal void DBSave(bool withRates)
        {
            DBBooking.Save(this);
            if (withRates)
                foreach (BookingRateInfo rInfo in this.Rates)
                    rInfo.DBSave();
        }
    }

    public class BookingRateInfo
    {
        internal long? Id { get; set; }

        internal string RateCode { get; set; }

        public string RoomName { get; internal set; }
        public string BoardName { get; internal set; }

        public float Net { get; internal set; }
        public int Count { get; internal set; }

        public string FirstName { get; internal set; }
        public string LastName { get; internal set; }
        public string Nationality { get; internal set; }
        public string Mobile { get; internal set; }
        public string Email { get; internal set; }

        public BookingInfo BookingInfo { get; internal set; }

        internal BookingRateInfo() { }

        internal static List<BookingRateInfo> GetRateInfo(long bookingId, BookingInfo bookingInfo)
        {
            return DBBookingRate.Selects(bookingId).Select(a => a.AsBookingRateInfo(bookingInfo)).ToList();
        }

        internal static List<BookingRateInfo> GetRateInfo(List<BookingRate> bookingRate, BookingInfo bookingInfo)
        {
            return bookingRate.Select(a => BookingRateInfo.AsRateInfo(a, bookingInfo)).ToList();
        }

        private static BookingRateInfo AsRateInfo(BookingRate bookingRate, BookingInfo bookingInfo)
        {
            BookingRateInfo rateInfo = new BookingRateInfo();

            rateInfo.RateCode = bookingRate.Rate.Key;

            rateInfo.RoomName = bookingRate.Rate.Room.Name;
            rateInfo.BoardName = bookingRate.Rate.BoardName;

            rateInfo.Net = bookingRate.Rate.LastNet;
            rateInfo.Count = bookingRate.Count;

            rateInfo.FirstName = bookingRate.CustomerInfo.FirstName;
            rateInfo.LastName = bookingRate.CustomerInfo.LastName;
            rateInfo.Nationality = bookingRate.CustomerInfo.Nationality;
            rateInfo.Mobile = bookingRate.CustomerInfo.Phone;
            rateInfo.Email = bookingRate.CustomerInfo.Email;

            rateInfo.BookingInfo = bookingInfo;
            return rateInfo;
        }

        internal void DBSave()
        {
            DBBookingRate.Save(this);
        }
    }

    public class Report
    {
        internal static void ToExcel(Booking booking)
        {
            StringBuilder sb = new StringBuilder();

            Excel.Application app = new Excel.Application();
            Excel.Workbook workbook = app.Workbooks.Open(Misc.GetFilePath("content/files/reportUn.xls"));
            Excel._Worksheet worksheet = workbook.Sheets[1];
            Excel.Range xlRange = worksheet.UsedRange;

            Excel.Range range = null;

            range = worksheet.Range["D5", "H5"];
            range.Value = $"CONFIRMATION {booking.User.Company.Name}";

            range = worksheet.Range["C7", "D7"];
            range.Value = booking.User.Company.Name;

            range = worksheet.Range["I7", "J7"];
            range.Value2 = Guid.NewGuid().ToString();

            range = worksheet.Range["I7", "J7"];
            range.Value2 = booking.CreationTime.ToShortDateString();

            range = worksheet.Range["C10", "D10"];
            range.Value2 = booking.User.FullName;

            range = worksheet.Range["I10", "J10"];
            range.Value2 = booking.CheckIn.ToShortDateString();

            range = worksheet.Range["C11", "D11"];
            range.Value2 = booking.User.Company.Name;

            range = worksheet.Range["I11", "J11"];
            range.Value2 = booking.CheckOut.ToShortDateString();

            range = worksheet.Range["C13", "F13"];
            range.Value2 = $"{booking.MainCustomerInfo.LastName}, {booking.MainCustomerInfo.FirstName}";

            range = worksheet.Range["I13", "J13"];
            range.Value2 = booking.AdultsCount;

            range = worksheet.Range["C15", "D15"];
            range.Value2 = booking.User.FullName;

            //int rowCount = xlRange.Rows.Count;
            //int colCount = xlRange.Columns.Count;

            //for (int i = 1; i <= rowCount; i++)
            //{
            //    for (int j = 1; j <= colCount; j++)
            //    {
            //        if (j == 1)
            //            sb.AppendLine();

            //        if (xlRange.Cells[i, j] != null && xlRange.Cells[i, j].Value2 != null)
            //            sb.AppendLine(xlRange.Cells[i, j].Value2.ToString() + "\t");
            //    }
            //}

            worksheet.PageSetup.Orientation = Excel.XlPageOrientation.xlLandscape;
            worksheet.ExportAsFixedFormat(Excel.XlFixedFormatType.xlTypePDF, Filename: Misc.GetFilePath($"content/files/{HttpContext.Current.Session.SessionID}.pdf"));

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Marshal.ReleaseComObject(range);

            Marshal.ReleaseComObject(xlRange);
            Marshal.ReleaseComObject(worksheet);

            workbook.Close(SaveChanges: false);
            Marshal.ReleaseComObject(workbook);

            app.Quit();
            Marshal.ReleaseComObject(app);

            //Misc.Log(sb.ToString());
        }
    }

    public class Company
    {
        public long? Id { get; internal set; }

        public bool IsActive { get; internal set; }
        public bool IsApproved { get; internal set; }

        public string Name { get; internal set; }
        public string ShortName { get; internal set; }
        public string CommercialRegistrationNo { get; internal set; }
        public string Country { get; internal set; }
        public string City { get; internal set; }
        public string POBox { get; internal set; }
        public string ZipCode { get; internal set; }
        public string Address { get; internal set; }
        public string LandLineNo { get; internal set; }
        public string FaxNo { get; internal set; }
        public string WebSite { get; internal set; }

        public string Logo { get; internal set; }
        public string BigLogo { get; internal set; }
        public string MiniLogo { get; internal set; }

        public int CreditLimit { get; internal set; }
        public float Profit { get; internal set; }
        //public string Remarks { get; internal set; }

        internal float reservedBalance;
        internal float actualBalance;

        public float ReservedBalance
        {
            //TODO: sum all pending, confirmed, reconfirmed
            get { return this.reservedBalance - DBBooking.GetSum(this.Id, BookingStatus.PendingOrConfirmed); }
        }

        public float ActualBalance
        {
            get { return this.actualBalance - DBBooking.GetSum(this.Id, BookingStatus.ReConfirmed); }
        }

        public float ActualBalanceInfo { get { return this.actualBalance; } }

        private User agent;
        public User Agent
        {
            get
            {
                if (this.agent == null)
                    this.agent = DBUser.GetAgent(this.Id.Value);
                return this.agent;
            }
            set
            {
                this.agent = value;
            }
        }

        private List<User> subUsers;
        public List<User> SubUsers
        {
            get
            {
                if (this.subUsers == null)
                    this.subUsers = DBUser.GetSubUsers(this.Id.Value);
                return this.subUsers;
            }
            set
            {
                this.subUsers = value;
            }
        }

        public DateTime CreationDate { get; set; }

        public List<DBBooking> BookingHistory
        {
            get { return DBBooking.Selects(this.Id.Value); }
        }

        public float SumOfPendingOrConfirmed
        {
            get { return DBBooking.GetSum(this.Id, BookingStatus.PendingOrConfirmed); }
        }

        public float SumOfReConfirmed
        {
            get { return DBBooking.GetSum(this.Id, BookingStatus.ReConfirmed); }
        }

        internal static Company Default
        {
            get
            {
                return new Company(Misc.DefaultCompanyName) { Address = Misc.DefaultCompanyAddress };
            }
        }

        public static List<Company> Companies
        {
            get { return DBCompany.Selects().Select(a => a.AsCompany()).ToList(); }
        }

        internal Company()
        {
            this.CreationDate = DateTime.Now;

            //TODO
            this.Logo = Misc.DefaultCompanyLogo;
            this.BigLogo = Misc.DefaultCompanyLogoBig;
            this.MiniLogo = Misc.DefaultCompanyLogoMini;
            this.ZipCode = "12345";

            this.CreditLimit = 50000;
            this.Profit = 0;

            this.reservedBalance = 50000;
            this.actualBalance = 5000;
        }

        public Company(string name) : this()
        {
            this.Name = name;
        }

        public Company(dynamic jCompany) : this()
        {
            this.Serialize(jCompany);
        }

        public static Company GetCompany(long companyId)
        {
            return DBCompany.Select(companyId).AsCompany();
        }

        public Company Serialize(dynamic jCompany)
        {
            if (!(jCompany.name == null)) this.Name = jCompany.name;
            if (!(jCompany.shortName == null)) this.ShortName = jCompany.shortName;
            if (!(jCompany.regNo == null)) this.CommercialRegistrationNo = jCompany.regNo;
            if (!(jCompany.country == null)) this.Country = jCompany.country;
            if (!(jCompany.city == null)) this.City = jCompany.city;
            if (!(jCompany.pobNo == null)) this.POBox = jCompany.pobNo;
            if (!(jCompany.zipCode == null)) this.ZipCode = jCompany.zipCode;
            if (!(jCompany.address == null)) this.Address = jCompany.address;
            if (!(jCompany.landLine == null)) this.LandLineNo = jCompany.landLine;
            if (!(jCompany.fax == null)) this.FaxNo = jCompany.fax;
            if (!(jCompany.website == null)) this.WebSite = jCompany.website;

            if (!(jCompany.creditLimit == null)) this.CreditLimit = jCompany.creditLimit;
            if (!(jCompany.profit == null)) this.Profit = jCompany.profit;

            if (!(jCompany.isActive == null)) this.IsActive = jCompany.isActive;
            if (!(jCompany.isApproved == null)) this.IsApproved = jCompany.isApproved;

            return this;
        }

        public Company SerializeWithAgent(dynamic jObject)
        {
            this.Serialize(new DynamicBag(jObject.companyInfo));
            this.Agent.Serialize(new DynamicBag(jObject.agentInfo));

            string remarks = jObject.remarks;
            bool isSendEmail = jObject.isSendEmail;

            if (isSendEmail)
                Mail.Send("ALTI Booking System", remarks, this.Agent.Email, null);

            return this;
        }

        internal void DBSave(bool withAgent, bool withSubusers)
        {
            DBCompany.Save(this);

            if (withAgent)
                this.Agent.DBSave();

            if (withSubusers)
                foreach (User subUser in this.SubUsers)
                    subUser.DBSave();
        }

        internal User GetSubuser(long userId)
        {
            return this.SubUsers.Where(a => a.Id == userId).FirstOrDefault();
        }
    }

    public class User
    {
        internal long? companyId;

        public long? Id { get; internal set; }

        public bool IsActive { get; internal set; }

        public string FirstName { get; internal set; }
        public string LastName { get; internal set; }
        public string Email { get; internal set; }
        public string Password { get; internal set; }
        public string Position { get; internal set; }
        public string PhoneNo { get; internal set; }
        public string MobileNo { get; internal set; }
        public string FaxNo { get; internal set; }

        public string Permissions { get; internal set; }

        public UserType Type { get; internal set; }

        private Company company = null;
        public Company Company
        {
            get
            {
                if (this.company == null)
                    this.company = DBCompany.Select(this.companyId.Value)?.AsCompany();
                return this.company;
            }
            set
            {
                this.company = value;
            }
        }

        public DateTime CreationDate { get; internal set; }
        public DateTime LastLoginTime { get; internal set; }

        public bool IsBuiltInUser { get { return this.Id == 0; } }

        public bool IsGuest { get { return this.Type == UserType.Guest; } }
        public bool IsAdmin { get { return this.Type == UserType.Admin; } }
        public bool IsAgent { get { return this.Type == UserType.Agent; } }
        public bool IsSubuser { get { return this.Type == UserType.AgentSubUser; } }

        public string FullName { get { return String.Format($"{this.FirstName} {this.LastName}"); } }

        public static User Guest
        {
            get { return new User("Guest", null, "guest@mail.com", UserType.Guest) { Company = Company.Default }; }
        }

        public static User Admin
        {
            get { return new User("Admin", null, "admin@mail.com", UserType.Admin) { Company = Company.Default }; }
        }

        internal User()
        {
            this.Password = Prog.Database.GeneratePassword();

            this.CreationDate = DateTime.Now;
            this.LastLoginTime = DateTime.Now;

            //TODO
            this.IsActive = true;
        }

        public User(string firstName, string lastName, string email, UserType userType) : this()
        {
            this.FirstName = firstName;
            this.LastName = lastName;
            this.Email = email;
            this.Type = userType;
        }

        public User(UserType userType, Company company, dynamic jUser) : this()
        {
            this.Type = userType;
            this.Company = company;

            this.Serialize(jUser);
        }

        public User Serialize(dynamic jUser)
        {
            if (jUser == null) return this;

            if (!(jUser.firstName == null)) this.FirstName = jUser.firstName;
            if (!(jUser.lastName == null)) this.LastName = jUser.lastName;
            if (!(jUser.email == null)) this.Email = jUser.email;
            if (!(jUser.position == null)) this.Position = jUser.position;
            if (!(jUser.phoneNo == null)) this.PhoneNo = jUser.phoneNo;
            if (!(jUser.mobileNo == null)) this.MobileNo = jUser.mobileNo;
            if (!(jUser.faxNo == null)) this.FaxNo = jUser.faxNo;

            if (!(jUser.isActive == null)) this.IsActive = jUser.isActive;
            if (!(jUser.lstPermission == null)) this.Permissions = jUser.lstPermission;
            return this;
        }

        public bool HasPermission(int permissionId)
        {
            return ActionPermission.HasPermission(this, permissionId);
        }

        public void AssertAction(int actionId)
        {
            ActionPermission.AssertAction(this, actionId);
        }

        internal void DBSave()
        {
            DBUser.Save(this);
        }
    }

    public class ActionPermission
    {
        public const int PBookingView = 101101;
        public const int PBookingConfirm = 101102;
        public const int PBookingReConfirm = 101103;
        public const int PBookingCancel = 101104;
        public const int PBookingPrint = 101105;
        public const int PAccountingViewStatement = 102101;
        public const int PAccountingViewBalance = 102102;
        public const int PMiscAddSubuser = 103101;
        public const int PMiscEditSubuser = 103102;

        public const int ABookingView = 101101;
        public const int ABookingConfirm = 101102;
        public const int ABookingReConfirm = 101103;
        public const int ABookingCancel = 101104;
        public const int ABookingPrint = 101105;
        public const int AAccountingViewStatement = 102101;
        public const int AAccountingViewBalance = 102102;
        public const int AMiscAddSubuser = 103101;
        public const int AMiscEditSubuser = 103102;

        public static void AssertAction(User user, int actionId)
        {
            //TODO: retrieve specific permission for actions, here the permissionId is the same as actionId.
            int permissionId = actionId;

            if (ActionPermission.HasPermission(user, permissionId)) return;
            throw new PermissionException();
        }

        internal static bool HasPermission(User user, int permissionId)
        {
            if (user.Type == UserType.Admin || user.Type == UserType.Agent)
                return true;
            if (user.Permissions == null)
                return false;
            return user.Permissions.Contains($";{permissionId};");
        }
    }

    public class Mail
    {
        public static bool Send(string subject, string body, string toA, string toB)
        {
            try
            {
                MailMessage message = new MailMessage();
                message.From = new MailAddress("msite@altitravel.com", "altitravel");

                if (!string.IsNullOrEmpty(toA))
                    message.To.Add(new MailAddress(toA));
                if (!string.IsNullOrEmpty(toB))
                    message.To.Add(new MailAddress(toB));

                message.Subject = subject;
                message.Body = body;

                SmtpClient smtp = new SmtpClient("altitravel.com", 587);
                smtp.Credentials = new NetworkCredential("msite@altitravel.com", "");
                smtp.Send(message);
                return true;
            }
            catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); return false; }
        }
    }

    public class IPInfo
    {
        public JObject ipInfo;
        public string City { get { return (string)this.ipInfo?["city"]; } }

        public IPInfo()
        {
            this.ipInfo = Misc.GetIPInfo(HttpContext.Current.Request.UserHostAddress);
        }
    }

    public class Settings
    {
        internal Random Random;

        public string SignInBackground { get { return String.Format("SignInBackground_{0}.jpg", this.Random.Next(0, 14)); } }
        public string MainBackground { get { return String.Format("MainBackground_{0}.jpg", this.Random.Next(0, 9)); } }
        public string Logo { get { return "defaultLogo.png"; } }
        public DateTime CreationTime { get; set; }

        private Settings()
        {
            this.Random = new Random();
            this.CreationTime = DateTime.Now;
        }

        internal static Settings GetSettings()
        {
            return new Settings();
        }
    }

    public class Misc
    {
        private static readonly object _sync = new object();

        internal static string DefaultCompanyName = "ALTI Group, ALTI Travel & Tours";
        internal static string DefaultCompanyAddress = "JORDAN, UAE/DUBAI, TURKEY, OMAN, LEBANON & JERUSALEM, Head Office Amman";
        internal static string DefaultCompanyLogo = "defaultLogo.png";
        internal static string DefaultCompanyLogoBig = "amaze_300x300.jpg";
        internal static string DefaultCompanyLogoMini = "amaze_40x40.jpg";

        public static string NonRefundableShort = "Non-refundable rate.";
        public static string NoResults = "We couldn't find properties that match your filter selections. please refine your search filters.";
        public static string NonRefundable = "This rate is non-refundable.If you choose to change or cancel this booking you will not be refunded any of the payment.";
        public static string PermissionRequired = "You do not have permission. please contact your system administrator.";

        internal static JObject requestJsonString { get { return JObject.Parse(HttpContext.Current.Request["jsonString"] ?? "{}"); } }

        internal static JObject GetIPInfo(string ipAddress)
        {
            try
            {
                JObject jObj = JObject.Parse(Misc.DownloadString($"http://ipinfo.io/{ipAddress}/json"));
                //JObject jObj = JObject.Parse(Misc.DownloadString($"http://ipinfo.io/92.241.39.200/json"));
                Misc.Log(jObj?.ToString());
                return jObj;
            }
            catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); return null; }
        }

        public static string GetWeather(string city)
        {
            try
            {
                if (city == null)
                    city = Prog.Terminal.IPInfo.City;
                city = HttpContext.Current.Server.HtmlEncode(city);
                // string query = string.Format("http://api.openweathermap.org/data/2.5/weather?APPID=9f0b9b4e06cf9fda1ee67f4a5bcb99d9&units=metric&mode=json&q={0}", city);
                // JObject jObj = JObject.Parse(Misc.DownloadString(query));
                // return $"{jObj["main"]["temp"]} {jObj["weather"][0]["description"]}";

                string queryHTML = Misc.DownloadString(string.Format("http://api.openweathermap.org/data/2.5/weather?APPID=9f0b9b4e06cf9fda1ee67f4a5bcb99d9&units=metric&mode=html&q={0}", city));
                Match m = System.Text.RegularExpressions.Regex.Match(queryHTML, ".*?<body>(?'Go'.*?)</body>", RegexOptions.Singleline);

                return m.Groups["Go"].Value.Replace("color: gray", "color: white").Replace("font-size: medium", "font-size: large");
            }
            catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); return null; }
        }

        public static string AsString(Dictionary<string, string> source, string delimiter)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> value in source)
                sb.AppendFormat("{0}: {1}{2} ", value.Key, value.Value, delimiter);
            return null;
        }

        internal static string DownloadString(string url)
        {
            Misc.Log(url);
            try
            {
                //TODO: reduce the time to 400 or 40 and test to check if it's working
                return new TimedWebClient(1 * 1000) { Encoding = Encoding.UTF8 }.DownloadString(url);
                //return new StreamReader(HttpWebRequest.Create(url).GetResponse().GetResponseStream()).ReadToEnd();
            }
            catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); return null; }
        }

        internal static string GetMealType(string mealType)
        {
            mealType = mealType.ToUpper();

            if (mealType == "RO")
                return "Room Only";
            else if (mealType == "BB")
                return "Bed & Breakfast";
            else if (mealType == "HB")
                return "Half-Board";
            else if (mealType == "FB")
                return "Full-Board";
            else if (mealType == "AI")
                return "All Inclusive";

            return null;
        }

        public static string GetRandomImage(bool hotel)
        {
            //if (hotel)
            return $"../img/random/img ({Prog.Settings.Random.Next(59, 82)}).jpg";
        }

        internal static string GetFilePath(string fileName)
        {
            return HttpContext.Current.Server.MapPath($"~/{fileName}");
        }

        internal static string DecompressGZip(byte[] data)
        {
            MemoryStream toStream = new MemoryStream();
            Misc.DecompressGZip(new MemoryStream(data), toStream);
            return UTF8Encoding.UTF8.GetString(toStream.ToArray());
        }

        internal static void DecompressGZip(Stream from, Stream to)
        {
            using (GZipStream stream = new GZipStream(from, CompressionMode.Decompress))
                stream.CopyTo(to);
        }

        public static void Log(string name, JObject jObject)
        {
            Misc.Log("{0}: {1}", name, jObject.ToString().Replace("{", "[").Replace("}", "]"));
        }

        public static void Log(string text, params object[] args)
        {
            if (args.Length == 0)
                Misc.LogInternal(text);
            else
                Misc.LogInternal(string.Format(text, args));
        }

        private static void LogInternal(string text, bool flush = false)
        {
            lock (Misc._sync)
            {
                Prog.Log.Add($"{DateTime.Now.ToString()}\t{text}");

                if (Prog.Log.Count > 2500 || flush)
                {
                    if (!(HttpContext.Current == null))
                        File.AppendAllLines(Misc.GetFilePath("content/files/log.log"), Prog.Log);

                    Prog.Log.Clear();
                }
            }
        }

        public static void FlushLog()
        {
            Misc.LogInternal("flush", true);
        }

        internal static void SaveToFile(object data)
        {
            Misc.SaveToFile(data, null);
        }

        internal static void SaveToFile(object data, string fileName)
        {
            string path = Misc.GetFilePath($"content/files/{DateTime.Now.ToString("yyyyMMddHHmmss")}.json");

            if (!(fileName == null))
                path = Misc.GetFilePath($"content/files/{fileName}");

            if (data is string)
                File.AppendAllText(path, (string)data);
            else if (data is byte[])
                using (FileStream fStr = File.OpenWrite(path))
                {
                    fStr.Seek(0, SeekOrigin.End);
                    fStr.Write((byte[])data, 0, ((byte[])data).Length);
                }
            else if (data is JObject)
                using (StreamWriter sWriter = new StreamWriter(File.OpenWrite(path)))
                using (JsonTextWriter jWriter = new JsonTextWriter(sWriter))
                {
                    sWriter.BaseStream.Seek(0, SeekOrigin.End);
                    ((JObject)data).WriteTo(jWriter);
                }
        }

        internal static JArray GetMatch(IEnumerable<string> source, string item)
        {
            Misc.Log("GetMatch: {0}", item);

            item = item.ToLower();
            JArray jArr = new JArray();
            foreach (string name in source)
                if (name.ToLower().Contains(item))
                {
                    jArr.Add(name);
                    if (jArr.Count > 3)
                        break;
                }
            return jArr;
        }

        public static string Serialize(IEnumerator items)
        {
            StringBuilder sb = new StringBuilder();

            items.Reset();
            while (items.MoveNext())
                sb.AppendFormat("{0}, ", items.Current);
            return sb.ToString();
        }

        public static string AggregateAsString(IEnumerable<string> source)
        {
            return source.Aggregate(new StringBuilder(), (sb, item) => sb.AppendLine(item)).ToString();
        }
    }

    public class DynamicBag : DynamicObject
    {
        private JObject jObject;
        private Dictionary<string, dynamic> dic;
        private bool emptyAsNull;

        public DynamicBag(JObject jObject, bool emptyAsNull = true)
        {
            this.jObject = jObject;
            this.emptyAsNull = emptyAsNull;
            this.dic = new Dictionary<string, dynamic>();
        }

        public override bool TryGetMember(GetMemberBinder binder, out dynamic result)
        {
            if (this.jObject[binder.Name] == null)
                result = null;
            else
            {
                if (this.emptyAsNull && string.IsNullOrEmpty(this.jObject[binder.Name].ToString()))
                    result = null;
                else
                    result = this.jObject[binder.Name];
            }

            // result = (this.jObject[binder.Name] == null ? null : this.jObject[binder.Name]);
            // result = (this.dic.ContainsKey(binder.Name) ? this.dic[binder.Name] : null);
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            if (value == null)
                this.jObject.Remove(binder.Name);
            else
                this.jObject[binder.Name] = JToken.FromObject(value);

            return true;

            //if (value == null)
            //{
            //    if (this.dic.ContainsKey(binder.Name))
            //        this.dic.Remove(binder.Name);
            //}
            //else
            //    this.dic[binder.Name] = value;
            //return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = null;
            if (binder.Name == "updateJObject")
            {
                Misc.Log("BeforeUpdating", this.jObject);
                result = this.jObject.Update((JObject)args[0]);
                Misc.Log("AfterUpdating", this.jObject);
                return true;
            }
            else if (binder.Name == "getJObject")
            {
                result = this.jObject;
                return true;
            }
            return false;
        }

        public override string ToString()
        {
            return this.jObject.ToString();
        }
    }

    public class IRoomComparer : IEqualityComparer<Room>
    {
        public bool Equals(Room x, Room y)
        {
            return x.Code == y.Code;
        }

        public int GetHashCode(Room obj)
        {
            return 0;
        }
    }

    public class IRateComparer : IEqualityComparer<Rate>
    {
        public bool Equals(Rate x, Rate y)
        {
            return x.BoardCode == y.BoardCode;
        }

        public int GetHashCode(Rate obj)
        {
            return 0;
        }
    }

    public class HTMLElements
    {
        public static string GetCircledButtons(int count, dynamic selected)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.AppendFormat("<label class=\"btn btn - primary{0}\"><input type=\"radio\" name=\"options\" />{1}</label>",
                    ((i + 1 == (int)selected) ? " active" : string.Empty),
                    i + 1);
            }
            sb.AppendFormat("<label class=\"btn btn - primary\"><input type=\"radio\" name=\"options\" />{0}+</label>",
                    count);
            return sb.ToString();
        }

        public static string GetOptions(int start, int count, dynamic selected)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = start; i <= count; i++)
                sb.AppendFormat("<option{0}>{1}</option>", ((i == (int)selected) ? " selected=\"selected\"" : string.Empty), i);
            return sb.ToString();
        }

        public static string GetChildrenAge(List<int> ages)
        {
            if (ages == null || ages.Count == 0) return null;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ages.Count; i++)
                sb.AppendFormat("<div class=\"col-md-2 form-group form-group form-group-select-plus\"><label>Child {0} Age</label><select class=\"form-control cmbChild2\">{1}</select></div>",
                    i + 1,
                    HTMLElements.GetOptions(1, 9, ages[i]));
            return sb.ToString();
        }
    }

    public class ContentProvider
    {
        internal string City;
        private string[] cities = { "amman", "beirut", "cairo", "dubai", "istanbul", "muscat" };

        private string[] dubaiDistricts = { "Al Barsha", "Bur Dubai", "Deira", "Downtown Dubai", "Dubai Marina", "Dubai Media City", "Jebel Ali", "Jumeirah Beach", "Palm Jumeirah" };

        private List<DBHotel> dbHotels;
        private JArray jContentHotels;

        private JArray jTypesRooms;
        private JArray jTypesBoards;
        private JArray jTypesCategories;
        private JArray jTypesDestinations;
        private JArray jTypesFacilities;

        private SortedList<int, int> GoGlobalLinks;
        private SortedList<int, int> RestelLinks;

        private List<string> hotelsNames;
        public List<string> HotelsNames
        {
            get
            {
                if (this.hotelsNames == null)
                    this.hotelsNames = this.dbHotels.Select(a => a.Name).ToList();
                return this.hotelsNames;
            }
        }

        public ContentProvider()
        {
            this.jTypesRooms = JToken.Parse(File.ReadAllText(Misc.GetFilePath("content/hotelBeds/rooms.json"))).Value<JArray>("rooms");
            this.jTypesBoards = JToken.Parse(File.ReadAllText(Misc.GetFilePath("content/hotelBeds/boards.json"))).Value<JArray>("boards");
            this.jTypesCategories = JToken.Parse(File.ReadAllText(Misc.GetFilePath("content/hotelBeds/categories.json"))).Value<JArray>("categories");
            this.jTypesDestinations = JToken.Parse(File.ReadAllText(Misc.GetFilePath("content/hotelBeds/destinationsCompact.json"))).Value<JArray>("destinations");
            this.jTypesFacilities = JToken.Parse(File.ReadAllText(Misc.GetFilePath("content/hotelBeds/facilities.json"))).Value<JArray>("facilities");

            this.LoadHotels();

            this.GoGlobalLinks = new SortedList<int, int>();
            this.RestelLinks = new SortedList<int, int>();

            string[] values = null;
            foreach (var line in File.ReadAllLines(Misc.GetFilePath("content/files/cross.txt")))
            {
                values = line.Split(',');
                if (values[1].Length > 0)
                    this.RestelLinks.Add(int.Parse(values[1]), int.Parse(values[0]));
                if (values[2].Length > 0)
                    this.GoGlobalLinks.Add(int.Parse(values[2]), int.Parse(values[0]));
            }
        }

        private void LoadHotels()
        {
            Misc.Log("LoadHotels");

            this.dbHotels = new List<DBHotel>();
            foreach (string city in this.cities)
            {
                using (StreamReader sr = File.OpenText(Misc.GetFilePath($"content/hotelBeds/hotels_{city}.json")))
                using (JsonTextReader jReader = new JsonTextReader(sr))
                {
                    JArray jHotels = JToken.ReadFrom(jReader).Value<JArray>("hotels");
                    foreach (JToken jHotel in jHotels)
                        this.dbHotels.Add(new DBHotel()
                        {
                            Code = jHotel.Value<int>("code"),
                            Name = jHotel.Value<JToken>("name").Value<string>("content").ToLower(),
                            City = city // jHotel.Value<JToken>("city").Value<string>("content").ToLower()
                        });
                }
            }

            Misc.Log("LoadHotels, Count: {0}", this.dbHotels.Count);
        }

        internal List<int> GetHotelsIds(string city)
        {
            return this.dbHotels.Where(a => a.City == city).Select(b => b.Code).ToList();
        }

        internal List<JToken> GetHotels()
        {
            return this.jContentHotels.Children<JToken>().ToList();
        }

        internal int CityOrHotel(string name)
        {
            name = name.Split(',')[0].Trim().ToLower();

            if (this.cities.Contains(name))
                return 0;
            else if (this.dbHotels.Where(a => a.Name == name).Count() > 0)
                return 1;
            return -1;
        }

        internal void LoadCity(string cityOrHotelName)
        {
            //TODO: check city file if exist

            cityOrHotelName = cityOrHotelName.Split(',')[0].Trim().ToLower();

            if (this.City == cityOrHotelName)
                return;

            int typeOfName = this.CityOrHotel(cityOrHotelName);
            if (typeOfName == 0)
                this.City = cityOrHotelName;
            else if (typeOfName == 1)
                this.City = this.dbHotels.Where(a => a.Name == cityOrHotelName).First().City;
            else
            {
                this.LoadCity("dubai");
                return;
            }

            using (StreamReader sr = File.OpenText(Misc.GetFilePath($"content/hotelBeds/hotels_{this.City}.json")))
            using (JsonTextReader jReader = new JsonTextReader(sr))
                this.jContentHotels = JToken.ReadFrom(jReader).Value<JArray>("hotels");
        }

        public List<string> GetDistricts()
        {
            Misc.Log("GetDistricts");
            return this.jTypesDestinations.Where(a => a.Value<JToken>("name").Value<string>("content").ToLower() == this.City).First().Value<JArray>("zones").Values<string>("name").ToList();

            //if (this.City == "dubai")
            //    return this.dubaiDistricts;
            //else if (this.City == "istanbul")
            //    return this.istanbulDistricts;
            //else
            //{
            //    List<string> re = new List<string>();
            //    string[] lines = File.ReadAllLines(Misc.GetFilePath("content/dest.txt"));
            //    foreach (string line in lines)
            //        if (line.Split('|')[0].ToLower() == city)
            //            re.Add(line.Split('|')[1]);
            //    return re.Take(15).ToArray();
            //}
        }

        public JToken GetContentHotel(int hotelCode)
        {
            return this.jContentHotels.Where(a => a.Value<int>("code") == hotelCode).FirstOrDefault();
        }

        public JToken GetContentHotel(string hotelName)
        {
            hotelName = hotelName.ToLower();
            return this.jContentHotels.Where(a => a.Value<JToken>("name").Value<string>("content").ToLower() == hotelName).FirstOrDefault();
        }

        public JToken GetContentRoom(JToken jHotel, string roomCode)
        {
            return jHotel.Value<JArray>("rooms").Where(a => a.Value<string>("roomCode") == roomCode).FirstOrDefault();
        }

        public string GetCity(string destinationCode)
        {
            return this.jTypesDestinations.Where(a => a.Value<string>("code") == destinationCode).FirstOrDefault().Value<JToken>("name").Value<string>("content");
        }

        public string GetDistrict(string destinationCode, int zoneCode)
        {
            return this.jTypesDestinations.Where(a => a.Value<string>("code") == destinationCode)
                .First().Value<JArray>("zones").Where(b => b.Value<int>("zoneCode") == zoneCode)
                .First().Value<string>("name");
        }

        public string GetRoomName(string roomCode)
        {
            return this.jTypesRooms.Where(a => a.Value<string>("code") == roomCode).First().Value<string>("description");
        }

        public string GetBoard(string boardCode)
        {
            return this.jTypesBoards.Where(a => a.Value<string>("code") == boardCode).FirstOrDefault().Value<JToken>("description").Value<string>("content");
        }

        public int GetStars(string categoryCode)
        {
            return this.jTypesCategories.Where(a => a.Value<string>("code") == categoryCode).FirstOrDefault().Value<int>("simpleCode");
        }

        public JToken GetTypeFacility(int facilityCode, int facilityGroupCode)
        {
            return this.jTypesFacilities
                .Where(a => a.Value<int>("code") == facilityCode && a.Value<int>("facilityGroupCode") == facilityGroupCode)
                .FirstOrDefault();
        }

        internal int MatchHotel(int type, int id)
        {
            int value = 0;

            if (type == 0)
                this.RestelLinks.TryGetValue(id, out value);
            else if (type == 1)
                this.GoGlobalLinks.TryGetValue(id, out value);

            return value;
        }
    }

    public class Database
    {
        private static long lastId = 10010;
        internal ContentProvider Content;

        internal List<DBCompany> dbCompanyCol;
        internal List<DBUser> dbUserCol;
        internal List<DBBooking> dbBookingCol;
        internal List<DBBookingRate> dbBookingRateCol;

        public List<User> Agents
        {
            get { return DBUser.Selects((int)UserType.Agent).Select(a => a.AsUser()).ToList(); }
        }

        public Database()
        {
            this.dbCompanyCol = new List<DBCompany>();
            this.dbUserCol = new List<DBUser>();
            this.dbBookingCol = new List<DBBooking>();
            this.dbBookingRateCol = new List<DBBookingRate>();
        }

        internal User SignIn(string email, string password)
        {
            return DBUser.Select(email, password)?.AsUser() ?? null;
        }

        public bool SignUp(dynamic jObject)
        {
            Company company = new Company(new DynamicBag(jObject.companyInfo));
            company.Agent = new User(UserType.Agent, company, new DynamicBag(jObject.agentInfo));
            company.SubUsers = new List<User>();
            company.SubUsers.Add(new User(UserType.AgentSubUser, company, new DynamicBag(jObject.contactInfo)) { Position = "Contact Person" });
            company.SubUsers.Add(new User(UserType.AgentSubUser, company, new DynamicBag(jObject.accountantInfo)) { Position = "Accountant" });

            company.DBSave(true, true);
            return true;
        }

        //internal Company LoadCompanyWithUsers(long companyId)
        //{
        //    Company company = DBCompany.Select(companyId).AsCompany();
        //    company.Agent = DBUser.Selects(companyId, (int)UserType.Agent).First().AsUser();
        //    company.SubUsers = DBUser.Selects(companyId, (int)UserType.AgentSubUser).Select(a => a.AsUser()).ToList();
        //    return company;
        //}

        internal string GeneratePassword()
        {
            return "0000";
        }

        internal long GenerateId()
        {
            return Database.lastId++;
        }

        internal void Initialize()
        {
            this.GenerateInitialData();
            this.Content = new ContentProvider();
        }
    }

    internal class DBCompany
    {
        internal long? Id;

        internal bool IsActive;
        internal bool IsApproved;

        internal string Name;
        internal string ShortName;
        internal string CommercialRegistrationNo;
        internal string Country;
        internal string City;
        internal string POBox;
        internal string ZipCode;
        internal string Address;
        internal string LandLineNo;
        internal string FaxNo;
        internal string WebSite;

        internal string Logo;
        internal string BigLogo;
        internal string MiniLogo;

        internal int CreditLimit;
        internal float Profit;
        //internal string Remarks;

        //TODO
        internal float ReservedBalance;
        internal float ActualBalance;

        internal DateTime CreationDate;

        internal DBCompany()
        {
            this.Id = Prog.Database.GenerateId();
        }

        internal DBCompany(Company company) : this()
        {
            this.Serialize(company);
        }

        internal Company AsCompany()
        {
            Company company = new Company();

            company.Id = this.Id;
            company.IsActive = this.IsActive;
            company.IsApproved = this.IsApproved;
            company.Name = this.Name;
            company.ShortName = this.ShortName;
            company.CommercialRegistrationNo = this.CommercialRegistrationNo;
            company.Country = this.Country;
            company.City = this.City;
            company.POBox = this.POBox;
            company.ZipCode = this.ZipCode;
            company.Address = this.Address;
            company.LandLineNo = this.LandLineNo;
            company.FaxNo = this.FaxNo;
            company.WebSite = this.WebSite;
            company.Logo = this.Logo;
            company.BigLogo = this.BigLogo;
            company.MiniLogo = this.MiniLogo;

            company.CreditLimit = this.CreditLimit;
            company.Profit = this.Profit;

            company.reservedBalance = this.ReservedBalance;
            company.actualBalance = this.ActualBalance;

            company.CreationDate = this.CreationDate;
            return company;
        }

        private void Serialize(Company company)
        {
            if (company == null) return;

            this.IsActive = company.IsActive;
            this.IsApproved = company.IsApproved;
            this.Name = company.Name;
            this.ShortName = company.ShortName;
            this.CommercialRegistrationNo = company.CommercialRegistrationNo;
            this.Country = company.Country;
            this.City = company.City;
            this.POBox = company.POBox;
            this.ZipCode = company.ZipCode;
            this.Address = company.Address;
            this.LandLineNo = company.LandLineNo;
            this.FaxNo = company.FaxNo;
            this.WebSite = company.WebSite;
            this.Logo = company.Logo;
            this.BigLogo = company.BigLogo;
            this.MiniLogo = company.MiniLogo;

            this.CreditLimit = company.CreditLimit;
            this.Profit = company.Profit;

            this.ReservedBalance = company.ReservedBalance;
            this.ActualBalance = company.ActualBalance;

            this.CreationDate = company.CreationDate;
        }

        internal static bool Save(Company company)
        {
            if (company.Id.HasValue)
                DBCompany.Select(company.Id.Value).Serialize(company);
            else
            {
                var dbCom = new DBCompany(company);
                Prog.Database.dbCompanyCol.Add(dbCom);

                company.Id = dbCom.Id;
            }
            return true;
        }

        internal static DBCompany Select(long companyId)
        {
            return Prog.Database.dbCompanyCol.Where(a => a.Id == companyId).FirstOrDefault();
        }

        internal static List<DBCompany> Selects()
        {
            return Prog.Database.dbCompanyCol;
        }
    }

    internal class DBUser
    {
        internal long? companyId;

        internal long? Id;

        internal bool IsActive;

        internal string FirstName;
        internal string LastName;
        internal string Email;
        internal string Password;
        internal string Position;
        internal string PhoneNo;
        internal string MobileNo;
        internal string FaxNo;

        internal string Permissions;

        internal int Type;

        internal DateTime CreationDate;
        internal DateTime LastLoginTime;

        internal DBUser()
        {
            this.Id = Prog.Database.GenerateId();
        }

        internal DBUser(User user) : this()
        {
            this.Serialize(user);
        }

        internal static User GetAgent(long companyId)
        {
            return DBUser.Selects(companyId, (int)UserType.Agent).FirstOrDefault().AsUser();
        }

        internal static List<User> GetSubUsers(long companyId)
        {
            return DBUser.Selects(companyId, (int)UserType.AgentSubUser).Select(a => a.AsUser()).ToList();
        }

        internal User AsUser()
        {
            User user = new User();

            user.Id = this.Id;
            user.IsActive = this.IsActive;
            user.FirstName = this.FirstName;
            user.LastName = this.LastName;
            user.Email = this.Email;
            user.Password = this.Password;
            user.Position = this.Position;
            user.PhoneNo = this.PhoneNo;
            user.MobileNo = this.MobileNo;
            user.FaxNo = this.FaxNo;
            user.Permissions = this.Permissions;
            user.Type = (UserType)this.Type;
            user.CreationDate = this.CreationDate;
            user.LastLoginTime = this.LastLoginTime;

            user.companyId = this.companyId;
            return user;
        }

        private void Serialize(User user)
        {
            if (user == null) return;

            this.IsActive = user.IsActive;
            this.FirstName = user.FirstName;
            this.LastName = user.LastName;
            this.Email = user.Email;
            this.Password = user.Password;
            this.Position = user.Position;
            this.PhoneNo = user.PhoneNo;
            this.MobileNo = user.MobileNo;
            this.FaxNo = user.FaxNo;
            this.Permissions = user.Permissions;
            this.Type = (int)user.Type;
            this.CreationDate = user.CreationDate;
            this.LastLoginTime = user.LastLoginTime;

            this.companyId = user.Company.Id;
        }

        internal static bool Save(User user)
        {
            if (user.Id.HasValue)
                DBUser.Select(user.Id.Value).Serialize(user);
            else
            {
                var dbUser = new DBUser(user);
                Prog.Database.dbUserCol.Add(dbUser);

                user.Id = dbUser.Id;
            }
            return true;
        }

        internal static bool Delete(long userId)
        {
            return Prog.Database.dbUserCol.Remove(DBUser.Select(userId));
        }

        internal static DBUser Select(long userId)
        {
            return Prog.Database.dbUserCol.Where(a => a.Id == userId).FirstOrDefault();
        }

        internal static DBUser Select(string email, string password)
        {
            return Prog.Database.dbUserCol.Where(a => a.Email == email && a.Password == password).FirstOrDefault();
        }

        internal static List<DBUser> Selects(int type)
        {
            return DBUser.Selects(null, type);
        }

        internal static List<DBUser> Selects(long? companyId, int type)
        {
            if (companyId.HasValue)
                return Prog.Database.dbUserCol.Where(a => a.companyId == companyId && a.Type == type).ToList();
            return Prog.Database.dbUserCol.Where(a => a.Type == type).ToList();
        }
    }

    public class DBBooking
    {
        internal long? companyId;
        internal long? userId;

        internal long? Id;

        internal DateTime CheckIn;
        internal DateTime CheckOut;
        internal int RoomsCount;
        internal int AdultsCount;
        internal int ChildrenCount;
        internal string ChildrenAge;

        internal long HotelCode;
        internal string HotelName;
        internal string City;
        internal float TotalPrice;

        internal int BookingStatus;

        internal string BRefNo;

        internal string MFirstName;
        internal string MLastName;
        internal string MEmail;

        internal DateTime? CancellationDate;
        internal DateTime CreationDate;

        internal DBBooking()
        {
            this.Id = Prog.Database.GenerateId();
        }

        internal DBBooking(Booking booking) : this()
        {
            this.Serialize(booking);
        }

        internal DBBooking(BookingInfo bookingInfo) : this()
        {
            this.Serialize(bookingInfo);
        }

        internal Booking AsBooking()
        {
            Booking booking = new Booking();
            booking.CheckIn = this.CheckIn;
            booking.CheckOut = this.CheckOut;
            booking.RoomsCount = this.RoomsCount;
            booking.AdultsCount = this.AdultsCount;
            booking.ChildrenCount = this.ChildrenCount;
            if (!(this.ChildrenAge == null)) booking.ChildrenAge = this.ChildrenAge.Split(',').Select(a => a.IntOrDefault(1)).ToList();

            booking.Status = (BookingStatus)this.BookingStatus;
            booking.BRefNo = this.BRefNo;

            booking.CreationTime = this.CreationDate;
            return booking;
        }

        internal BookingInfo AsBookingInfo()
        {
            BookingInfo bInfo = new BookingInfo();

            bInfo.Id = this.Id;

            bInfo.CheckIn = this.CheckIn;
            bInfo.CheckOut = this.CheckOut;
            bInfo.RoomsCount = this.RoomsCount;
            bInfo.AdultsCount = this.AdultsCount;
            bInfo.ChildrenCount = this.ChildrenCount;

            bInfo.HotelName = this.HotelName;
            bInfo.City = this.City;
            bInfo.BRefNo = this.BRefNo;

            bInfo.TotalPrice = this.TotalPrice;

            bInfo.BookingStatus = (BookingStatus)this.BookingStatus;
            bInfo.CancellationDate = this.CancellationDate;

            bInfo.MFirstName = this.MFirstName;
            bInfo.MLastName = this.MLastName;
            bInfo.MEmail = this.MEmail;

            bInfo.CreationDate = this.CreationDate;
            bInfo.userId = this.userId;

            return bInfo;
        }

        private void Serialize(Booking booking)
        {
            if (booking == null) return;

            this.CheckIn = booking.CheckIn;
            this.CheckOut = booking.CheckOut;
            this.RoomsCount = booking.RoomsCount;
            this.AdultsCount = booking.AdultsCount;
            this.ChildrenCount = booking.ChildrenCount;
            this.ChildrenAge = booking.ChildrenAge?.Aggregate(new StringBuilder(), (sb, i) => sb.Append($"{i},")).ToString().TrimEnd(',') ?? null;

            this.HotelCode = booking.SelectedHotel.Code;
            this.HotelName = booking.SelectedHotel.Name;
            this.City = booking.SelectedHotel.City;
            this.TotalPrice = booking.TotalPrice;

            this.BookingStatus = (int)booking.Status;
            this.BRefNo = booking.BRefNo;

            if (booking.Cancellable)
                this.CancellationDate = booking.FirstActiveCancellation.From;

            this.companyId = booking.User.Company.Id;
            this.userId = booking.User.Id;

            this.MFirstName = booking.MainCustomerInfo.FirstName;
            this.MLastName = booking.MainCustomerInfo.LastName;
            this.MEmail = booking.MainCustomerInfo.Email;

            this.CreationDate = DateTime.Now;
        }

        private void Serialize(BookingInfo bookingInfo)
        {
            if (bookingInfo == null) return;

            this.CheckIn = bookingInfo.CheckIn;
            this.CheckOut = bookingInfo.CheckOut;
            this.RoomsCount = bookingInfo.RoomsCount;
            this.AdultsCount = bookingInfo.AdultsCount;
            this.ChildrenCount = bookingInfo.ChildrenCount;
            //TODO
            //this.ChildrenAge = bookingInfo.ChildrenAge?.Aggregate(new StringBuilder(), (sb, i) => sb.Append($"{i},")).ToString().TrimEnd(',') ?? null;

            //this.HotelCode = bookingInfo.Ho
            this.HotelName = bookingInfo.HotelName;
            this.City = bookingInfo.City;
            this.TotalPrice = bookingInfo.TotalPrice;

            this.BookingStatus = (int)bookingInfo.BookingStatus;
            this.BRefNo = bookingInfo.BRefNo;

            this.CancellationDate = bookingInfo.CancellationDate;

            this.companyId = bookingInfo.User.Company.Id;
            this.userId = bookingInfo.User.Id;

            this.MFirstName = bookingInfo.MFirstName;
            this.MLastName = bookingInfo.MLastName;
            this.MEmail = bookingInfo.MEmail;

            this.CreationDate = DateTime.Now;
        }

        internal static float GetSum(long? companyId, BookingStatus bookingStatus)
        {
            int value = (int)bookingStatus;
            return DBBooking.Selects(companyId).Where(a => (a.BookingStatus & value) == a.BookingStatus).Sum(a => a.TotalPrice);
        }

        internal static bool Save(Booking booking)
        {
            if (booking.Id.HasValue)
                DBBooking.Select(booking.Id.Value).Serialize(booking);
            else
            {
                var dbBooking = new DBBooking(booking);
                Prog.Database.dbBookingCol.Add(dbBooking);

                booking.Id = dbBooking.Id;
            }
            return true;
        }

        internal static bool Save(BookingInfo bookingInfo)
        {
            if (bookingInfo.Id.HasValue)
                DBBooking.Select(bookingInfo.Id.Value).Serialize(bookingInfo);
            else
            {
                var dbBooking = new DBBooking(bookingInfo);
                Prog.Database.dbBookingCol.Add(dbBooking);

                bookingInfo.Id = dbBooking.Id;
            }
            return true;
        }

        internal static bool Delete(long bookingId)
        {
            return Prog.Database.dbBookingCol.Remove(DBBooking.Select(bookingId));
        }

        internal static DBBooking Select(long bookingId)
        {
            return Prog.Database.dbBookingCol.Where(a => a.Id == bookingId).FirstOrDefault();
        }

        internal static List<DBBooking> Selects(BookingStatus bookingStatus, DateTime timePassed)
        {
            int value = (int)bookingStatus;
            return Prog.Database.dbBookingCol.Where(a =>
            ((a.BookingStatus & value) == a.BookingStatus) &&
            a.CancellationDate.HasValue &&
            a.CancellationDate.Value < timePassed).ToList();
        }

        internal static List<DBBooking> Selects(long? companyId)
        {
            return DBBooking.Selects(companyId, null, null, null, null, null, null, null, null, (int)MSite.BookingStatus.All);
        }

        internal static List<DBBooking> Selects(long? companyId, DateTime? from, DateTime? to,
            string country, string city, string bRefNo, string hotelName, string firstName, string lastName, int bookingStatus)
        {
            Misc.Log("DBBookingSelect");

            if (!companyId.HasValue)
                return new List<DBBooking>();

            IEnumerable<DBBooking> bCol = Prog.Database.dbBookingCol.Where(a => a.companyId == companyId);
            if (from.HasValue)
                bCol = bCol.Where(a => a.CheckIn >= from);
            if (to.HasValue)
                bCol = bCol.Where(a => a.CheckOut <= to);
            if (!string.IsNullOrEmpty(city))
                bCol = bCol.Where(a => a.City.ToLower() == city.ToLower());

            //TODO
            //if (!string.IsNullOrEmpty(country))
            //    bCol = bCol.Where(a => a.Country == country);
            if (!string.IsNullOrEmpty(firstName))
                bCol = bCol.Where(a => a.MFirstName.ToLower().Contains(firstName.ToLower()));
            if (!string.IsNullOrEmpty(lastName))
                bCol = bCol.Where(a => a.MLastName.ToLower().Contains(lastName.ToLower()));
            if (!string.IsNullOrEmpty(bRefNo))
                bCol = bCol.Where(a => a.BRefNo == bRefNo);
            if (!string.IsNullOrEmpty(hotelName))
                bCol = bCol.Where(a => a.HotelName.ToLower().Contains(hotelName.ToLower()));
            bCol = bCol.Where(a => (a.BookingStatus & bookingStatus) == a.BookingStatus);
            return bCol.ToList();
        }
    }

    internal class DBBookingRate
    {
        internal long? bookingId;

        internal long? Id;

        internal string RoomCode;
        internal string RateCode;

        internal string RoomName;
        internal string BoardName;

        internal float Net;
        internal int Count;

        internal DateTime? CancellationDate;

        internal string FirstName;
        internal string LastName;
        internal string Nationality;
        internal string Mobile;
        internal string Email;

        internal DBBookingRate()
        {
            this.Id = Prog.Database.GenerateId();
        }

        internal DBBookingRate(BookingRate bookingRate) : this()
        {
            this.Serialize(bookingRate);
        }

        internal DBBookingRate(BookingRateInfo bookingRateInfo) : this()
        {
            this.Serialize(bookingRateInfo);
        }

        internal BookingRate AsBookingRate()
        {
            BookingRate bookingRate = new BookingRate();
            bookingRate.Id = this.Id;
            bookingRate.Count = this.Count;

            bookingRate.bookingId = this.bookingId;
            return bookingRate;
        }

        internal BookingRateInfo AsBookingRateInfo(BookingInfo bookingInfo)
        {
            BookingRateInfo bRInfo = new BookingRateInfo();

            bRInfo.RateCode = this.RateCode;

            bRInfo.RoomName = this.RoomName;
            bRInfo.BoardName = this.BoardName;

            bRInfo.Net = this.Net;
            bRInfo.Count = this.Count;

            bRInfo.FirstName = this.FirstName;
            bRInfo.LastName = this.LastName;
            bRInfo.Nationality = this.Nationality;
            bRInfo.Mobile = this.Mobile;
            bRInfo.Email = this.Email;

            bRInfo.BookingInfo = bookingInfo;

            return bRInfo;
        }

        internal void Serialize(BookingRate bookingRate)
        {
            if (bookingRate == null) return;

            this.RoomCode = bookingRate.Rate.Room.Code;
            this.RateCode = bookingRate.Rate.Key;
            this.RoomName = bookingRate.Rate.Room.Name;
            this.BoardName = bookingRate.Rate.BoardName;
            this.Net = bookingRate.TotalPrice;
            this.Count = bookingRate.Count;

            if (bookingRate.Rate.Cancellable)
                this.CancellationDate = bookingRate.Rate.FirstCancellation.From;

            this.FirstName = bookingRate.CustomerInfo.FirstName;
            this.LastName = bookingRate.CustomerInfo.LastName;
            this.Nationality = bookingRate.CustomerInfo.Nationality;
            this.Mobile = bookingRate.CustomerInfo.Phone;
            this.Email = bookingRate.CustomerInfo.Email;

            this.bookingId = bookingRate.Booking.Id;
        }

        internal void Serialize(BookingRateInfo bookingRateInfo)
        {
            if (bookingRateInfo == null) return;

            //this.RoomCode = bookingRateInfo.Room
            this.RateCode = bookingRateInfo.RateCode;
            this.RoomName = bookingRateInfo.RoomName;
            this.BoardName = bookingRateInfo.BoardName;
            this.Net = bookingRateInfo.Net;
            this.Count = bookingRateInfo.Count;

            //this.CancellationDate = bookingRateInfo.Ca

            this.FirstName = bookingRateInfo.FirstName;
            this.LastName = bookingRateInfo.LastName;
            this.Nationality = bookingRateInfo.Nationality;
            this.Mobile = bookingRateInfo.Mobile;
            this.Email = bookingRateInfo.Email;

            this.bookingId = bookingRateInfo.BookingInfo.Id;
        }

        internal static bool Save(BookingRate bookingRate)
        {
            if (bookingRate.Id.HasValue)
                DBBookingRate.Select(bookingRate.Id.Value).Serialize(bookingRate);
            else
            {
                var dbBookingRate = new DBBookingRate(bookingRate);
                Prog.Database.dbBookingRateCol.Add(dbBookingRate);

                bookingRate.Id = dbBookingRate.Id;
            }
            return true;
        }


        internal static bool Save(BookingRateInfo bookingRateInfo)
        {
            if (bookingRateInfo.Id.HasValue)
                DBBookingRate.Select(bookingRateInfo.Id.Value).Serialize(bookingRateInfo);
            else
            {
                var dbBookingRate = new DBBookingRate(bookingRateInfo);
                Prog.Database.dbBookingRateCol.Add(dbBookingRate);

                bookingRateInfo.Id = dbBookingRate.Id;
            }
            return true;
        }

        internal static DBBookingRate Select(long bookingRateId)
        {
            return Prog.Database.dbBookingRateCol.Where(a => a.Id == bookingRateId).FirstOrDefault();
        }

        internal static List<DBBookingRate> Selects(long bookingId)
        {
            return Prog.Database.dbBookingRateCol.Where(a => a.bookingId == bookingId).ToList();
        }
    }


    public class DBHotel
    {
        public int Code { get; set; }
        public int GoGlobalId { get; set; }
        public int RestelId { get; set; }
        public string Name { get; set; }
        public string City { get; set; }

        public DBHotel() { }
    }

    [Serializable]
    public class MutualHotel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string CityCode { get; set; }
        public string City { get; set; }
        public string POBox { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public float Longitude { get; set; }
        public float Latitude { get; set; }

        public float Longitude2 { get; set; }
        public float Latitude2 { get; set; }
        public float Longitude3 { get; set; }
        public float Latitude3 { get; set; }

        public MutualHotel Match { get; set; }
        public List<MutualHotel> Matches { get; set; }

        public MutualHotel GoGlobalHotel { get; set; }
        public MutualHotel RestelHotel { get; set; }

        public List<MutualHotel> Duplicates { get; set; }

        public string GeoLocationTextRound1 { get { return $"{Math.Round(this.Longitude, 1)}-{Math.Round(this.Latitude, 1)}"; } }
        public string GeoLocationTextRound2 { get { return $"{Math.Round(this.Longitude, 2)}-{Math.Round(this.Latitude, 2)}"; } }
        public string GeoLocationTextRound3 { get { return $"{Math.Round(this.Longitude, 3)}-{Math.Round(this.Latitude, 3)}"; } }
        public string GeoLocationText { get { return $"({this.Longitude},{this.Latitude})"; } }

        private GeoInfo geoLocation;
        public GeoInfo GeoLocation
        {
            get
            {
                if (this.geoLocation == GeoInfo.Default)
                    this.geoLocation = new GeoInfo(this.Longitude, this.Latitude);
                return this.geoLocation;
            }
            set { this.geoLocation = value; }
        }

        public int Tag { get; set; }

        public string Phone2
        {
            get
            {
                if (string.IsNullOrEmpty(this.Phone))
                    return "phoneUnExist";

                string phone = Regex.Replace(this.Phone, "[()\\-_\\s+\\.#]", "");
                if (phone.Length < 9)
                    return "lessThan9";

                Match m = Regex.Match(phone, @"\d{9}$");
                if (m.Success)
                    return m.Value;
                else
                    return "couldNotMatch9";
            }
        }

        public string Email2
        {
            get
            {
                if (string.IsNullOrEmpty(this.Email))
                    return "noEmail";
                else
                    return this.Email;
            }
        }

        public MutualHotel()
        {
            this.Duplicates = new List<MutualHotel>();
        }

        public override string ToString()
        {
            return string.Format("{0,-50}({1},{2})", this.Name, this.Latitude, this.Longitude);
        }
    }

    public class XmlDataSource
    {
        public string Name { get; set; }
        public float Balance { get; set; }
        public bool IsActive { get; set; }

        public static List<XmlDataSource> Xmls
        {
            get
            {
                List<XmlDataSource> xCol = new List<XmlDataSource>();
                xCol.Add(new XmlDataSource() { Name = "Go Global", Balance = 10000, IsActive = true });
                xCol.Add(new XmlDataSource() { Name = "Hotel Beds", Balance = 10000, IsActive = false });
                xCol.Add(new XmlDataSource() { Name = "Hotels Pro", Balance = 10000, IsActive = false });
                xCol.Add(new XmlDataSource() { Name = "Conso", Balance = 10000, IsActive = true });
                xCol.Add(new XmlDataSource() { Name = "Restel", Balance = 10000, IsActive = false });
                xCol.Add(new XmlDataSource() { Name = "Darina", Balance = 10000, IsActive = false });
                xCol.Add(new XmlDataSource() { Name = "Sntta", Balance = 10000, IsActive = false });
                return xCol;
            }
        }

        public XmlDataSource() { }

        internal static Hotel FetchHotel(Booking booking)
        {
            return new XmlHotelBedsProvider(booking).GetHotel();
        }

        internal static List<Hotel> FetchHotels(Booking booking)
        {
            List<Hotel> hCol = new List<Hotel>();
            //try
            //{
            // XmlGoGlobalProvider goGlobalPro = new XmlGoGlobalProvider(booking);
            // hCol.AddRange(goGlobalPro.GetHotels());

            var hbTask = new XmlHotelBedsProvider(booking).GetHotels();
            //var reTask = new XmlRestelProvider(booking).GetHotels();

            //string hbNames = Misc.AggregateAsString(hbTask.Select(a => a.Name).OrderBy(a => a));
            //string reNames = Misc.AggregateAsString(reTask.Select(a => a.Name).OrderBy(a => a));


            //foreach (var item in reTask)
            //{
            //    int id = Prog.Content.MatchHotel(0, item.Code);
            //    if (id == 0)
            //        continue;

            //    Hotel match = hbTask.Where(a => a.Code == id).FirstOrDefault();
            //    if (match == null)
            //        continue;

            //    match.Rooms.AddRange(item.Rooms);
            //    hCol.Add(match);
            //}


            hCol.AddRange(hbTask);
            //hCol.AddRange(reTask);

            //hCol.AddRange(new XmlRestelProvider(booking).GetHotels() ?? new List<Hotel>());
            //}
            //catch (Exception ex)
            //{
            //    Logger.Log(ex.GetBaseException().ToString());
            //}
            return hCol;
        }

        internal static string GetIATACode(string city)
        {
            if (string.IsNullOrEmpty(city))
                return "DXB";

            string place = city.Split(',')[0].ToLower();
            if (place == "istanbul")
                return "IST";
            else if (place == "dubai")
                return "DXB";
            else if (place == "amman")
                return "AMM";
            else if (place == "aqaba")
                return "AQJ";

            return "DXB";
        }
    }

    public class XmlHotelBedsProvider : IProvider
    {
        private Booking booking;

        public XmlHotelBedsProvider(Booking booking)
        {
            this.booking = booking;
        }

        internal static string GetSignature()
        {
            string apiKey = "";
            string Secret = "";

            // Compute the signature to be used in the API call (combined key + secret + timestamp in seconds)
            string signature;
            using (var sha = SHA256.Create())
            {
                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds / 1000;
                var computedHash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiKey + Secret + ts));
                signature = BitConverter.ToString(computedHash).Replace("-", "");
            }

            return signature;
        }

        public static List<Hotel> TestUn(List<int> idCol, DateTime checkIn, DateTime checkOut, int roomsCount, int adultsCount, int childrenCount, List<int> childrenAge, int repeat)
        {
            string response = null;

            try
            {
                string url = "https://api.test.hotelbeds.com/hotel-api/1.0/hotels";

                WebHeaderCollection whCol = new WebHeaderCollection();
                whCol.Add(HttpRequestHeader.Accept, "application/json");
                //whCol.Add(HttpRequestHeader.AcceptEncoding, "utf-8");
                whCol.Add(HttpRequestHeader.AcceptEncoding, "gzip");
                whCol.Add(HttpRequestHeader.ContentType, "application/json");
                whCol.Add("Api-Key", "");
                whCol.Add("X-Signature", XmlHotelBedsProvider.GetSignature());

                if (adultsCount <= 1)
                    adultsCount = 2;

                string paxes = "";
                for (int i = 0; i < adultsCount; i++)
                    paxes += "{'type': 'AD'},";
                for (int i = 0; i < childrenCount; i++)
                    paxes += $"{{'type': 'CH', 'age': {childrenAge[i]}}},";

                paxes = paxes.Trim(',');

                string body = "{'stay':{'checkIn':'" + checkIn.ToString("yyyy-MM-dd") +
                    "','checkOut':'" + checkOut.ToString("yyyy-MM-dd") +
                    "'},'occupancies':[{'rooms': " + roomsCount +
                    // ", 'reviews': [{ 'type': 'HOTELBEDS', 'minRate': 1}]" +
                    ",'adults': " + adultsCount.ToString() +
                    ",'children': " + childrenCount.ToString() +
                    ",'paxes':[" + paxes + "]}],'hotels':{'hotel':[" + idCol.SplitByComma() +
                    "]}}";
                body = body.Replace('\'', '"');

                Misc.Log("JRequest-string, {0}", body.Replace("{", "(").Replace("}", ")"));
                // Misc.Log("JRequest", JObject.Parse(body));

                WebClient wCli = new WebClient();
                wCli.Headers.Add(whCol);
                byte[] buffer = wCli.UploadData(url, Encoding.UTF8.GetBytes(body));
                //Misc.SaveToFile(buffer);
                response = Misc.DecompressGZip(buffer);
                //System.Threading.Thread.Sleep(2000);
                //Misc.SaveToFile(response);

                Misc.Log(wCli.ResponseHeaders.AllKeys.Aggregate(
                    new StringBuilder().AppendLine(), (sb, item) => sb.AppendLine($"{item} - {wCli.ResponseHeaders.Get(item)}")).ToString());

                //for (int i = 0; i < wCli.ResponseHeaders.Count; i++)
                //    Misc.Log($"{wCli.ResponseHeaders.GetKey(i)} - {wCli.ResponseHeaders.Get(i)}");
                // Misc.SaveToFile(response);
                // Misc.Log("response, {0}", response.Substring(0, 4000));

                JObject jResponse = JObject.Parse(response);
                JArray jrHotels = jResponse.Value<JObject>("hotels").Value<JArray>("hotels");
                // Misc.SaveToFile(jResponse);

                if (jrHotels == null)
                {
                    Misc.Log("jResponse, {0}", jResponse);
                    if (repeat < 4)
                    {
                        Misc.Log("Repeat {0}", repeat);
                        return XmlHotelBedsProvider.TestUn(idCol, checkIn, checkOut, roomsCount, adultsCount, childrenCount, childrenAge, ++repeat);
                    }
                    else
                    {
                        Misc.Log("Repeat finish");
                        return new List<Hotel>();
                    }
                }

                Misc.Log("hotelBeds - found: {0}", jrHotels.Count);

                Hotel mHotel = null;
                Room mRoom = null;
                List<Hotel> hCol = new List<Hotel>();

                for (int i = 0; i < jrHotels.Count; i++)
                {
                    try
                    {
                        JToken jrHotel = jrHotels.Value<JToken>(i);
                        mHotel = new Hotel(Prog.Content.GetContentHotel(jrHotel.Value<int>("code")));
                        hCol.Add(mHotel);

                        JArray jrRooms = jrHotel.Value<JArray>("rooms");
                        for (int j = 0; j < jrRooms.Count; j++)
                        {
                            JToken jrRoom = jrRooms.Value<JToken>(j);
                            mRoom = new Room(mHotel, jrRoom.Value<string>("code"));
                            mHotel.Rooms.Add(mRoom);

                            JArray jRates = jrRoom.Value<JArray>("rates");
                            for (int k = 0; k < jRates.Count; k++)
                            {
                                JToken jRate = jRates.Value<JToken>(k);

                                Rate rate = new Rate(mRoom);
                                mRoom.Rates.Add(rate);

                                rate.Key = jRate.Value<string>("rateKey");
                                rate.Net = jRate.Value<string>("net").FloatOrDefault();
                                rate.Allotment = jRate.Value<int>("allotment");
                                rate.PaymentType = jRate.Value<string>("paymentType");
                                rate.BoardCode = jRate.Value<string>("boardCode");

                                JArray jCancellations = jRate.Value<JArray>("cancellationPolicies");
                                if (jCancellations.Count > 0)
                                {
                                    rate.Cancellation = new List<Cancellation>();
                                    for (int l = 0; l < jCancellations.Count; l++)
                                    {
                                        JToken jCancellation = jCancellations.Value<JToken>(l);
                                        rate.Cancellation.Add(new Cancellation(
                                            jCancellation.Value<DateTime>("from").ToUniversalTime(), //.DateTimeOrDefault(DateTime.Now).Value,
                                            jCancellation.Value<string>("amount").FloatOrDefault()));
                                    }
                                }

                                JArray jOffers = jRate.Value<JArray>("offers");
                                if (!(jOffers == null) && jOffers.Count > 0)
                                {
                                    rate.Offers = new List<Offer>();
                                    for (int oIndex = 0; oIndex < jOffers.Count; oIndex++)
                                    {
                                        JToken jOffer = jOffers.Value<JToken>(oIndex);
                                        rate.Offers.Add(new Offer(
                                            jOffer.Value<int>("code"),
                                            jOffer.Value<string>("name"),
                                            jOffer.Value<float>("amount")));
                                    }
                                }

                                JArray jPromotions = jRate.Value<JArray>("promotions");
                                if (!(jPromotions == null) && jPromotions.Count > 0)
                                {
                                    rate.Promotions = new List<Promotion>();
                                    for (int pIndex = 0; pIndex < jPromotions.Count; pIndex++)
                                    {
                                        JToken jPromotion = jPromotions.Value<JToken>(pIndex);
                                        rate.Promotions.Add(new Promotion(
                                            jPromotion.Value<int>("code"),
                                            jPromotion.Value<string>("name"),
                                            jPromotion.Value<string>("remark")));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Misc.Log(ex.GetBaseException().ToString()); }
                }
                hCol = hCol.Where(a => a.HasRates).ToList();
                Misc.Log("hotelBeds - HasRates: {0}", hCol.Count);
                return hCol;
            }
            catch (Exception ex)
            {
                if (ex is WebException)
                {
                    WebException wEx = (WebException)ex;
                    byte[] bytes = new byte[wEx.Response.ContentLength];
                    wEx.Response.GetResponseStream().Read(bytes, 0, bytes.Length);
                    Misc.Log("error-response, {0}", Misc.DecompressGZip(bytes));
                }
                Misc.Log("error: {0}", ex.GetBaseException().ToString());
                // Misc.Log("response, {0}", response.Substring(0, 4000));
                return new List<Hotel>();
            }
        }

        public async Task<List<Hotel>> GetHotelsAsync()
        {
            var task = Task.Factory.StartNew(new Func<List<Hotel>>(() =>
            {
                return this.GetHotels();
            }));
            return await task;
        }

        public List<Hotel> GetHotels()
        {
            return XmlHotelBedsProvider.TestUn(
               Prog.Content.GetHotelsIds(this.booking.City), this.booking.CheckIn, this.booking.CheckOut,
               this.booking.RoomsCount, this.booking.AdultsCount,
               this.booking.ChildrenCount, this.booking.ChildrenAge,
               0);
        }

        public Hotel GetHotel()
        {
            Hotel hotel = XmlHotelBedsProvider.TestUn(
                new List<int>() { this.booking.SelectedHotel.Code }, this.booking.CheckIn, this.booking.CheckOut,
                this.booking.RoomsCount, this.booking.AdultsCount,
                this.booking.ChildrenCount, this.booking.ChildrenAge,
                0).FirstOrDefault();

            if (hotel == null)
                hotel = this.booking.SelectedHotel.ShallowCopy(true);
            return hotel;
        }

        public void UpdateInfo(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRates(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRooms(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public static void DownloadHotels()
        {
            string path = "/hotelBedsHotels2.json";

            using (FileStream fStr = File.OpenWrite(path))
                fStr.WriteByte((byte)'[');

            string url = null;
            for (int i = 0; i <= 158817; i += 1000)
            {
                url = $"https://api.test.hotelbeds.com/hotel-content-api/1.0/hotels?fields=all&language=ENG&from={ i + 1 }&to={ i + 1000 }&useSecondaryLanguage=false";

                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
                request.Accept = "application/json";
                request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip");
                request.Headers.Add("Api-Key", "");
                request.Headers.Add("X-Signature", XmlHotelBedsProvider.GetSignature());

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Misc.Log("Response Code: {0} - Desc: {1} - i: {2}", response.StatusCode, response.StatusDescription, i);
                Misc.FlushLog();

                using (FileStream fStr = File.OpenWrite(path))
                using (Stream rStr = response.GetResponseStream())
                {
                    fStr.Seek(0, SeekOrigin.End);
                    Misc.DecompressGZip(rStr, fStr);
                    fStr.WriteByte((byte)',');
                }
            }

            using (FileStream fStr = File.OpenWrite(path))
            {
                fStr.Seek(1, SeekOrigin.End);
                fStr.WriteByte((byte)']');
            }
        }
    }

    public class XmlRestelProvider : IProvider
    {
        private Booking booking;
        private string xmlUrl = "http://xml.hotelresb2b.com/xml/listen_xml.jsp";

        public XmlRestelProvider(Booking booking)
        {
            this.booking = booking;
        }

        private NameValueCollection GetIdentification()
        {
            return new NameValueCollection() { { "codigousu", "PMVB" }, { "clausu", "xml483054" }, { "afiliacio", "RS" }, { "secacc", "958755" }, { "Codusu", "E10348" } };
        }

        private byte[] SendRequest(string data)
        {
            //Using GET request http://xml.hotelresb2b.com/xml/listen_xml.jsp?codigousu=OMNV&clausu=xml480424&afiliacio=RS&secacc=119755&Codusu=E12798&xml=

            //System.Net.ServicePointManager.DefaultConnectionLimit = 1000;

            NameValueCollection nvCol = new NameValueCollection() { this.GetIdentification(), { "xml", HttpContext.Current.Server.UrlEncode(data) } };

            //foreach (string i in nvCol.Keys)
            //    Misc.Log("restel identification, {0} - {1}", i, nvCol[i]);

            //WebClient webClient = new WebClient();
            //byte[] response = webClient.UploadValues(this.xmlUrl, nvCol);
            //string txtResponse = Encoding.UTF8.GetString(response);
            //Misc.Log("restel response, {0}", txtResponse);
            //return null;

            string parameters = string.Empty;
            foreach (string key in nvCol)
                parameters += $"{key}={nvCol[key]}&";
            parameters = parameters.TrimEnd('&');
            Misc.Log("parameters data: {0}", parameters);

            byte[] bytes = Encoding.ASCII.GetBytes(parameters);

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(this.xmlUrl);
            request.ContentType = "application/x-www-form-urlencoded";
            request.Method = "POST";
            request.ContentLength = bytes.Length;
            request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip");

            using (Stream reqStream = request.GetRequestStream())
                reqStream.Write(bytes, 0, bytes.Length);

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Misc.Log("Status Code: {0} - {1}", response.StatusCode, response.StatusDescription);

            var mStr = new MemoryStream();
            using (Stream resStream = response.GetResponseStream())
                resStream.CopyTo(mStr);

            response.Close();
            return mStr.ToArray();
        }

        internal string GetXmlCountries()
        {
            string xml = $"<peticion><nombre></nombre><agencia></agencia><tipo>5</tipo><idioma>2</idioma></peticion>";

            byte[] re = this.SendRequest(xml);
            string countries = Encoding.UTF8.GetString(re);
            //Misc.SaveToFile(countries);
            return countries;
        }

        internal void GetXmlProvinces()
        {
            string xml = $"<peticion><nombre></nombre><agencia></agencia><tipo>6</tipo><idioma>2</idioma></peticion>";

            byte[] re = this.SendRequest(xml);
            //Misc.SaveToFile(Encoding.UTF8.GetString(re));
        }

        internal void GetXmlCodes()
        {
            string xml = $"<peticion><tipo>27</tipo><nombre></nombre><agencia></agencia><parametros><provincia></provincia><marca></marca><afiliacion>RS</afiliacion><ultact></ultact><fechaalta></fechaalta><baja>0</baja></parametros></peticion>";

            byte[] re = this.SendRequest(xml);
            Misc.SaveToFile(Encoding.UTF8.GetString(re));
        }

        internal void GetXmlHotels()
        {
            string fileName = "hotelsBytes.xml";
            string xml = null;
            //string results = string.Empty;
            List<byte> bytes = new List<byte>();
            int counter = 0;
            int counterAll = 0;

            Misc.SaveToFile("<root>", fileName);
            List<string> ids = XElement.Load(File.OpenRead("/restelData/codes.xml")).Element("parametros").Element("hoteles").Elements("hotel").Select(a => (string)a.Element("codigo_cobol").Value).ToList();

            for (int i = 0; i < ids.Count; i++)
            {
                counterAll++;

                xml = $"<peticion><tipo>15</tipo><nombre></nombre><agencia></agencia><parametros></parametros><codigo>{ids[i]}</codigo><idioma>2</idioma></peticion>";

                byte[] re = this.SendRequest(xml);
                //results += Encoding.UTF8.GetString(re);
                bytes.AddRange(re.Skip(119));

                if (counter++ > 1000)
                {
                    //Misc.SaveToFile(results, "hotels.xml");
                    Misc.SaveToFile(bytes.ToArray(), fileName);

                    //results = string.Empty;
                    bytes.Clear();

                    counter = 0;

                    Misc.Log("reached {0}, Id: {1}", counterAll, ids[i]);
                    Misc.FlushLog();
                }
            }

            //Misc.SaveToFile(results, "hotels.xml");
            Misc.SaveToFile(bytes.ToArray(), fileName);
            Misc.SaveToFile("</root>", fileName);
        }

        public Hotel GetHotel()
        {
            throw new NotImplementedException();
        }

        public async Task<List<Hotel>> GetHotelsAsync()
        {
            var task = Task.Factory.StartNew(new Func<List<Hotel>>(() =>
            {
                return this.GetHotels();
            }));
            return await task;
        }

        public List<Hotel> GetHotels()
        {
            //string xml = "<peticion><tipo>110</tipo><nombre></nombre><agencia></agencia><parametros><pais>AE</pais><provincia>AEDXB</provincia><categoria>0</categoria><radio>9</radio><fechaentrada>06/25/2017</fechaentrada><fechasalida>06/27/2017</fechasalida><marca></marca><afiliacion>RS</afiliacion><usuario>E12798</usuario><numhab1>1</numhab1><paxes1>1-0</paxes1><numhab2>0</numhab2><paxes2>0</paxes2><numhab3>0</numhab3><paxes3>0</paxes3><idioma>2</idioma><duplicidad>1</duplicidad><comprimido>0</comprimido><informacion_hotel>0</informacion_hotel></parametros></peticion>";

            string xml = $"<peticion><tipo>110</tipo><nombre></nombre><agencia></agencia><parametros><pais>{"AE"}</pais><provincia>{"AEDXB"}</provincia><categoria>0</categoria><radio>9</radio><fechaentrada>{ this.booking.CheckInMMDDYYYY }</fechaentrada><fechasalida>{ this.booking.CheckOutMMDDYYYY }</fechasalida><marca></marca><afiliacion>{"RS"}</afiliacion><usuario>{"E12798"}</usuario><numhab1>{ this.booking.RoomsCount }</numhab1><paxes1>{ this.booking.AdultsCount }-{ this.booking.ChildrenCount }</paxes1><numhab2>0</numhab2><paxes2>0</paxes2><numhab3>0</numhab3><paxes3>0</paxes3><idioma>2</idioma><duplicidad>1</duplicidad><comprimido>2</comprimido><informacion_hotel>0</informacion_hotel></parametros></peticion>";

            byte[] re = this.SendRequest(xml);
            //string result = Encoding.GetEncoding(28591).GetString(re);
            string result = Encoding.UTF8.GetString(re);
            //Misc.SaveToFile(result);
            //Misc.SaveToFile(Misc.DecompressGZip(re));

            Hotel hotel = null;
            Room room = null;
            Rate rate = null;

            var rCol = new List<Hotel>();
            XElement xRoot = XElement.Parse(Misc.DecompressGZip(re));
            foreach (var xHotel in xRoot.Element("param").Element("hotls").Elements("hot"))
            {
                hotel = new Hotel();
                rCol.Add(hotel);

                hotel.Code = (int)xHotel.Element("cod");
                hotel.Name = (string)xHotel.Element("nom");
                hotel.Stars = (int)xHotel.Element("cat");

                foreach (var xRoom in xHotel.Element("res").Element("pax").Elements("hab"))
                {
                    room = new Room(hotel);
                    hotel.Rooms.Add(room);

                    room.Code = (string)xRoom.Attribute("cod");
                    room.Name = (string)xRoom.Attribute("desc");

                    foreach (var xRate in xRoom.Elements("reg"))
                    {
                        rate = new Rate(room);
                        room.Rates.Add(rate);

                        rate.BoardCode = (string)xRate.Attribute("cod");
                        rate.Net = (float)xRate.Attribute("prr");
                    }
                }
            }
            return rCol;
        }

        public void UpdateInfo(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRates(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRooms(Hotel hotel)
        {
            throw new NotImplementedException();
        }
    }

    public class XmlGoGlobalProvider : IProvider
    {
        private Booking booking;

        public XmlGoGlobalProvider(Booking booking)
        {
            this.booking = booking;
        }

        public Hotel GetHotel()
        {
            throw new NotImplementedException();
        }

        public async Task<List<Hotel>> GetHotelsAsync()
        {
            var task = Task.Factory.StartNew(new Func<List<Hotel>>(() =>
            {
                return this.GetHotels();
            }));
            return await task;
        }

        public List<Hotel> GetHotels()
        {
            string postData = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><soap12:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:soap12=\"http://www.w3.org/2003/05/soap-envelope\"><soap12:Body><MakeRequest xmlns=\"http://www.goglobal.travel/\"><requestType>11</requestType><xmlRequest><![CDATA[ <Root><Header><Agency>1523402</Agency><User>T3TRORTXML</User><Password></Password><Operation>HOTEL_SEARCH_REQUEST</Operation><OperationType>Request</OperationType></Header><Main Version=\"2\" ResponseFormat=\"JSON\" Currency=\"USD\" IncludeRating=\"true\" MaxHotels=\"200\"><SortOrder>1</SortOrder><FilterPriceMin>0</FilterPriceMin><FilterPriceMax>10000</FilterPriceMax><MaximumWaitTime></MaximumWaitTime><MaxResponses>1000</MaxResponses><FilterRoomBasises><FilterRoomBasis></FilterRoomBasis></FilterRoomBasises><HotelName></HotelName><Apartments>false</Apartments><CityCode>{this.GetCityCode()}</CityCode><ArrivalDate>{this.booking.CheckIn.ToString("yyyy-MM-dd")}</ArrivalDate><Nights>{this.booking.TotalNights}</Nights><Rooms><Room Adults=\"{this.booking.AdultsCount}\" RoomCount=\"{this.booking.RoomsCount}\" ></Room></Rooms></Main></Root>]]></xmlRequest></MakeRequest></soap12:Body></soap12:Envelope>";

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://xml.qa.goglobal.travel/XMLWebService.asmx");
            request.Method = "POST";
            request.ContentType = "application/soap+xml; charset=utf-8";

            byte[] dArr = Encoding.UTF8.GetBytes(postData);
            request.ContentLength = dArr.Length;

            Stream dataStream = request.GetRequestStream();
            dataStream.Write(dArr, 0, dArr.Length);
            dataStream.Close();

            WebResponse response = request.GetResponse();
            dataStream = response.GetResponseStream();
            StreamReader sr = new StreamReader(dataStream);
            string result = sr.ReadToEnd();

            sr.Close();
            dataStream.Close();
            response.Close();

            string jString = null;
            JObject jObject = null;

            try
            {
                XElement xEle = XElement.Parse(result);
                XNamespace nsSoap = XNamespace.Get("http://www.w3.org/2003/05/soap-envelope");
                XNamespace ns = XNamespace.Get("http://www.goglobal.travel/");
                jString = xEle.Element(nsSoap + "Body").Element(ns + "MakeRequestResponse").Element(ns + "MakeRequestResult").Value;
                jObject = JObject.Parse(jString);
            }
            catch
            {
                Misc.Log(this.booking.AsText);
                Misc.Log(jString);
            }

            List<Hotel> hCol = this.parseJsonHotels(jObject);
            return hCol;
        }

        public void UpdateInfo(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRates(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRooms(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        internal List<Hotel> parseJsonHotels(JObject jObject)
        {
            Hotel hotel = null;
            Room room = null;
            Rate rate = null;
            List<Hotel> rCol = new List<Hotel>();

            //JArray jHotels = (JArray)jObject["Hotels"];
            //JArray jOffers = null;
            //foreach (JObject jHotel in jHotels)
            //{
            //    hotel = new Hotel();
            //    rCol.Add(hotel);

            //    hotel.Code = (int)jHotel["HotelCode"];
            //    //hotel.Name = (string)jHotel["HotelName"];
            //    //hotel.Image = (string)jHotel["Thumbnail"];
            //    //hotel.Street = (string)jHotel["Location"];
            //    //hotel.Longitude = (string)jHotel["Longitude"];
            //    //hotel.Latitude = (string)jHotel["Latitude"];
            //    //hotel.TARating = (float)jHotel["Rating"];
            //    //hotel.TARatingImage = (string)jHotel["RatingImage"];
            //    //hotel.ReviewsCount = ((string)jHotel["ReviewCount"]).IntOrDefault();

            //    jOffers = (JArray)jHotel["Offers"];
            //    foreach (JObject jOffer in jOffers)
            //    {
            //        room = new Room(hotel);
            //        hotel.Rooms.Add(room);

            //        rate = new Rate(room);
            //        room.Rates.Add(rate);

            //        rate.BoardCode = (string)jOffer["RoomBasis"];
            //        // room.CancellationDate = DateTime.ParseExact((string)jRoom["CxlDeadLine"], "dd/MMM/yyyy", CultureInfo.InvariantCulture);
            //        room.CancellationDate = (string)jOffer["CxlDeadLine"];
            //        //room.Type = (string)((JArray)jOffer["Rooms"]).First;
            //        room.Availability = 1;
            //        room.Price = float.Parse((string)jOffer["TotalPrice"]);
            //        hotel.Stars = int.Parse((string)jOffer["Category"]);
            //        hotel.Rooms.Add(room);
            //    }

            //    hCol.Add(hotel);
            //}

            return rCol;
        }

        internal static List<Hotel> GoGlobalHotelsParseXml(XElement xElement)
        {
            throw new NotImplementedException();

            //XNamespace nsSoap = XNamespace.Get("http://www.w3.org/2003/05/soap-envelope");
            //XNamespace ns = XNamespace.Get("http://www.goglobal.travel/");

            // IEnumerable<XElement> xEle = xElement.Element(ns + "Body").Element("MakeRequestResponse").Element(ns2 + "MakeRequestResult").Element(ns2 + "Root").Elements("Header");

            //IEnumerable<XElement> xEle = xElement.Descendants(ns + "Hotel");
            //foreach (XElement item in xEle)
            //{
            //    Hotel hotel = new Hotel();
            //    hotel.Name = (string)item.Element(ns + "HotelName").Value;
            //    hotel.RoomType = (string)item.Element(ns + "RoomType").Value;
            //    hotel.MealType = (string)item.Element(ns + "RoomBasis").Value;
            //    hotel.Price = float.Parse(item.Element(ns + "TotalPrice").Value);
            //    hCol.Add(hotel);
            //}

            //return hCol;
        }

        internal string GetCityCode()
        {
            if (string.IsNullOrEmpty(this.booking.Place))
                return "563";

            string place = this.booking.Place.Split(',')[0].ToLower();
            if (place == "istanbul")
                return "889";
            else if (place == "dubai")
                return "563";
            else if (place == "amman")
                return "72";
            else if (place == "cairo")
                return "329";
            else if (place == "beirut")
                return "204";

            return "563";
        }
    }

    public class XmlConsoProvider : IProvider
    {
        private Booking booking;

        public XmlConsoProvider(Booking booking)
        {
            this.booking = booking;
        }

        public Hotel GetHotel()
        {
            throw new NotImplementedException();
        }

        public async Task<List<Hotel>> GetHotelsAsync()
        {
            var task = Task.Factory.StartNew(new Func<List<Hotel>>(() =>
            {
                return this.GetHotels();
            }));
            return await task;
        }

        public List<Hotel> GetHotels()
        {
            throw new NotImplementedException();

            //XElement xEle = XElement.Load(
            //    string.Format("https://hotelacc.resfinity.net/hotels/test.consoxml,100000/{0}/{1}-{2}/{3}ADT?token=xgnmdo9bq5xo743hwdjr",
            //    XmlDataSource.GetIATACode(this.booking.Place),
            //    this.booking.CheckIn.ToString("yyyyMMdd"),
            //    this.booking.CheckOut.ToString("yyyyMMdd"),
            //    this.booking.AdultsCount));

            //List<XElement> xCol = xEle.Element("transaction").Elements("segment").ToList();
            //List<Hotel> hCol = new List<Hotel>();
            //for (int i = 0; i < xCol.Count; i++)
            //{
            //    XElement xElement = xCol[i];

            //    Hotel hotel = new Hotel();
            //    hotel.Id = xElement.Element("hotel").Attribute("mh").Value;
            //    hotel.Name = xElement.Element("hotel").Attribute("name").Value;
            //    hotel.Stars = (int)float.Parse(xElement.Element("hotel").Attribute("cat").Value);
            //    hotel.RoomType = xElement.Element("rates").Element("rate").Element("name").Value;
            //    hotel.RoomName = xElement.Element("rates").Element("rate").Element("room").Element("name").Value;
            //    hotel.MealType = xElement.Element("rates").Element("rate").Element("meal").Attribute("type").Value;
            //    hotel.Price = float.Parse(xElement.Element("rates").Element("price").Attribute("value").Value);
            //    hotel.Image = $"/img/test/img ({(i + 1) % 20}).jpg";

            //    hCol.Add(hotel);
            //}
            //return hCol;
        }

        public void UpdateInfo(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRates(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        public void UpdateRooms(Hotel hotel)
        {
            throw new NotImplementedException();
        }

        internal XElement GetHotelsFromResfinity(Booking booking)
        {
            // return OnlineData.GetHotelsFromGoGlobal(booking);

            //FileStream fs = new FileStream(HttpContext.Current.Server.MapPath("~/hotel_data.xml"), FileMode.Open);
            //return XElement.Load(fs);

            return XElement.Load(
                string.Format("https://hotelacc.resfinity.net/hotels/test.consoxml,100000/{0}/{1}-{2}/{3}ADT?token=xg03gw9o38cec9uhwdjr",
                XmlDataSource.GetIATACode(this.booking.Place),
                booking.CheckIn.ToString("yyyyMMdd"),
                booking.CheckOut.ToString("yyyyMMdd"),
                booking.AdultsCount));
        }

        internal XElement GetRooms(Booking booking)
        {
            //FileStream fs = new FileStream(HttpContext.Current.Server.MapPath("~/room_data.xml"), FileMode.Open);
            //return XElement.Load(fs);

            return XElement.Load(
                string.Format("https://hotelacc.resfinity.net/hotel/test.consoxml,100000/{0}/{1}-{2}/{3}ADT?token=xg03gw9o38cec9uhwdjr",
                booking.SelectedHotel.Code,
                booking.CheckIn.ToString("yyyyMMdd"),
                booking.CheckOut.ToString("yyyyMMdd"),
                booking.AdultsCount));
        }

        internal XElement GetHotelInfo(Hotel hotel)
        {
            //FileStream fs = new FileStream(HttpContext.Current.Server.MapPath("~/hotel_info.xml"), FileMode.Open);
            //return XElement.Load(fs);

            return XElement.Load(
                string.Format("https://hotelacc.resfinity.net/hotel_info/test.consoxml,100000/{0}?token=xg03gw9o38cec9uhwdjr",
                hotel.Code));
        }

        // TODO: replaced this method by calling OnlineData directly from Booking object, move this deserialization process to OnlineData
        internal List<Hotel> GetHotels(Booking booking)
        {
            List<Hotel> hCol = new List<Hotel>();

            //try
            //{
            //    XElement root = OnlineData.GetHotelsFromResfinity(booking);

            //    //foreach (XElement e in root.Element("transaction").Elements("segment"))
            //    //    hCol.Add(Hotel.Deserialize(e));

            //    List<XElement> xCol = root.Element("transaction").Elements("segment").ToList();
            //    for (int i = 0; i < xCol.Count; i++)
            //        hCol.Add(Hotel.Deserialize(xCol[i], i));

            //    Random random = new Random();
            //    foreach (Hotel hotel in hCol)
            //        hotel.ReviewsCount = random.Next(1005, 1980);
            //}
            //catch (Exception ex)
            //{
            //    Misc.Log(ex.GetBaseException().ToString());
            //    hCol.Add(Hotel.ErroneousHotel);
            //}

            return hCol;
        }

        internal void UpdateInfo()
        {
            //XElement hotel = OnlineData.GetHotelInfo(this).Element("transaction").Element("segment").Element("hotel");
            //this.Latitude = hotel.Element("geo")?.Attribute("lat")?.Value ?? "0.0";
            //this.Longitude = hotel.Element("geo")?.Attribute("lon")?.Value ?? "0.0";
            //this.City = hotel.Element("address")?.Element("city")?.Value ?? "Unknown City";
            //this.Street = hotel.Element("address")?.Element("street")?.Value ?? "Unknown Street";
            //this.Phone = hotel.Element("address")?.Element("phone")?.Value ?? "Phone unavailable";
            //this.Email = hotel.Element("address")?.Element("email")?.Value ?? "Email unavailable";
            //this.Website = hotel.Element("address")?.Element("website")?.Value ?? "Website unavailable";

            //// foreach (XElement description in hotel.Element("descriptions")?.Elements("description").Take(13))

            //this.About = hotel?.Elements("descriptions")?.Elements("description")?.Elements("texts")?.Elements("text")?.Select(a => a.Value).Aggregate(new StringBuilder(), (sb, line) => sb.AppendLine(line)).ToString();

            //this.ImagesUrls = new List<string>(hotel?.Element("descriptions")?.Elements("description")?.Elements("images")?.Elements("image")?.Take(22).Select(a => a.Attribute("uri")?.Value) ?? new List<string>());
        }

        // TODO: replaced this method by another one which belongs to OnlineData
        internal bool UpdateRooms()
        {
            try
            {
                //this.UpdateInfo();

                //XElement xElement = OnlineData.GetRooms(Prog.SessionCache.CurrentUser.CurrentBooking);
                // List<XElement> rates = xElement.Element("transaction").Element("segment").Elements("rates").ToList();
                // List<Room> rCol = new List<Room>();
                //foreach (XElement xE in rates)
                //    rCol.Add(Room.Deserialize(this, xE));

                //for (int i = 0; i < rates.Count; i++)
                //    rCol.Add(Room.Deserialize(this, rates[i], i));

                // this.Rooms = rCol.OrderBy(a => a.Price).ToList();
                return true;
            }
            catch { return false; }
        }

        public static Room Deserialize(Hotel hotel, XElement xElement, int index)
        {
            throw new NotImplementedException();

            //Room room = new Room();
            //room.Hotel = hotel;
            //room.Id = xElement.Attribute("mhr").Value;
            //room.Source = xElement.Attribute("source").Value;
            //room.Mesh = xElement.Attribute("mesh").Value;
            //room.ProviderCode = xElement.Attribute("provider_code").Value;
            //room.ProviderBookingCode = xElement.Attribute("provider_booking_code").Value;
            //room.Name = xElement.Element("rate").Element("room").Element("name").Value;
            //room.Type = xElement.Element("rate").Element("name").Value;
            //room.MealType = xElement.Element("rate").Element("meal").Attribute("type").Value;
            //room.Meal = xElement.Element("rate").Element("meal").Value;
            //room.AdultsCount = int.Parse(xElement.Element("rate").Element("occupancy").Element("pax").Attribute("count").Value);
            //room.Price = float.Parse(xElement.Element("price").Attribute("value").Value, System.Globalization.NumberStyles.AllowDecimalPoint);
            //room.Image = hotel.ImagesUrls?[index % hotel.ImagesUrls.Count] ?? "/img/images/hotel-4.jpg";
            //return room;
        }

        private static Hotel Deserialize(XElement xElement, int index)
        {
            throw new NotImplementedException();

            //Hotel hotel = new Hotel();
            //hotel.Id = xElement.Element("hotel").Attribute("mh").Value;
            //hotel.Name = xElement.Element("hotel").Attribute("name").Value;
            //hotel.Stars = (int)float.Parse(xElement.Element("hotel").Attribute("cat").Value);
            //hotel.RoomType = xElement.Element("rates").Element("rate").Element("name").Value;
            //hotel.RoomName = xElement.Element("rates").Element("rate").Element("room").Element("name").Value;
            //hotel.MealType = xElement.Element("rates").Element("rate").Element("meal").Attribute("type").Value;
            //hotel.Price = float.Parse(xElement.Element("rates").Element("price").Attribute("value").Value);
            //hotel.Image = $"/img/test/img ({(index + 1) % 20}).jpg";
            //return hotel;
        }
    }

    public class XmlUtilities
    {
        private List<MutualHotel> globalCol;
        private List<MutualHotel> bedsCol;
        private List<MutualHotel> restelCol;

        public XmlUtilities() { }

        internal static string Normalize(string phoneString)
        {
            return phoneString.Replace("\"", "").Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        }

        public static void FixRestelFile()
        {
            int lineCount = 0;
            string newLine = "";
            using (StreamReader sReader = new StreamReader(File.OpenRead("/files/restelHotels.xml"), Encoding.GetEncoding("ISO-8859-1")))
            using (StreamWriter sWriter = new StreamWriter(File.OpenWrite("/files/restelHotels2.xml"), Encoding.GetEncoding("ISO-8859-1")))
            {
                while (!sReader.EndOfStream)
                {
                    lineCount++;
                    if (lineCount == 2223191)
                    {
                        string lineText = sReader.ReadLine();
                        newLine = lineText.Replace("\u0003", "-");
                        //char[] chars = lineText.ToCharArray();
                    }
                    else
                    {
                        newLine = sReader.ReadLine();
                    }
                    sWriter.WriteLine(newLine);
                }
            }
        }

        public static void ConvertStructure()
        {
            string path2 = "/files/hotelBedsHotels2.json";
            string path3 = "/files/hotelBedsHotels3.json";

            //convert from structure 2 to 3
            {
                using (FileStream thirdFile = File.OpenWrite(path3))
                using (StreamReader sReader = File.OpenText(path2))
                using (JsonTextReader jReader = new JsonTextReader(sReader))
                {
                    thirdFile.WriteByte((byte)'[');

                    jReader.SupportMultipleContent = true;
                    var serializer = new JsonSerializer();

                    int counter = 0;
                    int hotelsArrCounter = 0;
                    int hotelsCounter = 0;

                    jReader.Read();
                    if (jReader.Depth == 0 && jReader.TokenType == JsonToken.StartArray)
                    {
                        while (jReader.Read())
                        {
                            if (jReader.Depth == 1 && jReader.TokenType == JsonToken.StartObject)
                            {
                                counter++;
                                while (jReader.Read())
                                {
                                    if (jReader.Depth == 2 && jReader.TokenType == JsonToken.PropertyName && jReader.Value.Equals("hotels"))
                                    {
                                        hotelsArrCounter++;
                                        jReader.Read();
                                        while (jReader.Read())
                                        {
                                            if (jReader.Depth == 3 && jReader.TokenType == JsonToken.StartObject)
                                            {
                                                hotelsCounter++;
                                                jReader.Skip();
                                                //JObject jHotel = serializer.Deserialize<JObject>(jReader);
                                                //byte[] bytes = Encoding.UTF8.GetBytes(jHotel.ToString());
                                                //thirdFile.Write(bytes, 0, bytes.Length);
                                                //thirdFile.WriteByte((byte)',');
                                            }
                                            else if (jReader.TokenType == JsonToken.EndArray)
                                                break;
                                        }
                                    }
                                    else if (jReader.Depth == 1 && jReader.TokenType == JsonToken.EndObject)
                                        break;
                                    else
                                        jReader.Skip();
                                }
                            }
                        }
                    }

                    thirdFile.Seek(1, SeekOrigin.End);
                    thirdFile.WriteByte((byte)']');
                }
            }
        }

        public static List<MutualHotel> ReadGoGlobal()
        {
            string goGlobalPath = "/files/Extended.csv";

            return File.ReadLines(goGlobalPath).Skip(1).Select(a => a.Split('|')).Select(a => new MutualHotel()
            {
                Id = int.Parse(a[5].Trim('"')),
                Name = a[6].Trim('"'),
                City = a[4].Trim('"'),
                Phone = a[8].Trim('"'),
                Longitude = a[12].Trim('"').FloatOrDefault(),
                Latitude = a[13].Trim('"').FloatOrDefault()
            }).ToList();
        }

        public static List<MutualHotel> ReadRestel()
        {
            int nullCount = 0;
            int counter = 0;
            string path = "/files/restelHotels2.xml";

            string temp2 = "";
            string temp9109 = "";

            List<MutualHotel> rCol = new List<MutualHotel>();

            using (var rReader = XmlReader.Create(new StreamReader(path, Encoding.GetEncoding("ISO-8859-1")), new XmlReaderSettings() { CheckCharacters = false }))
            {
                rReader.MoveToContent();
                while (rReader.Read())
                {
                    if (rReader.NodeType == XmlNodeType.Element && rReader.Name == "root")
                        ;
                    else if (rReader.NodeType == XmlNodeType.Element && rReader.Name == "respuesta")
                    {
                        counter++;
                        //rReader.Skip();

                        XElement xHotel = (XElement.ReadFrom(rReader) as XElement).Element("parametros").Element("hotel");
                        if (xHotel == null)
                        {
                            nullCount++;
                            continue;
                        }

                        int id = (int)xHotel.Element("codigo_hotel");
                        string phone = (string)xHotel.Element("telefono");
                        int duplicate = (int)xHotel.Element("coddup");

                        if (duplicate > 0)
                            continue;

                        if (duplicate == 2)
                            temp2 += xHotel.ToString();
                        else if (duplicate == 9109)
                            temp9109 += xHotel.ToString();

                        rCol.Add(new MutualHotel()
                        {
                            Id = id,
                            Name = (string)xHotel.Element("nombre_h"),
                            CityCode = (string)xHotel.Element("codprovincia"),
                            City = (string)xHotel.Element("provincia"),
                            Email = (string)xHotel.Element("mail"),
                            Phone = phone,
                            Address = (string)xHotel.Element("direccion"),
                            Longitude = ((string)xHotel.Element("longitud")).FloatOrDefault(),
                            Latitude = ((string)xHotel.Element("latitud")).FloatOrDefault(),
                            Tag = duplicate
                        });
                    }
                }
            }

            return rCol;
        }

        public static List<MutualHotel> ReadHotelBeds()
        {
            string hotelBedsPath = "/files/hotelBedsHotels2.json";

            int counter = 0;
            List<MutualHotel> bCol = new List<MutualHotel>();

            var serializer = new JsonSerializer();
            using (StreamReader sReader = File.OpenText(hotelBedsPath))
            using (JsonTextReader jReader = new JsonTextReader(sReader))
            {
                jReader.SupportMultipleContent = true;

                jReader.Read();
                while (jReader.Read())
                {
                    if (jReader.Depth == 1 && jReader.TokenType == JsonToken.StartObject)
                    {
                        counter++;
                        //jReader.Skip();
                        JObject jHotel = serializer.Deserialize<JObject>(jReader);
                        bCol.Add(new MutualHotel()
                        {
                            Id = jHotel.Value<int>("code"),
                            Name = jHotel.Value<JObject>("name").Value<string>("content"),
                            CityCode = jHotel.Value<string>("destinationCode"),
                            City = jHotel.Value<JObject>("city").Value<string>("content"),
                            Phone = jHotel.Value<JArray>("phones")?.Values<string>("phoneNumber")?.First(),
                            Email = jHotel.Value<string>("email"),
                            POBox = jHotel.Value<string>("postalCode"),
                            Address = jHotel.Value<JObject>("address")?.Value<string>("content"),
                            Longitude = jHotel.Value<JObject>("coordinates")?.Value<float>("longitude") ?? 0,
                            Latitude = jHotel.Value<JObject>("coordinates")?.Value<float>("latitude") ?? 0
                        });
                    }
                }
            }

            return bCol;
        }

        public void Read()
        {
            using (FileStream fStr = File.OpenRead("/files/gCol.bin")) this.globalCol = (List<MutualHotel>)new BinaryFormatter().Deserialize(fStr);
            using (FileStream fStr = File.OpenRead("/files/bCol.bin")) this.bedsCol = (List<MutualHotel>)new BinaryFormatter().Deserialize(fStr);
            using (FileStream fStr = File.OpenRead("/files/rCol.bin")) this.restelCol = (List<MutualHotel>)new BinaryFormatter().Deserialize(fStr);
        }

        public void Save()
        {
            using (FileStream fStr = File.OpenWrite("/files/gCol.bin")) new BinaryFormatter().Serialize(fStr, this.globalCol);
            using (FileStream fStr = File.OpenWrite("/files/bCol.bin")) new BinaryFormatter().Serialize(fStr, this.bedsCol);
            using (FileStream fStr = File.OpenWrite("/files/rCol.bin")) new BinaryFormatter().Serialize(fStr, this.restelCol);
        }

        public void Merge()
        {
            this.globalCol = XmlUtilities.ReadGoGlobal();
            this.bedsCol = XmlUtilities.ReadHotelBeds();
            this.restelCol = XmlUtilities.ReadRestel();

            //this.Read();
            this.TestF();
            //this.Save();
        }

        private void TestF()
        {
            foreach (var item in this.bedsCol) item.Duplicates?.Clear();

            SortedList<string, MutualHotel> bSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.bedsCol)
                if (!bSCol.ContainsKey(item.GeoLocationTextRound2)) bSCol.Add(item.GeoLocationTextRound2, item);
                else bSCol[item.GeoLocationTextRound2].Duplicates.Add(item);

            MutualHotel mTemp = null;

            foreach (var item in this.restelCol)
                if (bSCol.TryGetValue(item.GeoLocationTextRound2, out mTemp))
                    //item.Match = mTemp;
                    mTemp.RestelHotel = item;

            foreach (var item in this.globalCol)
                if (bSCol.TryGetValue(item.GeoLocationTextRound2, out mTemp))
                    //item.Match = mTemp;
                    mTemp.GoGlobalHotel = item;

            //var idCol = new SortedList<MutualHotel, int>();
            //foreach (var item in this.restelCol)
            //    if (!(item.Match == null) && item.Match.Duplicates.Count == 0)
            //        idCol.Add(item.Match.Id, item.Id);

            //var aCol = this.restelCol.Where(a => !(a.Match == null) && a.Match.Duplicates.Count == 0).ToList();
            //string aText = aCol.Aggregate(new StringBuilder(), (sb, item) => sb.AppendFormat("{0,-70}{1,-70}", item.Name, item.Match.Name).AppendLine()).ToString();

            var sCol = this.bedsCol.Where(a => a.Duplicates.Count == 0).Aggregate(new StringBuilder(), (sb, item) => sb.AppendFormat("{0,-10}{1,-10}{2,-10}{3,-50}{4,-50}{5}", item.Id, item.RestelHotel?.Id ?? 0, item.GoGlobalHotel?.Id ?? 0, item.Name, item.RestelHotel?.Name, item.GoGlobalHotel?.Name).AppendLine()).ToString();

            var sCol2 = this.bedsCol.Where(a => a.Duplicates.Count == 0).Aggregate(new StringBuilder(), (sb, item) => sb.AppendFormat("{0},{1},{2}", item.Id, item.RestelHotel?.Id, item.GoGlobalHotel?.Id).AppendLine()).ToString();

            //var rCol = this.bedsCol.Where(a => !(a.RestelHotel == null)).Count();

            var varA = Misc.AggregateAsString(
                this.bedsCol.Where(a => a.CityCode == "DXB").OrderBy(a => a.Name).Select(a => string.Format("{0,-50}{1,-30}{2,-20}{3}", a.Name.SubStringExt(40), a.GeoLocationText, a.Phone, a.Address)));
            var varB = Misc.AggregateAsString(
                this.restelCol.Where(a => a.CityCode == "AEDXB").OrderBy(a => a.Name).Select(a => string.Format("{0,-50}{1,-30}{2,-20}{3}", a.Name.SubStringExt(40), a.GeoLocationText, a.Phone, a.Address)));

            return;
        }

        private void TestE()
        {
            var rSCol = this.GenerateIndex(23, this.restelCol) as SortedList<string, MutualHotel>;
            var bSCol = this.GenerateIndex(23, this.bedsCol) as SortedList<string, MutualHotel>;

            int count = rSCol.Values.Where(a => a.Tag > 0).Count();
        }

        private void TestD()
        {
            this.globalCol.Where(a => a.Duplicates.Count > 0).ToList().ForEach(a => a.Duplicates.Clear());
            this.bedsCol.Where(a => a.Duplicates.Count > 0).ToList().ForEach(a => a.Duplicates.Clear());
            this.restelCol.Where(a => a.Duplicates.Count > 0).ToList().ForEach(a => a.Duplicates.Clear());

            var gSCol = this.GenerateIndex(20, this.globalCol) as SortedList<string, MutualHotel>;
            var bSCol = this.GenerateIndex(20, this.bedsCol) as SortedList<string, MutualHotel>;
            var rSCol = this.GenerateIndex(20, this.restelCol) as SortedList<string, MutualHotel>;

            var gDuplicated = this.globalCol.Where(a => a.Duplicates.Count > 0)
                .Aggregate(new StringBuilder(), (sb, item) =>
                {
                    sb.AppendLine(item.ToString());
                    item.Duplicates.Aggregate(sb, (sb2, item2) => sb2.AppendLine(item2.ToString()));
                    sb.AppendLine();
                    return sb;
                }).ToString();

            var bDuplicated = this.bedsCol.Where(a => a.Duplicates.Count > 0)
                .Aggregate(new StringBuilder(), (sb, item) =>
                {
                    sb.AppendLine(item.ToString());
                    item.Duplicates.Aggregate(sb, (sb2, item2) => sb2.AppendLine(item2.ToString()));
                    sb.AppendLine();
                    return sb;
                }).ToString();

            var rDuplicated = this.restelCol.Where(a => a.Duplicates.Count > 0)
                .Aggregate(new StringBuilder(), (sb, item) =>
                {
                    sb.AppendLine(item.ToString());
                    item.Duplicates.Aggregate(sb, (sb2, item2) => sb2.AppendLine(item2.ToString()));
                    sb.AppendLine();
                    return sb;
                }).ToString();

            var gCount = this.globalCol.Where(a => a.Duplicates.Count > 0).ToList();
            var bCount = this.bedsCol.Where(a => a.Duplicates.Count > 0).ToList();
            var rCount = this.restelCol.Where(a => a.Duplicates.Count > 0).ToList();

            MutualHotel temp = null;
            List<MutualHotel> mCol = new List<MutualHotel>();
            foreach (var a in gSCol.Keys)
                if (bSCol.TryGetValue(a, out temp))
                {
                    temp.GoGlobalHotel = gSCol[a];
                    mCol.Add(temp);
                }

            string value = mCol.Aggregate(new StringBuilder(), (sb, item) => sb.AppendFormat(
                "{0,-30}{1,-30}{2,-50}{3,-50}",
                item.GeoLocationText, item.GoGlobalHotel.GeoLocationText, item.Name, item.GoGlobalHotel.Name).AppendLine()).ToString();
        }

        private void TestC()
        {
            foreach (var item in this.globalCol) item.GeoLocation = GeoInfo.Default;
            foreach (var item in this.bedsCol) item.GeoLocation = GeoInfo.Default;
            foreach (var item in this.restelCol) item.GeoLocation = GeoInfo.Default;

            var gDupli = new List<string>();
            var gSCol = new SortedList<GeoInfo, MutualHotel>(new GeoInfoComparer());
            foreach (var item in this.globalCol)
                if (!gSCol.ContainsKey(item.GeoLocation)) gSCol.Add(item.GeoLocation, item);
                else gDupli.Add($"{item.GeoLocation}\t\t{item.City}\t\t{item.Name}");

            string gDuplicated = Misc.AggregateAsString(gDupli.OrderBy(a => a));

            var bDupli = new List<string>();
            var bSCol = new SortedList<GeoInfo, MutualHotel>(new GeoInfoComparer());
            foreach (var item in this.bedsCol)
                if (!bSCol.ContainsKey(item.GeoLocation)) bSCol.Add(item.GeoLocation, item);
                else bDupli.Add($"{item.GeoLocation}\t\t{item.City}\t\t{item.Name}");

            string bDuplicated = Misc.AggregateAsString(bDupli.OrderBy(a => a));

            var rDupli = new List<string>();
            var rSCol = new SortedList<GeoInfo, MutualHotel>(new GeoInfoComparer());
            foreach (var item in this.restelCol)
                if (!rSCol.ContainsKey(item.GeoLocation)) rSCol.Add(item.GeoLocation, item);
                else rDupli.Add($"{item.GeoLocation}\t\t{item.City}\t\t{item.Name}");

            string rDuplicated = Misc.AggregateAsString(rDupli.OrderBy(a => a));

            MutualHotel temp = null;
            List<MutualHotel> mCol = new List<MutualHotel>();
            foreach (var a in gSCol.Keys)
                if (bSCol.TryGetValue(a, out temp))
                {
                    temp.GoGlobalHotel = gSCol[a];
                    mCol.Add(temp);
                }

            string value = mCol.Aggregate(new StringBuilder(), (sb, item) => sb.AppendFormat(
                "{0,-30}\t{1,-30}\t{2,-50}\t{3,-50}",
                item.GeoLocation, item.GoGlobalHotel.GeoLocation, item.Name, item.GoGlobalHotel.Name).AppendLine()).ToString();
        }

        private void TestB()
        {
            var bDupli = new List<string>();
            var bSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.bedsCol)
                if (!bSCol.ContainsKey(item.Email2)) bSCol.Add(item.Email2, item);
                else bDupli.Add($"{item.Email2}\t\t{item.City}\t\t{item.Name}");

            string bDuplicated = Misc.AggregateAsString(bDupli.OrderBy(a => a));

            var rDupli = new List<string>();
            var rSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.restelCol)
                if (!rSCol.ContainsKey(item.Email2)) rSCol.Add(item.Email2, item);
                else rDupli.Add($"{item.Email2}\t\t{item.City}\t\t{item.Name}");

            string rDuplicated = Misc.AggregateAsString(rDupli.OrderBy(a => a));

            int counter = 0;
            foreach (var a in bSCol.Keys)
                if (rSCol.ContainsKey(a))
                    counter++;
        }

        private void TestA()
        {
            var gDupli = new List<string>();
            var gSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.globalCol)
                if (!gSCol.ContainsKey(item.Phone2)) gSCol.Add(item.Phone2, item);
                else gDupli.Add($"{item.Phone2}\t\t{item.City}\t\t{item.Name}");

            string gDuplicated = Misc.AggregateAsString(gDupli.OrderBy(a => a));

            var bDupli = new List<string>();
            var bSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.bedsCol)
                if (!bSCol.ContainsKey(item.Phone2)) bSCol.Add(item.Phone2, item);
                else bDupli.Add($"{item.Phone2}\t\t{item.City}\t\t{item.Name}");

            string bDuplicated = Misc.AggregateAsString(bDupli.OrderBy(a => a));

            var rDupli = new List<string>();
            var rSCol = new SortedList<string, MutualHotel>();
            foreach (var item in this.restelCol)
                if (!rSCol.ContainsKey(item.Phone2)) rSCol.Add(item.Phone2, item);
                else rDupli.Add($"{item.Phone2}\t\t{item.City}\t\t{item.Name}");

            string rDuplicated = Misc.AggregateAsString(rDupli.OrderBy(a => a));

            int counter = 0;
            foreach (var a in rSCol.Keys)
                if (bSCol.ContainsKey(a))
                    counter++;
        }

        private object GenerateIndex(int type, List<MutualHotel> collection)
        {
            if (type == 20)
            {
                var sCol = new SortedList<string, MutualHotel>();
                foreach (var item in collection)
                    if (!sCol.ContainsKey(item.GeoLocationText)) sCol.Add(item.GeoLocationText, item);
                    else
                        sCol[item.GeoLocationText].Duplicates.Add(item);
                return sCol;
            }
            else if (type == 23)
            {
                var sCol = new SortedList<string, MutualHotel>();
                foreach (var item in collection)
                    if (!sCol.ContainsKey(item.GeoLocationTextRound3)) sCol.Add(item.GeoLocationTextRound3, item);
                    else
                        sCol[item.GeoLocationTextRound3].Duplicates.Add(item);
                return sCol;
            }

            return null;
        }
    }

    public class PermissionException : Exception
    {
        public PermissionException() { }
    }

    public class TimedWebClient : WebClient
    {
        public int Timeout { get; set; }

        public TimedWebClient(int timeout)
        {
            this.Timeout = timeout;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = this.Timeout;
            return request;
        }
    }

    [Serializable]
    public struct GeoInfo
    {
        public float Longitude { get; set; }
        public float Latitude { get; set; }
        public float Distance { get; set; }

        public static GeoInfo Default { get { return default(GeoInfo); } }

        public GeoInfo(float longitude, float latitude)
        {
            this.Longitude = (float)Math.Round(longitude, 5, MidpointRounding.ToEven);
            this.Latitude = (float)Math.Round(latitude, 5, MidpointRounding.ToEven);
            this.Distance = (float)GeoInfo.Calc(this.Latitude, this.Longitude, 0, 0);
        }

        public static bool operator ==(GeoInfo first, GeoInfo second)
        {
            return first.Longitude == second.Longitude && first.Latitude == second.Latitude;
        }

        public static bool operator !=(GeoInfo first, GeoInfo second)
        {
            return !(first == second);
        }

        public override string ToString()
        {
            return $"({this.Longitude},{this.Latitude})";
        }

        public static double Calc(double Lat1, double Long1, double Lat2, double Long2)
        {
            /*
                The Haversine formula according to Dr. Math.
                http://mathforum.org/library/drmath/view/51879.html

                dlon = lon2 - lon1
                dlat = lat2 - lat1
                a = (sin(dlat/2))^2 + cos(lat1) * cos(lat2) * (sin(dlon/2))^2
                c = 2 * atan2(sqrt(a), sqrt(1-a)) 
                d = R * c

                Where
                    * dlon is the change in longitude
                    * dlat is the change in latitude
                    * c is the great circle distance in Radians.
                    * R is the radius of a spherical Earth.
                    * The locations of the two points in 
                        spherical coordinates (longitude and 
                        latitude) are lon1,lat1 and lon2, lat2.
            */

            double dDistance = Double.MinValue;
            double dLat1InRad = Lat1 * (Math.PI / 180.0);
            double dLong1InRad = Long1 * (Math.PI / 180.0);
            double dLat2InRad = Lat2 * (Math.PI / 180.0);
            double dLong2InRad = Long2 * (Math.PI / 180.0);

            double dLongitude = dLong2InRad - dLong1InRad;
            double dLatitude = dLat2InRad - dLat1InRad;

            // Intermediate result a.
            double a = Math.Pow(Math.Sin(dLatitude / 2.0), 2.0) +
                       Math.Cos(dLat1InRad) * Math.Cos(dLat2InRad) *
                       Math.Pow(Math.Sin(dLongitude / 2.0), 2.0);

            // Intermediate result c (great circle distance in Radians).
            double c = 2.0 * Math.Asin(Math.Sqrt(a));

            // Distance.
            // const Double kEarthRadiusMiles = 3956.0;
            const Double kEarthRadiusKms = 6376.5;
            dDistance = kEarthRadiusKms * c;

            return dDistance;
        }
    }

    public class GeoInfoComparer : IComparer<GeoInfo>
    {
        public int Compare(GeoInfo x, GeoInfo y)
        {
            //return x.Longitude.CompareTo(y.Longitude);
            return x.Distance.CompareTo(y.Distance);
        }
    }

    public static class Extension
    {
        public static IEnumerable<Hotel> OfStars(this IEnumerable<Hotel> source, int stars)
        {
            return source.Where(a => a.Stars == stars);
        }

        public static IEnumerable<Hotel> OfBoardType(this IEnumerable<Hotel> source, string mealType)
        {
            return source.Where(a => a.HasBoardType(mealType));
        }

        public static IEnumerable<Hotel> OfDistrict(this IEnumerable<Hotel> source, string district)
        {
            return source.Where(a => a.District.ToLower().Contains(district));
        }

        public static string SplitByComma(this IEnumerable<int> source)
        {
            if (source.Count() == 0)
                return null;

            return source.Aggregate(new StringBuilder(), (sb, item) => sb.Append($"{item},")).ToString().TrimEnd(',');
        }

        public static string SubStringExt(this string source, int count)
        {
            return new String(source.Take(count).ToArray());
        }

        public static int IntOrDefault(this string source, int defaultValue = 0)
        {
            int value;
            if (int.TryParse(source, out value)) return value;
            return defaultValue;
        }

        public static long LongOrDefault(this string source, long defaultValue = 0)
        {
            long value;
            if (long.TryParse(source, out value)) return value;
            return defaultValue;
        }

        public static float FloatOrDefault(this string source, float defaultValue = 0)
        {
            float value;
            if (float.TryParse(source, out value)) return value;
            return defaultValue;
        }

        public static bool BoolOrDefault(this string source, bool defaultValue = false)
        {
            bool value;
            if (bool.TryParse(source, out value)) return value;
            return defaultValue;
        }

        public static DateTime? DateTimeOrDefault(this string source, DateTime? defaultValue = null)
        {
            DateTime value;
            StringBuilder sb = new StringBuilder();
            sb.Append("DateTimeOrDefault, ").Append(source).Append(" ");
            if (DateTime.TryParseExact(source, new string[] { "dd/MM/yyyy", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:sszzz" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                sb.Append(true);
                return value;
            }
            else
            {
                sb.Append(false);
            }
            sb.AppendLine();
            Misc.Log(sb.ToString());

            return defaultValue;
        }

        public static int IntOrDefault(this JToken source, int defaultValue = 0)
        {
            return ((string)source).IntOrDefault();
        }

        public static long LongOrDefault(this JToken source, long defaultValue = 0)
        {
            return ((string)source).LongOrDefault();
        }

        public static DateTime? DateTimeOrDefault(this JToken source, DateTime? defaultValue = null)
        {
            return ((string)source).DateTimeOrDefault();
        }

        public static bool HasMember(ExpandoObject source, string memberName)
        {
            return ((IDictionary<string, object>)source).ContainsKey(memberName);
        }

        public static JObject Update(this JObject source, JObject newOne)
        {
            foreach (JProperty item in newOne.Properties())
                source[item.Name] = item.Value;
            return source;
        }
    }
}