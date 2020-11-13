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
using Newtonsoft.Json;
using System.Web.SessionState;
using System.Reflection;
using System.Collections;

namespace MSite.Controllers
{
    [HandleError(View = "IPNotFound")]
    public class HomeController : Controller
    {
        public ActionResult FWLink()
        {
            return this.FWLinkInternal((int)Prog.Terminal.RequestCache.linkId);
        }

        private ActionResult FWLinkInternal(int linkId)
        {
            Prog.Terminal.LastRequestCache = Prog.Terminal.RequestCache;

            switch (linkId)
            {
                case 101101:
                    Prog.Terminal.CompanyUnderEdit = Company.GetCompany((long)Prog.Terminal.RequestCache.companyId);
                    return this.RedirectToAction("SAAgent");
                case 101102:
                    Prog.Terminal.CompanyUnderEdit.SerializeWithAgent((DynamicBag)Prog.Terminal.RequestCache).DBSave(true, false);
                    return this.RedirectToAction("SAAgents");
                case 101103:
                    return this.RedirectToAction("SAAgents");
                case 101104:
                    return this.RedirectToAction("SAXmls");
                case 102101:
                    if (Prog.Terminal.SignIn((string)Prog.Terminal.RequestCache.userName, (string)Prog.Terminal.RequestCache.password))
                        return this.RedirectToAction("Index");
                    return this.RedirectToAction("ASSignUp");
                case 10210101:
                    if (Prog.Terminal.SignIn((string)Prog.Terminal.RequestCache.userName, (string)Prog.Terminal.RequestCache.password))
                        return new ContentResult() { Content = new JObject(new JProperty("approved", true)).ToString() };
                    return new ContentResult() { Content = new JObject(new JProperty("approved", false)).ToString() };
                case 102102:
                    Prog.Terminal.SignOut();
                    return this.RedirectToAction("Index");
                case 102103:
                    if (Prog.Database.SignUp(Prog.Terminal.RequestCache))
                        return this.RedirectToAction("ASSignIn");
                    return this.RedirectToAction("ASSignUp");
                case 102104:
                    return this.RedirectToAction("ASSignUp");
                case 102105:
                    switch (Prog.Terminal.User.Type)
                    {
                        case UserType.Admin:
                        case UserType.Agent:
                        case UserType.AgentSubUser:
                            return this.RedirectToAction("ASProfileHome");
                        default:
                            return this.RedirectToAction("ASSignIn");
                    }
                case 102106:
                    this.Session.Abandon();
                    return this.RedirectToAction("IPNotFound");
                case 103101:
                    if (Prog.Terminal.SubUserEditMode == null)
                        new User(UserType.AgentSubUser, Prog.Terminal.User.Company, Prog.Terminal.RequestCache).DBSave();
                    else
                    {
                        Prog.Terminal.SubUserEditMode.Serialize((DynamicBag)Prog.Terminal.RequestCache).DBSave();
                        Prog.Terminal.User.Company.SubUsers = null;
                    }
                    return this.RedirectToAction("APUsers");
                case 103102:
                    List<BookingInfo> bCol = DBBooking.Selects(Prog.Terminal.User.Company.Id.Value,
                        ((string)Prog.Terminal.RequestCache.fromDate).DateTimeOrDefault(),
                        ((string)Prog.Terminal.RequestCache.toDate).DateTimeOrDefault(),
                        (string)Prog.Terminal.RequestCache.country,
                        (string)Prog.Terminal.RequestCache.city,
                        (string)Prog.Terminal.RequestCache.bRefNo,
                        (string)Prog.Terminal.RequestCache.hotelName,
                        (string)Prog.Terminal.RequestCache.firstName,
                        (string)Prog.Terminal.RequestCache.lastName,
                        (int)Prog.Terminal.RequestCache.bStatus)
                        .Select(a => a.AsBookingInfo()).ToList();
                    this.TempData["bCol"] = bCol;
                    Misc.Log(bCol.Count.ToString());
                    return this.RedirectToAction("APBookingHistory");
                case 10310201:
                    if (BookingInfo.GetBookingInfo((long)Prog.Terminal.RequestCache.bId, true).Perform((int)Prog.Terminal.RequestCache.actionId))
                        return this.RedirectToAction("APBookingHistory");
                    return this.RedirectToAction("APBookingHistory");
                case 103103:
                    Prog.Terminal.User.AssertAction(ActionPermission.AMiscAddSubuser);
                    Prog.Terminal.SubUserEditMode = new User(UserType.AgentSubUser, Prog.Terminal.User.Company, null);
                    return this.RedirectToAction("APUser");
                case 103104:
                    return this.RedirectToAction("APUsers");
                case 10310401:
                    Prog.Terminal.User.AssertAction(ActionPermission.AMiscEditSubuser);
                    Prog.Terminal.SubUserEditMode = Prog.Terminal.User.Company.GetSubuser((long)Prog.Terminal.RequestCache.userId);
                    return this.RedirectToAction("APUser");
                case 103105:
                    Prog.Terminal.User.AssertAction(ActionPermission.ABookingView);
                    return this.RedirectToAction("APBookingHistory");
                case 103106:
                    Prog.Terminal.User.AssertAction(ActionPermission.AAccountingViewBalance);
                    return this.RedirectToAction("APBalance");
                case 104101:
                    //Prog.SessionCache.BookingParameters = new DynamicBag(Misc.requestJsonString, true);
                    //TODO: maybe Reset isn't required here
                    Prog.Terminal.Reset(Prog.Terminal.User);
                    Prog.Terminal.BasicBooking.UpdateParameters();
                    Prog.Terminal.BasicBooking.Search();
                    Prog.Terminal.BasicBooking.FilterAndSort(null, null, null, null);
                    return this.RedirectToAction("HOHotels");
                case 10410101:
                    return this.FWLinkInternal(104101);
                case 10410102:
                    Prog.Terminal.Reset(Prog.Terminal.User);
                    Prog.Terminal.BasicBooking.UpdateParameters();
                    Prog.Terminal.BasicBooking.LoadHotels();
                    return this.RedirectToAction("HOHotels");
                case 10410201:
                    if ((bool)Prog.Terminal.RequestCache.doSearch)
                    {
                        Prog.Terminal.BasicBooking.Search();
                        Prog.Terminal.BasicBooking.FilterAndSort(null, null, null, null);
                    }
                    else
                    {
                        Prog.Terminal.BasicBooking.FilterAndSort((string)Prog.Terminal.RequestCache.filterBy, (string)Prog.Terminal.RequestCache.sortBy, (int?)Prog.Terminal.RequestCache.fromPrice, (int?)Prog.Terminal.RequestCache.toPrice);
                    }
                    return this.PartialView("parHotels");
                case 104103:
                    //Prog.SessionCache.BookingParameters.updateJObject(Misc.requestJsonString);
                    Prog.Terminal.MoveToFinal();
                    Prog.Terminal.FinalBooking.UpdateParameters();
                    return this.RedirectToAction("HOHotel");
                case 10410301:
                    Prog.Terminal.FinalBooking.UpdateParameters();
                    Prog.Terminal.FinalBooking.SearchSelectedHotel();
                    return this.RedirectToAction("HOHotel");
                //return this.FWLinkInternal(104103);
                case 10410302:
                    Prog.Terminal.FinalBooking.UpdateParameters();
                    return this.RedirectToAction("HODetails");
                case 104104:
                    Prog.Terminal.FinalBooking.UpdateParameters();
                    Prog.Terminal.EndBooking();
                    if (Prog.Terminal.BookingInfo.ProceedByBalance())
                        return this.RedirectToAction("HOLast");
                    return this.View("HODetails");
                case 10410501:
                    //Prog.Terminal.FinalBooking.ExportToPDF(this.RenderRazorViewToString("ReportUn", null), true, false);
                    return this.RedirectToAction("HOLast");
                case 10410502:
                    if ((string)Prog.Terminal.RequestCache.type == "pReport")
                        Prog.Terminal.BookingInfo.ExportToPDF(this.RenderRazorViewToString("Report001001", null), true, true);
                    else if ((string)Prog.Terminal.RequestCache.type == "pReportP")
                        Prog.Terminal.BookingInfo.ExportToPDF(this.RenderRazorViewToString("Report001002", null), false, true);
                    else if ((string)Prog.Terminal.RequestCache.type == "pReportPC")
                        Prog.Terminal.BookingInfo.ExportToPDF(this.RenderRazorViewToString("Report001003", null), false, true);
                    return this.RedirectToAction("HOLast");
                case 105101:
                    return new ContentResult() { Content = Misc.GetMatch(Prog.Content.HotelsNames, (string)Prog.Terminal.RequestCache.ddlQuery).ToString() };
                case 105102:
                    return new ContentResult() { Content = Misc.GetMatch(Prog.Content.Nationalities, (string)Prog.Terminal.RequestCache.ddlQuery).ToString() };
                default:
                    return this.RedirectToAction("Index");
            }
        }

