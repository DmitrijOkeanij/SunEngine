﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SunEngine.Managers;
using SunEngine.Models.Materials;
using SunEngine.Presenters;
using SunEngine.Security.Authorization;
using SunEngine.Security.Filters;
using SunEngine.Stores;
using IAuthorizationService = SunEngine.Security.Authorization.IAuthorizationService;

namespace SunEngine.Controllers
{
    public class MessagesController : BaseController
    {
        protected readonly OperationKeysContainer OperationKeys;
        protected readonly MessageAuthorization messageAuthorization;
        protected readonly MaterialsManager materialsManager;
        protected readonly MessagesManager messagesManager;
        protected readonly IAuthorizationService authorizationService;
        protected readonly IMessagesPresenter messagesPresenter;
        
        public MessagesController(
            MaterialsManager materialsManager, 
            MessageAuthorization messageAuthorization,
            OperationKeysContainer operationKeys, 
            MessagesManager messagesManager,
            IAuthorizationService authorizationService,
            MyUserManager userManager,
            IMessagesPresenter messagesPresenter,
            IUserGroupStore userGroupStore) : base(userGroupStore, userManager)
        {
            OperationKeys = operationKeys;
            this.messageAuthorization = messageAuthorization;
            this.materialsManager = materialsManager;
            this.messagesManager = messagesManager;
            this.authorizationService = authorizationService;
            this.messagesPresenter = messagesPresenter;
        }

        [HttpPost]
        public virtual async Task<IActionResult> GetMaterialMessages(int materialId)
        {
            int? categoryId = await materialsManager.GetCategoryIdIfHasMaterialAsync(materialId);
            if (!messageAuthorization.HasAccessForGetMessages(User.UserGroups, categoryId.Value))
            {
                return Unauthorized();
            }

            var messages = await messagesPresenter.GetMaterialMessagesAsync(materialId);

            return Json(messages);
        }

        [HttpPost]
        [UserSpamProtectionFilter(TimeoutSeconds = 10)]
        public virtual async Task<IActionResult> Add(int materialId, string text)
        {
            Material material = await materialsManager.GetAsync(materialId);
            if (material == null)
            {
                return BadRequest();
            }

            if (!messageAuthorization.CanAdd(User.UserGroups,material.CategoryId))
            {
                return Unauthorized();
            }

            var now = DateTime.UtcNow;
            Message message = new Message
            {
                Material = material,
                MaterialId = materialId,
                PublishDate = now,
                EditDate = now,
                Text = text,
                AuthorId = User.UserId
            };

            await messagesManager.InsertAsync(message);

            return Ok();
        }

        [HttpPost]
        public virtual async Task<IActionResult> Get(int id)
        {
            (MessageViewModel messageViewModel,int categoryId)  = await messagesPresenter.GetMessageAsync(id);
            if (messageViewModel == null)
            {
                return BadRequest();
            }

            if (!authorizationService.HasAccess(User.UserGroups, categoryId,
                OperationKeys.MaterialAndMessagesRead))
            {
                return Unauthorized();
            }

            return Json(messageViewModel);
        }

        [HttpPost]
        public virtual async Task<IActionResult> Edit(Message newMessage)
        {
            (Message message,int categoryId) = await messagesManager.GetAsync(newMessage.Id);
            if (message == null)
            {
                return BadRequest();
            }

            if (! await messageAuthorization.CanEditAsync(User, message,categoryId))
            {
                return Unauthorized();
            }

            message.Text = newMessage.Text;
            message.EditDate = DateTime.UtcNow;

            await messagesManager.UpdateAsync(message);

            return Ok();
        }

        [HttpPost]
        public virtual async Task<IActionResult> MoveToTrash(int id)
        {
            (Message message,int categoryId) = await messagesManager.GetAsync(id);
            if (message == null)
            {
                return BadRequest();
            }

            if (! await messageAuthorization.CanMoveToTrashAsync(User, message, categoryId))
            {
                return Unauthorized();
            }
            
            await messagesManager.MoveToTrashAsync(message);
            
            return Ok();
        }

        /*[HttpPost]
        public async Task<IActionResult> Restore(int id)
        {
            Message message = await _messagesRepository.Query.Include(x => x.Material).FirstOrDefaultAsync(x => x.Id == id);
            if (message == null)
            {
                return BadRequest();
            }

            if (!_messageAuthorization.CanDelete(User, message, message.Material.CategoryId))
            {
                return Unauthorized();
            }

            _messagesRepository.RestoreFromTrash(message);

            await _messagesRepository.SaveChangesAsync();

            return Ok();
        }*/
    }
}