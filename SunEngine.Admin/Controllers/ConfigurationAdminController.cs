using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SunEngine.Admin.Managers;
using SunEngine.Admin.Presenters;
using SunEngine.Core.Models;

namespace SunEngine.Admin.Controllers
{
    public class ConfigurationAdminController : BaseAdminController
    {
        protected readonly IConfigurationManager configurationManager;
        protected readonly IConfigurationPresenter configurationPresenter;
        protected readonly IConfigurationRoot configurationRoot;

        public ConfigurationAdminController(
            IConfigurationManager configurationManager,
            IConfigurationPresenter configurationPresenter,
            IConfigurationRoot configurationRoot,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            this.configurationRoot = configurationRoot;
            this.configurationManager = configurationManager;
            this.configurationPresenter = configurationPresenter;
        }

        [HttpPost]
        public IActionResult UploadConfiguration()
        {
            var items = Request.Form.Select(x => new ConfigurationItem
            {
                Name = x.Key,
                Value = x.Value
            }).ToList();

            configurationManager.UploadConfigurationItems(items);
            configurationRoot.Reload();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> LoadConfiguration()
        {
            var items = await configurationPresenter.LoadConfigurationAsync();

            return Ok(items);
        }

        [HttpPost]
        public IActionResult GetEnums()
        {
            var items = configurationPresenter.GetEnums();

            return Json(items);
        }
    }
}