        public ActionResult Index()
        {
            if (Prog.Terminal.IsAuthenticated)
            {
                //TODO:
                if (Prog.Terminal.FinalBooking?.HasDone == true)
                    Prog.Terminal.Reset(Prog.Terminal.User);
                return this.View("Index");
            }
            else
                return this.RedirectToAction("ASSignIn");
        }

        public ActionResult Mila()
        {
            Prog.Terminal.SignIn("mila@mail.com", "0000");
            return this.RedirectToAction("Index");
        }

        public ActionResult IPComingSoon()
        {
            return this.View();
        }

        public ActionResult IPNotFound()
        {
            return this.View();
        }

        public ActionResult IPAbout()
        {
            return this.View();
        }

        public ActionResult IPContact()
        {
            return this.View();
        }

        public ActionResult IPMission()
        {
            return this.View();
        }

        public ActionResult IPObjectives()
        {
            return this.View();
        }

        public ActionResult IPKeysToSuccess()
        {
            return this.View();
        }

        public ActionResult IPInfo()
        {
            return this.View();
        }

        public ActionResult ASSignUp()
        {
            return this.View();
        }

        public ActionResult ASSignIn()
        {
            return this.View();
        }

        public ActionResult ASProfileHome()
        {
            return this.View();
        }

        public ActionResult SAAgents()
        {
            return this.View();
        }

