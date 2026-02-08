using Microsoft.AspNetCore.Hosting.Server;
using Newtonsoft.Json.Linq;
using System.Net;

namespace PersistenceServer.RPCs
{
    public class GetIpAndPort : BaseRpc
    {
        public GetIpAndPort()
        {
            RpcType = RpcType.RpcGetIpAndPort;
        }

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
                Console.WriteLine("GetIpAndPort a échoué : l'utilisateur a fourni un mauvais cookie !");
                Console.WriteLine("Cookie envoyé par le client : " + cookie);
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false),
                    WriteMmoString("")
                ));
                return;
            }

            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo == null)
            {
                Console.WriteLine("GetIpAndPort a échoué : l'utilisateur a fourni un identifiant de caractère incorrect !");
                connection.Send(MergeByteArrays(
                    ToBytes(RpcType.RpcGetIpAndPort),
                    ToBytes(false),
                    WriteMmoString("")
                ));
                return;
            }

            // Utiliser directement charInfo.Zone au lieu de parser le JSON
            string zone = charInfo.IsNewCharacter ? "" : charInfo.Zone;

            //@TODO: Définir vers quelle zone se connecter si c'est un personnage nouvellement créé.
            //@TODO: Existe-t-il une zone spécifique pour les personnages nouvellement créés ? Est-elle toujours la même ou dépend-elle de l'espèce/de l'origine/etc. ?

            // En fonction de la zone, on peut vouloir connecter le joueur à une certaine adresse IP et un port précis  
            // Si aucun serveur avec cette zone n'existe, ou s'il existe mais est plein, nous devons lancer une nouvelle instance
            var serverInstance = await Server!.GameLogic.GetOrStartServerForZone(zone);

#if DEBUG
            Console.WriteLine($"Retour de l'adresse IP et du port du serveur de jeu (127.0.0.1:{serverInstance.Port} -- (en raison de la configuration de débogage) pour l'ID de caractère: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true),
                WriteMmoString($"127.0.0.1:{serverInstance.Port}")
            );
            connection.Send(msg);
#else
            Console.WriteLine($"Retour de l'adresse IP et du port du serveur de jeu ({serverInstance.Conn.Ip}:{serverInstance.Port}) pour l'ID de caractère: {charId}");
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetIpAndPort), 
                ToBytes(true),
                WriteMmoString($"{serverInstance.Conn.Ip}:{serverInstance.Port}")
            );
            connection.Send(msg);
#endif
        }
    }
}
