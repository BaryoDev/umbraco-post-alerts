using System.Reflection;
using BaryoDev.Umbraco.PostAlerts.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NUnit.Framework;
using Umbraco.Cms.Web.Common.Authorization;

namespace BaryoDev.Umbraco.PostAlerts.IntegrationTests;

/// <summary>Pins the access rules of the endpoints, so a change to them shows up in review.</summary>
[TestFixture]
public class ApiApprovalTests
{
    [Test]
    public void The_management_api_requires_content_section_access()
    {
        var authorize = typeof(PostAlertsApiController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>().Single();

        Assert.That(authorize.Policy, Is.EqualTo(AuthorizationPolicies.SectionAccessContent));
    }

    [Test]
    public void Every_public_route_is_the_expected_set()
    {
        var routes = new List<string>();
        foreach (Type type in new[] { typeof(PostAlertsController), typeof(PushController) })
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (HttpMethodAttribute attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    routes.Add($"{attribute.HttpMethods.Single()} {attribute.Template}");
                }
            }
        }

        Assert.That(routes, Is.EquivalentTo(new[]
        {
            "POST subscribe",
            "GET confirm/{token}",
            "GET unsubscribe/{token}",
            "POST unsubscribe/{token}",
            "GET push/key",
            "POST push/subscription",
            "DELETE push/subscription",
            "GET sw.js",
        }));
    }

    [Test]
    public void Only_the_unsubscribe_post_skips_antiforgery()
    {
        MethodInfo[] skipping = typeof(PostAlertsController).GetMethods()
            .Where(m => m.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() is not null).ToArray();

        Assert.That(skipping.Select(m => m.Name), Is.EqualTo(new[] { nameof(PostAlertsController.Unsubscribe) }));
    }

    [Test]
    public void Push_writes_accept_only_json()
    {
        foreach (string name in new[] { nameof(PushController.Subscribe), nameof(PushController.Unsubscribe) })
        {
            Assert.That(typeof(PushController).GetMethod(name)!.GetCustomAttribute<ConsumesAttribute>(), Is.Not.Null, name);
        }
    }
}
