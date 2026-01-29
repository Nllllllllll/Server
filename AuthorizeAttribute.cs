using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistenceServer
{
    public enum Role
    {
        Client,
        Server,
        Anonymous
    }

    public class AuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly HashSet<Role> _allowedRoles;

        public AuthorizeAttribute(params Role[] allowedRoles)
        {
            _allowedRoles = new HashSet<Role>(allowedRoles);
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var Headers = context.HttpContext.Request.Headers;

            // si nous disposons d'en-têtes Serveur, vérifier que nous avons un rôle Serveur et valider le mot de passe du serveur
            if (Headers.TryGetValue("ServerPassword", out StringValues ServerPassword) && Headers.TryGetValue("ServerGuid", out StringValues Guid))
            {
                if (_allowedRoles.Contains(Role.Server))
                {
                    if (MmoWsServer.Singleton!.Settings.ServerPassword == ServerPassword)
                    {
                        context.HttpContext.Items["ServerGuid"] = Guid.ToString();
                        context.HttpContext.Items["Role"] = Role.Server;
                        return;
                    }
                }
                ForbidResult(context);
                return;
            }

            // si nous disposons d'en-têtes Client, vérifier que nous avons un rôle Client et valider les en-têtes
            if (Headers.TryGetValue("Cookie", out StringValues Cookie) && Headers.TryGetValue("CharId", out StringValues CharId))
            {
                if (_allowedRoles.Contains(Role.Client))
                {
                    context.HttpContext.Items["Cookie"] = Cookie.ToString();
                    if (Headers.TryGetValue("Account", out StringValues AccountId)) // facultatif, uniquement pour le mode PIE (Play-In-Editor) et le débogage
                    {
                        if (int.TryParse(AccountId.ToString(), out int accountId))
                            context.HttpContext.Items["Account"] = accountId;
                    }
                    if (int.TryParse(CharId.ToString(), out int charId))
                    {
                        context.HttpContext.Items["CharId"] = charId;
                        context.HttpContext.Items["Role"] = Role.Client;
                        return;
                    }
                }
                ForbidResult(context);
                return;
            }

            // si aucun en-tête Client ni Serveur n'est présent, mais que le rôle Anonyme est autorisé, accorder l'autorisation
            if (_allowedRoles.Contains(Role.Anonymous))
            {
                context.HttpContext.Items["Role"] = Role.Anonymous;
                return;
            }

            ForbidResult(context);
        }

        // Normalement, on pourrait simplement faire : `context.Result = new ForbidResult();`  
        // Mais comme UE5 ne peut pas actuellement lire le code de statut HTTP, on renvoie un corps JSON personnalisé contenant le code de statut
        void ForbidResult(AuthorizationFilterContext context)
        {
            context.Result = new ObjectResult(new { error = "Accès refusé : rôle non autorisé", status = 403 })
            {
                StatusCode = 403
            };
        }
    }
}
