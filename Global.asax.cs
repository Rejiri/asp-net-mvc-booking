using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace MSite
{
    public class MvcApplication : System.Web.HttpApplication
    {
        private static readonly object _sync = new object();

        protected void Application_Start()
        {
            Prog prog = Prog.Current;
            Prog.Database.Initialize();

            Misc.Log("Application_Start");

            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            //this.Error += delegate (object sender, EventArgs e)
            //{
            //    Logger.Log(HttpContext.Current.Error.GetBaseException().ToString());
            //    HttpContext.Current.Response.Redirect("~/Home/IPNotFound");
            //};

            //this.BeginRequest += delegate (object sender, EventArgs e)
            //{
            //    Logger.Log(null, "BeginRequest");
            //};
        }

        protected void Application_Error()
        {
            Exception ex = HttpContext.Current.Error.GetBaseException();
            Misc.Log(ex.ToString());

            //Misc.FlushLog();

            if (ex is PermissionException)
            {
                Prog.Terminal.Message = Misc.PermissionRequired;
                HttpContext.Current.Response.Redirect("~/Home/IPInfo");
            }
            else
                HttpContext.Current.Response.Redirect("~/Home/IPNotFound");
        }

        protected void Session_Start(object sender, EventArgs e)
        {
            Misc.Log("Session, Session_Start");
        }

        protected void Session_End(object sender, EventArgs e)
        {
            //TODO, this call throws an exception as it aquired HttpCurrent after disposing.
            // Misc.Log("Session, Session_End");
        }
    }

    public class MSiteModule : IHttpModule
    {
        public void Init(HttpApplication context)
        {
            context.BeginRequest += delegate (object sender, EventArgs e)
            {
                Misc.Log("BeginRequest, {0}", context.Request.Path);
            };
        }

        public void Dispose()
        {
            return;
        }
    }

    public class MSiteFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);

            Misc.Log($"OnActionExecuting {filterContext.HttpContext.Request.UserHostAddress}\t{filterContext.HttpContext.Request.Path}");
            Misc.Log($"requestData: {new StreamReader(filterContext.HttpContext.Request.InputStream).ReadToEnd()}");
            Misc.Log($"jsonString 1: {filterContext.HttpContext.Request["jsonString"]}");
            Misc.Log("jsonString 2", Misc.requestJsonString);
            // Misc.Log("Request.Params: {0}", filterContext.HttpContext.Request.Params.ToString());

            Prog.Terminal.RequestCache = new DynamicBag(Misc.requestJsonString);
        }
    }
}