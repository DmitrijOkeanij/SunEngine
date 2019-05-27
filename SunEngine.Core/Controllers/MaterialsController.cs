﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SunEngine.Core.Cache.Services;
using SunEngine.Core.DataBase;
using SunEngine.Core.Errors;
using SunEngine.Core.Filters;
using SunEngine.Core.Managers;
using SunEngine.Core.Models.Materials;
using SunEngine.Core.Presenters;
using SunEngine.Core.Security;
using SunEngine.Core.Services;

namespace SunEngine.Core.Controllers
{
    /// <summary>
    /// Materials CRUD controller.
    /// Used by Articles, Forum and Blog
    /// </summary>
    public class MaterialsController : BaseController
    {
        protected readonly MaterialsAuthorization materialsAuthorization;
        protected readonly ICategoriesCache categoriesCache;
        protected readonly IMaterialsManager materialsManager;
        protected readonly IMaterialsPresenter materialsPresenter;

        public MaterialsController(
            MaterialsAuthorization materialsAuthorization,
            ICategoriesCache categoriesCache,
            IMaterialsManager materialsManager,
            IMaterialsPresenter materialsPresenter,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            this.materialsAuthorization = materialsAuthorization;
            this.categoriesCache = categoriesCache;
            this.materialsManager = materialsManager;
            this.materialsPresenter = materialsPresenter;
        }

        [HttpPost]
        public virtual async Task<IActionResult> Get(string idOrName)
        {
            return int.TryParse(idOrName, out int id)
                ? await GetById(id)
                : await GetByName(idOrName);
        }

        [NonAction]
        protected virtual async Task<IActionResult> GetById(int id)
        {
            int? categoryId = await materialsManager.GetCategoryIdAsync(id);
            if (categoryId == null)
                return BadRequest();

            var category = categoriesCache.GetCategory(categoryId.Value);

            if (!materialsAuthorization.CanGet(User.Roles, category))
                return Unauthorized();


            var materialView = await materialsPresenter.GetAsync(id);

            return Json(materialView);
        }

        [NonAction]
        protected virtual async Task<IActionResult> GetByName(string name)
        {
            int? categoryId = await materialsManager.GetCategoryIdAsync(name);
            if (categoryId == null)
                return BadRequest();

            var category = categoriesCache.GetCategory(categoryId.Value);

            if (!materialsAuthorization.CanGet(User.Roles, category))
                return Unauthorized();

            var materialViewModel = await materialsPresenter.GetAsync(name);

            return Json(materialViewModel);
        }

        [HttpPost]
        [UserSpamProtectionFilter(TimeoutSeconds = 60)]
        public virtual async Task<IActionResult> Create(MaterialRequestModel materialData)
        {
            var category = categoriesCache.GetCategory(materialData.CategoryName);
            if (category == null)
                return BadRequest();

            if (!materialsAuthorization.CanCreate(User.Roles, category))
                return Unauthorized();

            var now = DateTime.UtcNow;

            var material = new Material
            {
                Title = materialData.Title,
                Text = materialData.text,
                PublishDate = now,
                LastActivity = now,
                CategoryId = category.Id,
                AuthorId = User.UserId
            };

            var result = await SetNameAsync(material, materialData.Name);
            if (result.Failed)
                return BadRequest(result.Error);

            bool isDescriptionEditable = category.IsDescriptionEditable();
            if (isDescriptionEditable)
                material.Description = materialData.Description;


            if (materialData.IsHidden && materialsAuthorization.CanHide(User.Roles, category))
                material.IsHidden = true;

            if (materialData.IsHidden && materialsAuthorization.CanBlockComments(User.Roles, category))
                material.IsCommentsBlocked = true;

            contentCache.InvalidateCache(category.Id);

            await materialsManager.CreateAsync(material, materialData.Tags, isDescriptionEditable);
            return Ok();
        }


