﻿using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace StateMachineDemoShared
{
    public class WholeAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new(ClaimTypes.Name, "Admin"),
                new(ClaimTypes.Role, "Admin"),
            ], "Admin");
            var user = new ClaimsPrincipal(identity);
            return Task.FromResult(new AuthenticationState(user));
        }
    }
}