        public ActionResult SAAgent()
        {
            return this.View();
        }

        public ActionResult SAXmls()
        {
            return this.View();
        }

        public ActionResult APBookingHistory()
        {
            Prog.Terminal.User.AssertAction(ActionPermission.ABookingView);
            return this.View();
        }

        public ActionResult APUsers()
        {
            return this.View();
        }

        public ActionResult APBalance()
        {
            Prog.Terminal.User.AssertAction(ActionPermission.AAccountingViewBalance);
            return this.View();
        }

        public ActionResult APUser()
        {
            return this.View();
        }

        public ActionResult HOHotels()
        {
            return this.View();
        }

        public ActionResult HOHotel()
        {
            return this.View();
        }

        public ActionResult HODetails()
        {
            return this.View();
        }

        public ActionResult HOLast()
        {
            return this.View();
        }

        public string RenderRazorViewToString(string viewName, object model)
        {
            // ViewData.Model = model;
            using (var sw = new StringWriter())
            {
                var viewResult = ViewEngines.Engines.FindPartialView(ControllerContext,
                                                                         viewName);
                var viewContext = new ViewContext(ControllerContext, viewResult.View,
                                             this.ViewData, this.TempData, sw);
                viewResult.View.Render(viewContext, sw);
                viewResult.ViewEngine.ReleaseView(ControllerContext, viewResult.View);
                return sw.GetStringBuilder().ToString();
            }
        }
    }

