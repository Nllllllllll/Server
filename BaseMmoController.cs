using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PersistenceServer.Controllers;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistenceServer
{
    public class BaseMmoController : ControllerBase
    {
        protected Role GetRole()
        {
            return (Role)HttpContext.Items["Role"]!;
        }

        protected Player? GetPlayer()
        {
            string clientCookie = (string)HttpContext.Items["Cookie"]!;
            var charId = (int)HttpContext.Items["CharId"]!;

            // In PIE, this is a special case
            // Because login works via a universal cookie, the PS doesn't keep a map <cookie, account>, so in Debug the PS will
            // trust the Account ID you provide instead of retrieving it from the cookie. But we still verify the validity of the universal cookie.
            int accountId = -1;
#if DEBUG
            if (clientCookie == MmoWsServer.Singleton!.Settings.UniversalCookie && HttpContext.Items.ContainsKey("Account"))
                accountId = (int)HttpContext.Items["Account"]!;         
            else
                accountId = MmoWsServer.Singleton!.GameLogic.GetAccountIdByCookie(clientCookie);
#else
            accountId = MmoWsServer.Singleton!.GameLogic.GetAccountIdByCookie(clientCookie);
#endif
            Player? player = MmoWsServer.Singleton!.GameLogic.GetPlayerById(charId);

            // On récupère l'ID du compte depuis le cookie, qui ne peut pas être deviné par d'autres joueurs
            // Ensuite on récupère le personnage par l'ID de personnage que le client a fourni (qui peut être falsifié)
            // Si le personnage récupéré appartient au même compte auquel appartient le cookie, la vérification de validité est considérée comme réussie et on retourne le Player
            if (player != null && player.AccountId == accountId)
                return player;
            else
                return null;
        }

        protected GameServer? GetGameServer()
        {
            string serverGuid = (string)HttpContext.Items["ServerGuid"]!;
            return MmoWsServer.Singleton!.GameLogic.GetServerByGuid(serverGuid);
        }

        protected async Task<dynamic?> GetBodyJsonAsync()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            return JsonConvert.DeserializeObject<ExpandoObject>(body);
        }
    }

    public static class DynamicHelper
    {
        public static string ToString(object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }
    }
}
