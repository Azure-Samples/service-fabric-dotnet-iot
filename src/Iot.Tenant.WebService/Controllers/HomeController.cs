// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService.Controllers
{
    using System.Fabric;
    using System.Linq;
    using Microsoft.AspNetCore.Mvc;

    public class HomeController : Controller
    {
        private readonly StatelessServiceContext context;

        public HomeController(StatelessServiceContext context)
        {
            this.context = context;
        }

        public IActionResult Index()
        {
            this.ViewData["Tenant"] = this.context.ServiceName.AbsolutePath.Split('/').Last();
            return this.View();
        }

        public IActionResult About()
        {
            this.ViewData["Message"] = "Your application description page.";

            return this.View();
        }

        public IActionResult Contact()
        {
            this.ViewData["Message"] = "Your contact page.";

            return this.View();
        }

        public IActionResult Error()
        {
            return this.View();
        }
    }
}