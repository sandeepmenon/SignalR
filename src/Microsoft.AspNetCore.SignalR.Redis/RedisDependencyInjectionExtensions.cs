// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Redis;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisDependencyInjectionExtensions
    {
        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder)
        {
            return AddRedis(builder, o => { });
        }

        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder, string redisConnectionString)
        {
            return AddRedis(builder, o =>
            {
                o.Options = ConfigurationOptions.Parse(redisConnectionString);
            });
        }

        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder, Action<RedisOptions> configure)
        {
            builder.Services.Configure(configure);
            builder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(RedisHubLifetimeManager<>));
            return builder;
        }
    }
}
