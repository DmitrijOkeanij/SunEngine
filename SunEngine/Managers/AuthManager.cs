using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using Flurl;
using LinqToDB;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SunEngine.Configuration.Options;
using SunEngine.Controllers;
using SunEngine.DataBase;
using SunEngine.Models;
using SunEngine.Security;
using SunEngine.Security.Cryptography;
using SunEngine.Services;
using SunEngine.Utils;

namespace SunEngine.Managers
{
    public interface IAuthManager
    {
        Task<UserServiceResult> LoginAsync(string nameOrEmail, string password);
        Task<ServiceResult> RegisterAsync(NewUserArgs model);
    }

    public class AuthManager : DbService, IAuthManager
    {
        protected readonly MyUserManager userManager;
        protected readonly GlobalOptions globalOptions;
        protected readonly IEmailSender emailSender;
        protected readonly ILogger logger;


        public AuthManager(
            MyUserManager userManager,
            IEmailSender emailSender,
            DataBaseConnection db,
            IOptions<GlobalOptions> globalOptions,
            ILoggerFactory loggerFactory) : base(db)
        {
            this.userManager = userManager;
            this.globalOptions = globalOptions.Value;
            this.emailSender = emailSender;
            logger = loggerFactory.CreateLogger<AccountController>();
        }
        

        public async Task<UserServiceResult> LoginAsync(string nameOrEmail, string password)
        {
            User user = await userManager.FindUserByNameOrEmailAsync(nameOrEmail);

            if (user == null || !await userManager.CheckPasswordAsync(user, password))
            {
                return UserServiceResult.BadResult(new ErrorViewModel
                {
                    ErrorName = "username_password_invalid",
                    ErrorText = "The username or password is invalid."
                });
            }

            if (!await userManager.IsEmailConfirmedAsync(user))
            {
                return UserServiceResult.BadResult(new ErrorViewModel
                {
                    ErrorName = "email_not_confirmed",
                    ErrorText = "You must have a confirmed email to log in."
                });
            }

            if (await userManager.IsUserInRoleAsync(user.Id, RoleNames.Banned))
            {
                return UserServiceResult.BadResult(new ErrorViewModel
                {
                    ErrorName = "user_banned",
                    ErrorText = "Error" // Что бы не провоцировать пользователя словами что он забанен
                });
            }

            return UserServiceResult.OkResult(user);
        }


        public virtual async Task<ServiceResult> RegisterAsync(NewUserArgs model)
        {
            var user = new User
            {
                UserName = model.UserName,
                Email = model.Email,
                Avatar = User.DefaultAvatar,
                Photo = User.DefaultAvatar
            };

            using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                IdentityResult result = await userManager.CreateAsync(user, model.Password);
                await db.Users.Where(x => x.Id == user.Id).Set(x => x.Link, x => x.Id.ToString()).UpdateAsync();

                if (!result.Succeeded)
                    return new ServiceResult
                    {
                        Succeeded = false,
                        Error = new ErrorViewModel
                        {
                            ErrorsNames = result.Errors.Select(x => x.Code).ToArray(),
                            ErrorsTexts = result.Errors.Select(x => x.Description).ToArray()
                        }
                    };

                logger.LogInformation($"New user registered (id: {user.Id})");

                if (!user.EmailConfirmed)
                {
                    // Send email confirmation email
                    var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

                    var emailConfirmUrl = globalOptions.SiteApi.AppendPathSegments("Auth", "ConfirmRegister")
                        .SetQueryParams(new {uid = user.Id, token = confirmToken});

                    try
                    {
                        await emailSender.SendEmailAsync(model.Email, "Please confirm your account",
                            $"Please confirm your account by clicking this <a href=\"{emailConfirmUrl}\">link</a>."
                        );
                    }
                    catch (Exception e)
                    {
                        return new ServiceResult
                        {
                            Succeeded = false,
                            Error = new ErrorViewModel
                            {
                                ErrorName = "Can not send email",
                                ErrorText = "Ошибка отправки email"
                            }
                        };
                    }


                    logger.LogInformation($"Sent email confirmation email (id: {user.Id})");
                }

                logger.LogInformation($"User logged in (id: {user.Id})");

                transaction.Complete();

                return new ServiceResult
                {
                    Succeeded = true
                };
            }
        }
    }
}