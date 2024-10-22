﻿using BlogEngine.DbAccess;
using BlogEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine;

public static class BlogSvcInitializer
{
    public static void Initialize(IServiceCollection services, string dbConnectionString)
    {
        services.AddTransient<IUserLoginRepository>(x => new UserLoginRepo(dbConnectionString));
        services.AddTransient<IBlogUserRepo>(x => new BlogUserRepo(dbConnectionString));
        services.AddTransient<AuthSvc>();
    }
}