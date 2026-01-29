using System.Net;
using System;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace PersistenceServer.RPCs
{
    public class LoginWithSteam : BaseRpc
    {
        public LoginWithSteam()
        {
            RpcType = RpcType.RpcLoginSteam; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string steamId = reader.ReadMmoString();
            string authTicket = reader.ReadMmoString();
#if DEBUG
            Console.WriteLine($"(thread {Environment.CurrentManagedThreadId}): Connectez-vous avec Steam");
#endif            
            Server!.Processor.ConQ.Enqueue(async () => await ProcessLogin(steamId, authTicket, connection));
        }

        private async Task ProcessLogin(string steamId, string authTicket, UserConnection connection)
        {
            // Interrogation de AuthenticateUserTicket: https://partner.steamgames.com/doc/webapi/ISteamUserAuth            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri("https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/");

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(
                    string.Format("?format=json&key={0}&appid={1}&ticket={2}",
                    Server!.Settings.SteamWebApiKey,
                    Server!.Settings.SteamAppId,
                    authTicket
                    ));
            }
            catch (Exception)
            {
                // l'exception la plus probable est un dépassement de délai (timeout)
                Console.WriteLine("Échec de la connexion Steam : délai d'expiration de l'API Steam");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("Délai d'expiration de l'API Steam"));
                connection.Send(msg);
                return;
            }

            var responseString = await response.Content.ReadAsStringAsync();            
            JObject jObject = JObject.Parse(responseString);

            if (!jObject.ContainsKey("response"))
            {
                Console.WriteLine("Échec de la connexion Steam : réponse inattendue");
                Console.WriteLine(responseString);
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("API Steam : réponse inattendue"));
                connection.Send(msg);
                return;
            }

            if (jObject["response"]!["error"] != null)
            {
                Console.WriteLine("Échec de la connexion Steam : erreur");
                Console.WriteLine($"Code: {jObject["response"]!["error"]!["errorcode"]}, error: {jObject["response"]!["error"]!["errordesc"]}");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString($"Échec de la connexion Steam: {jObject["response"]!["error"]!["errordesc"]}"));
                connection.Send(msg);
                return;
            }

            if (jObject["response"]!["params"] == null)
            {
                Console.WriteLine("Échec de la connexion Steam : réponse inattendue");
                Console.WriteLine(responseString);
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("API Steam : réponse inattendue"));
                connection.Send(msg);
                return;
            }
            
            var jParam = jObject!["response"]!["params"]!;

            /* Nous avons passé toutes les vérifications d'erreur et disposons désormais des champs suivants :

            string jParam["result"] qui devrait toujours valoir "OK"
            string jParam["steamid"]
            string jParam["ownersteamid"]            
            bool jParam["vacbanned"]
            bool jParam["publisherbanned"]

            Je suppose que steamid et ownersteamid peuvent différer si un membre de la famille joue au jeu sans en être le propriétaire.
            À vous de décider comment gérer ce cas, mais pour l'instant je l'autorise. Si vous souhaitez l'interdire, décommentez un bloc de code ~25 lignes plus bas.

            En revanche, j'interdirai l'accès si publisherbanned est true.
            */

            if (jParam["result"]!.ToString() != "OK" || jParam["steamid"]!.ToString() != steamId)
            {
                Console.WriteLine($"Échec de la connexion Steam pour l'identifiant Steam fourni: {steamId}");
                Console.WriteLine(responseString);
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("API Steam : connexion rejetée"));
                connection.Send(msg);
                return;
            }

            if (jParam["publisherbanned"]!.ToObject<bool>())
            {
                Console.WriteLine($"Échec de la connexion Steam : éditeur banni");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("API Steam : vous êtes banni par l'éditeur"));
                connection.Send(msg);
                return;
            }

            /* Décommente le bloc suivant si tu souhaites interdire la connexion aux membres de la famille qui ne possèdent pas eux-mêmes le jeu */
            //if (jParam["steamid"]!.ToString() != jParam["ownersteamid"]!.ToString())
            //{
            //    Console.WriteLine($"Échec de la connexion Steam : le steamid n'est pas celui d'un propriétaire direct du jeu");
            //    byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("Échec de la connexion : vous devez posséder le jeu"));
            //    connection.Send(msg);
            //    return;
            //}            

            // récupère l'identifiant utilisateur à partir du steamid dans la base de données, ou crée un compte Steam s'il n'existe pas
            // retourne -1 si la connexion a échoué en raison d'un statut de compte invalide        
            int accountId = await Server!.Database.LoginSteamUser(steamId);
            if (accountId >= 0)
            {
                Console.WriteLine($"Connexion Steam réussie pour Steamid: {steamId}, userid: {accountId}");
                var cookie = BCrypt.Net.BCrypt.GenerateSalt();
                Server!.GameLogic.UserLoggedIn(accountId, cookie, connection);

                // envoi de true pour signifier « succès », ainsi qu'un cookie  
                // ce cookie permettra une reconnexion ultérieure lorsque l'utilisateur changera de niveau pour entrer sur le serveur de jeu
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(true), WriteMmoString(cookie));
                connection.Send(msg);
            }
            // si le compte est banni (status == -1)
            else
            {
                Console.WriteLine($"Connexion Steam pour '{steamId}' échec : le compte est banni");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginSteam), ToBytes(false), WriteMmoString("Échec de la connexion : banni temporairement"));
                connection.Send(msg);
            }
        }
    }
}