    public class VaController : AsyncController
    {
        public ActionResult GetActiveSessions()
        {
            List<SessionStateItemCollection> sCol = new List<SessionStateItemCollection>();

            object obj = typeof(HttpRuntime).GetProperty("CacheInternal", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            object[] obj2 = (object[])obj.GetType().GetField("_caches", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

            for (int i = 0; i < obj2.Length; i++)
            {
                Hashtable c2 = (Hashtable)obj2[i].GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj2[i]);
                foreach (DictionaryEntry entry in c2)
                {
                    object o1 = entry.Value.GetType().GetProperty("Value", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry.Value, null);
                    if (o1.GetType().ToString() == "System.Web.SessionState.InProcSessionState")
                    {
                        SessionStateItemCollection sess = (SessionStateItemCollection)o1.GetType().GetField("_sessionItems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o1);
                        if (sess != null)
                        {
                            sCol.Add(sess);
                        }
                    }
                }
            }

            return new ContentResult() { Content = sCol.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(a.ToString())).ToString() };
        }

        public ActionResult Extract()
        {
            List<string> lCol = new List<string>();
            StringBuilder sb = new StringBuilder();
            string[] lines = System.IO.File.ReadAllLines("/files/Basic.csv");

            foreach (string line in lines)
            {
                string[] values = line.Split('|');
                string cityId = values[2].Trim('"');
                if (cityId == "563" || cityId == "889" || cityId == "72" || cityId == "329")
                    lCol.Add(values[5]);
                // sb.AppendLine(values[5]);
            }
            System.IO.File.WriteAllLines("/files/Test.txt", lCol.ToArray());
            return new ContentResult() { Content = "Done." };
        }

        public ActionResult Extract2()
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();
            Dictionary<string, string> dic2 = new Dictionary<string, string>();
            List<string> lCol = new List<string>();
            StringBuilder sb = new StringBuilder();
            string[] first = System.IO.File.ReadAllLines("/files/admin2Codes.txt");
            string[] second = System.IO.File.ReadAllLines("/files/admin1CodesASCII.");

            foreach (string line in second)
            {
                string[] values = line.Split('\t');
                string cityId = values[0];
                string cityName = values[2];

                dic.Add(cityId, cityName);
            }

            foreach (string line in first)
            {
                string[] values = line.Split('\t');
                string cityId = values[0].Substring(0, values[0].LastIndexOf('.'));
                string distName = values[2];

                if (dic.ContainsKey(cityId))
                    lCol.Add(string.Format("{0}|{1}", dic[cityId], distName));
            }

            foreach (KeyValuePair<string, string> item in dic2)
                lCol.Add(string.Format("{0}|{1}", item.Key, item.Value));

            System.IO.File.WriteAllLines("/files/dist.txt", lCol.ToArray());
            return new ContentResult() { Content = "Done." };
        }

        public ActionResult Switch(int id)
        {
            string content = "Unspecified";

            if (id == 201)
                content = Prog.Terminal.BasicBooking.BaseHotels.SelectMany(a => a.Rooms).Where(a => !(a.Facilities == null))
                    .SelectMany(a => a.Facilities).Select(a => a.Name).Distinct(StringComparer.InvariantCulture)
                    .Aggregate(new StringBuilder(), (sb, f) => sb.AppendLine(f)).ToString();
            else if (id == 202)
            {
                string[] cities = { "amman", "beirut", "cairo", "dubai", "istanbul", "muscat" };
                string[] goGlobal = System.IO.File.ReadAllLines("/files/Basic.csv");

                List<DBHotel> dbHotels = new List<DBHotel>();
                foreach (string city in cities)
                {
                    using (StreamReader sr = System.IO.File.OpenText(Misc.GetFilePath($"content/hotelBeds/hotels_{city}.json")))
                    using (JsonTextReader jReader = new JsonTextReader(sr))
                    {
                        JArray jHotels = JToken.ReadFrom(jReader).Value<JArray>("hotels");
                        foreach (JToken jHotel in jHotels)
                            dbHotels.Add(new DBHotel()
                            {
                                Code = jHotel.Value<int>("code"),
                                Name = jHotel.Value<JToken>("name").Value<string>("content").ToLower(),
                                City = city // jHotel.Value<JToken>("city").Value<string>("content").ToLower()
                            });
                    }
                }
            }
            else if (id == 301)
            {
                new XmlUtilities().Merge();
            }
            else if (id == 60)
                content = Prog.Current.Terminals.Aggregate(new StringBuilder(), (sb, t) => sb.AppendLine($"{t.Info}")).ToString();
            else if (id == 61)
                Misc.FlushLog();
            else if (id == 99)
                content = Prog.Log.Aggregate(new StringBuilder().AppendLine($"Log.Count: {Prog.Log.Count}"), (sb, i) => sb.AppendLine(i)).ToString();
            return new ContentResult() { Content = content };
        }
    }
}