        [HttpPost]
        public virtual async Task<IActionResult> Update(MaterialRequestModel materialData)
        {
            if (!ModelState.IsValid)
            {
                var ers = ModelState.Values.SelectMany(v => v.Errors);
                return BadRequest(string.Join(",\n ", ers.Select(x => x.ErrorMessage)));
            }

            Material material = await materialsManager.GetAsync(materialData.Id);
            if (material == null)
                return BadRequest();

            if (!await materialsAuthorization.CanUpdateAsync(User, material))
                return Unauthorized();

            var newCategory = categoriesCache.GetCategory(materialData.CategoryName);
            if (newCategory == null)
                return BadRequest();

            material.Title = materialData.Title;
            material.Text = materialData.text;
            material.EditDate = DateTime.UtcNow;

            var result = await SetNameAsync(material, materialData.Name);
            if (result.Failed)
                return BadRequest(result.Error);

            bool isDescriptionEditable = newCategory.IsDescriptionEditable();
            material.Description = isDescriptionEditable ? materialData.Description : null;

            if (material.IsHidden != materialData.IsHidden && materialsAuthorization.CanHide(User.Roles, newCategory))
                material.IsHidden = materialData.IsHidden;

            if (material.IsCommentsBlocked != materialData.IsCommentsBlocked &&
                materialsAuthorization.CanBlockComments(User.Roles, newCategory))
                material.IsCommentsBlocked = materialData.IsCommentsBlocked;

            // Если категория новая, то обновляем
            if (material.CategoryId != newCategory.Id
                && materialsAuthorization.CanMove(User, categoriesCache.GetCategory(material.CategoryId), newCategory))
            {
                material.CategoryId = newCategory.Id;
            }

            await materialsManager.UpdateAsync(material, materialData.Tags, isDescriptionEditable);
            return Ok();
        }

        [NonAction]
        protected async Task<ServiceResult> SetNameAsync(Material material, string name)
        {
            if (User.IsInRole(RoleNames.Admin))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    material.Name = null;
                }
                else
                {
                    if (!materialsManager.IsNameValid(name))
                        return ServiceResult.BadResult(new ErrorView("MaterialNameNotValid", "Invalid material name",
                            ErrorType.System));

                    if (name != material.Name && await materialsManager.IsNameInDbAsync(name))
                        return ServiceResult.BadResult(ErrorView.SoftError("MaterialNameAlreadyUsed",
                            "This material name is already used"));

                    material.Name = name;
                }
            }

            return ServiceResult.OkResult();
        }

        [HttpPost]
        public virtual async Task<IActionResult> Delete(int id)
        {
            Material material = await materialsManager.GetAsync(id);
            if (material == null)
                return BadRequest();

            if (!await materialsAuthorization.CanDeleteAsync(User, material))
                return Unauthorized();

            contentCache.InvalidateCache(material.CategoryId);

            await materialsManager.DeleteAsync(material);
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> Restore(int id)
        {
            Material material = await materialsManager.GetAsync(id);
            if (material == null)
                return BadRequest();

            if (!materialsAuthorization.CanRestoreAsync(User, material))
                return Unauthorized();

            await materialsManager.RestoreAsync(material);
            
            contentCache.InvalidateCache(material.CategoryId);

            return Ok();
        }
        
        /// <summary>
        /// Move material up in sort order inside category
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> Up(int id)
        {
            int? categoryId = await materialsManager.GetCategoryIdAsync(id);
            if (!categoryId.HasValue)
                return BadRequest();

            if (materialsAuthorization.CanChangeOrder(User.Roles, categoryId.Value))
                return Unauthorized();

            var result = await materialsManager.UpAsync(id);
            if (result.Failed)
                return BadRequest();

            contentCache.InvalidateCache(categoryId.Value);

            return Ok();
        }

        /// <summary>
        /// Move material down in sort order inside category
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> Down(int id)
        {
            int? categoryId = await materialsManager.GetCategoryIdAsync(id);
            if (!categoryId.HasValue)
                return BadRequest();

            if (materialsAuthorization.CanChangeOrder(User.Roles, categoryId.Value))
                return Unauthorized();

            var result = await materialsManager.DownAsync(id);
            if (result.Failed)
                return BadRequest();

            contentCache.InvalidateCache(categoryId.Value);

            return Ok();
        }
    }

    public class MaterialRequestModel
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public string CategoryName { get; set; }

        [Required]
        [MinLength(3)]
        [MaxLength(DbColumnSizes.Materials_Title)]
        public string Title { get; set; }

        [MaxLength(DbColumnSizes.Materials_Description)]
        public string Description { get; set; }

        [Required] public string text { get; set; }

        public string Tags { get; set; } = "";
        public DateTime? PublishDate { get; set; } = null;
        public int? AuthorId { get; set; } = null;

        public bool IsHidden { get; set; }
        public bool IsCommentsBlocked { get; set; }
    }
}
