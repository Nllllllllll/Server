using Microsoft.AspNetCore.Hosting.Server;
using Newtonsoft.Json.Linq;
using System.Net;

namespace PersistenceServer.RPCs
{
    public class GetIpAndPort : BaseRpc
    {
        public GetIpAndPort()
        {
            RpcType = RpcType.RpcGetIpAndPort; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            var charId = reader.ReadInt32();
            var cookie = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetIpAndPort(charId, cookie, connection));
        }

        private async Task ProcessGetIpAndPort(int charId, string cookie, UserConnection connection)
        {
            var accountId = Server!.GameLogic.GetAccountIdByCookie(cookie);
            if (accountId < 0)
            {
                Console.WriteLine("GetIpAndPort a échoué : l'utilisateur a fourni un mauvais cookie !");
                Console.WriteLine("Cookie envoyé par le client : " + cookie);
                // envoyer un message pour déconnecter le joueur vers le menu principal
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false), // signifie que c'est un échec
                    WriteMmoString("")
                ));
                return;
            }

            // Récupère la dernière zone dans laquelle le personnage a été sauvegardé et trouve une instance de serveur avec cette zone
            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo == null)
            {
                Console.WriteLine("GetIpAndPort a échoué : l'utilisateur a fourni un identifiant de caractère incorrect !");
                // envoyer un message pour déconnecter le joueur vers le menu principal
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false), // signifie que c'est un échec
                    WriteMmoString("")
                ));
                return;
            }

            string zone = "";
            JObject jsonObject = JObject.Parse(charInfo.SerializedCharacter);

            // S'il s'agit d'un personnage nouvellement créé, il aura un champ NewCharacter de type booléen avec pour valeur true
            if (jsonObject.TryGetValue("NewCharacter", out JToken? value) && value.Type == JTokenType.Boolean && value.ToObject<bool>() == true)
            {
                //@TODO: Définir vers quelle zone se connecter si c'est un personnage nouvellement créé.
                //@TODO: Existe-t-il une zone spécifique pour les personnages nouvellement créés ? Est-elle toujours la même ou dépend-elle de l'espèce/de l'origine/etc. ?
                // pour l'instant, ne rien faire, laisser la zone vide
            }

            // S'il ne s'agit pas d'un personnage nouvellement créé, mais d'un personnage sauvegardé précédemment, nous pouvons récupérer le champ "zone" du personnage depuis son JSON
            if (jsonObject.TryGetValue("Zone", out JToken? zoneValue) && zoneValue.Type == JTokenType.String)
            {
                zone = zoneValue.ToString();
            }

            // En fonction de la zone, on peut vouloir connecter le joueur à une certaine adresse IP et un port précis  
            // Si aucun serveur avec cette zone n'existe, ou s'il existe mais est plein, nous devons lancer une nouvelle instance
            var serverInstance = await Server!.GameLogic.GetOrStartServerForZone(zone);

#if DEBUG
            Console.WriteLine($"Retour de l'adresse IP et du port du serveur de jeu (127.0.0.1:{serverInstance.Port} -- (en raison de la configuration de débogage) pour l'ID de caractère: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true), // signifie que c'est un succès
                WriteMmoString($"127.0.0.1:{serverInstance.Port}")
                );
            connection.Send(msg);
#else
            Console.WriteLine($"Retour de l'adresse IP et du port du serveur de jeu ({serverInstance.Conn.Ip}:{serverInstance.Port}) pour l'ID de caractère: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true), // signifie que c'est un succès
                WriteMmoString($"{serverInstance.Conn.Ip}:{serverInstance.Port}")
                );
            connection.Send(msg);
#endif
        }
    }
}