using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class HangfireExtensions
    {
        public static IEndpointRouteBuilder MapAuthorizedHangfireDashboard(
            this IEndpointRouteBuilder endpoints, 
            string url = "/.well-known/jobs",
            string policyName = "HangfirePolicyName")
        {

            endpoints.MapHangfireDashboard(url, new DashboardOptions()
            {
                Authorization = new List<IDashboardAuthorizationFilter> { }
            })
           .RequireAuthorization(policyName);


            return endpoints;
        }
        public static AuthorizationOptions AddHangfirePolicy(this AuthorizationOptions options,
            string policyName = "HangfirePolicyName",
            string schemaName = "eavfw",
            string role = "System Administrator")
        {
            options.AddPolicy(policyName, pb =>
            {
               
                    pb.AddAuthenticationSchemes(schemaName);
                    pb.RequireAuthenticatedUser();
                    pb.RequireClaim("role", role);
                
            });

            return options;
        }

        public static AuthorizationOptions AddHangfireAnonymousPolicy(this AuthorizationOptions options,
              string policyName = "HangfirePolicyName")
        {
            options.AddPolicy(policyName, pb =>
            {
                pb.RequireAssertion(c => true);
            });

            return options;
        }

    }
}
