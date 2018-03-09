// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public static class SignalRAppBuilderExtensions
    {
        public static IApplicationBuilder UseSignalR(this IApplicationBuilder app, Action<HubRouteBuilder> configure)
        {
            var marker = app.ApplicationServices.GetService(typeof(SignalRMarkerService));
            if (marker == null)
            {
                throw new InvalidOperationException("The SignalR service needs to be registered");
            }

            app.UseSockets(routes =>
            {
                configure(new HubRouteBuilder(routes));
            });

            return app;
        }
    }
}
