using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(MSite.Startup))]
namespace MSite
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
