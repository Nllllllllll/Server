using Newtonsoft.Json;
using System;
using System.Threading.Channels;

namespace PersistenceServer.RPCs
{
    public class Disconnected : BaseRpc
    {
        public Disconnected()
        {
            RpcType = RpcType.RpcDisconnected; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
#if DEBUG
            Console.WriteLine($"(thread {Environment.CurrentManagedThreadId}) Client déconnecté");
#endif
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(connection));
        }

        private void ProcessMessage(UserConnection conn)
        {
            var player = Server!.GameLogic.GetPlayerByConnection(conn);

            if (player != null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} est devenu hors ligne.");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Utilisateur déconnecté. ID de session : {conn.Id}");
                Server!.GameLogic.UserDisconnected(conn);
                return;
            }

            var guild = Server!.GameLogic.GetPlayerGuild(conn);
            if (guild != null && player != null)
            {
                // envoyer un message aux autres membres de la guilde en ligne pour leur indiquer que celui-ci vient de se déconnecter
                // pour le client, les paramètres sont : l'ID du personnage, le rang dans la guilde, le statut en ligne (booléen)
                byte[] msgToGuildies = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(player.CharId), ToBytes((int)player.GuildRank!), ToBytes(false)); // true for online
                foreach (var onlineMember in guild.GetOnlineMembers())
                {
                    // si c'est notre propre personnage, inutile de lui dire qu'il vient de se déconnecter
                    if (onlineMember.Conn == conn) continue;
                    onlineMember.Conn.Send(msgToGuildies);
                }
            }

            var charId = player!.CharId;
            var party = player.PartyRef;

            Server!.GameLogic.UserDisconnected(conn);
            
            if (party != null)
            {
                Server!.GameLogic.StoreDisconnectedPlayerPartyId(charId, party.Id);

                // si le groupe a encore des membres en ligne, envoyer une nouvelle info complète du groupe aux joueurs restants
                if (party.HasOnlineMembers())
                {
                    party.SendFullPartyToInvolvedServers();
                }
                // si le groupe n'a plus aucun membre actuellement en ligne, dissoudre le groupe
                else
                {
                    // cela enverra aussi l'info aux serveurs
                    // party.DisbandParty();
                }
            }
        }
    }
}