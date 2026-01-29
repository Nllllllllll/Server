using System;
using System.Numerics;

namespace PersistenceServer.RPCs
{
    internal class AcceptInvite : BaseRpc
    {
        public AcceptInvite()
        {
            RpcType = RpcType.RpcAcceptInvite; // Définissez-le sur le RpcType que vous souhaitez intercepter

        }

        // Lire le message depuis le reader, puis mettre en file d’attente une Action dans la queue concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        // Consultez les autres RPC pour plus d’exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            bool accept = reader.ReadBoolean();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(accept, connection));
        }

        private async Task ProcessMessage(bool accept, UserConnection senderConn)
        {
            var invitedPlayer = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (invitedPlayer == null) return;
            // if timeout happened
            if (!invitedPlayer.HasPendingInvite())
            {
                byte responseByte = 3;
                byte[] msgExpired = MergeByteArrays(ToBytes(RpcType.RpcAcceptInvite), responseByte); // L’octet 3 de la réponse signifie "l’invitation a expiré"
                senderConn.Send(msgExpired);
                return; 
            }

            PendingInvitation invite = invitedPlayer.GetPendingInvite()!;

            var inviter = Server!.GameLogic.GetPlayerByName(invite.inviterName);
            if (inviter != null) // L’invitant a peut-être été déconnecté entre-temps
            {
                // C’est ici que nous informons l’invitant que le joueur a accepté ou refusé l’invitation
                byte[] msgToInviter = MergeByteArrays(ToBytes(RpcType.RpcAcceptInvite), ToBytes(accept), WriteMmoString(invitedPlayer.Name));
                inviter.Conn.Send(msgToInviter);
            }

            if (accept && invite is GuildInvitation guildInvite)
            {
                var guild = Server!.GameLogic.GetGuildById(guildInvite.guildId);

                // Si la guilde a été dissoute entre-temps, retourner simplement
                // Ou si le joueur n’est pas sans guilde, retourner (par exemple, a rejoint une autre guilde entre-temps en en créant une)
                if (guild == null || invitedPlayer.GuildId != -1)
                {
                    invitedPlayer.ClearPendingInvite();
                    return;
                }

                Console.WriteLine($"{DateTime.Now:HH:mm} {invitedPlayer.Name} rejoint la guilde {guild.Name}");

                int rank = Server.Settings.DefaultGuildRank;                
                await Server!.Database.AddGuildMember(guildInvite.guildId, invitedPlayer.CharId, rank);
                Server!.GameLogic.AddGuildMember(guild, invitedPlayer, rank);

                // Envoyer la liste des membres de la guilde nouvellement créée à tous les membres
                byte[] msgNewRoster = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
                byte[] msgPlayerJoined = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberJoined), WriteMmoString(invitedPlayer.Name));
                foreach (var member in guild.GetOnlineMembers())
                {
                    member.Conn.Send(msgNewRoster);
                    // Envoyer "le joueur a rejoint votre guilde" à tous les membres en ligne, sauf au joueur qui vient de rejoindre
                    if (member != invitedPlayer)
                    {
                        member.Conn.Send(msgPlayerJoined);
                    }
                }

                // Envoyer un message à tous les serveurs indiquant qu’un personnage avec un certain ID appartient à une guilde avec un certain ID et nom
                // Pour le serveur, les paramètres sont : ID du personnage, nom de la guilde, ID de la guilde, rang dans la guilde
                byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(invitedPlayer.CharId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes(invitedPlayer.GuildRank));
                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    serverConn.Send(msgToServers);
                }
            }

            // Si l’invitant n’a pas de groupe, nous devons en créer un
            // Si l’invitant est déjà dans un groupe, le nouveau membre doit le rejoindre (et vérifier que le groupe n’est pas complet)
            // Il faut également vérifier si l’invitant a rejoint un autre groupe entre-temps ou a changé de groupe ; dans ce cas, l’invitation ne doit pas être acceptée
            // Mais si l’invitant n’était pas le chef de groupe avant et l’est maintenant, nous devons tout de même traiter l’invitation
            // L’invitant peut aussi être null s’il s’est déconnecté… devons-nous accepter l’invitation dans ce cas ?
            if (accept && invite is PartyInvitation partyInvite)
            {
                // Si l’invitant s’est déconnecté, ne pas former de groupe
                if (inviter == null)
                {
                    byte responseByte = 4;
                    byte[] msgExpired = MergeByteArrays(ToBytes(RpcType.RpcAcceptInvite), responseByte); // L’octet 4 de la réponse signifie "le joueur s’est déconnecté"
                    senderConn.Send(msgExpired);
                    invitedPlayer.ClearPendingInvite();
                    return;
                }

                // Si l’invitation concernait un groupe existant
                if (partyInvite.partyId != "")
                {
                    // Si le groupe existe toujours et n’est pas complet, rejoindre ce groupe
                    var party = Server!.GameLogic.GetPartyById(partyInvite.partyId);
                    if (party != null)
                    {
                        if (!party.IsPartyFull())
                        {
                            party.AddMember(invitedPlayer); // Cela permet d’envoyer tous les messages nécessaires
                        }
                        // Si le groupe est complet, envoyer "le groupe est complet" au joueur invité
                        else
                        {
                            byte responseByte = 2;
                            byte[] msgPartyFull = MergeByteArrays(ToBytes(RpcType.RpcAcceptInvite), responseByte); // L’octet 2 de la réponse signifie "le groupe est complet"
                            invitedPlayer.Conn.Send(msgPartyFull);
                        }
                    }
                    // Si le groupe n’existe plus, mais que l’invitant n’a pas de groupe, créer un nouveau groupe
                    else if (inviter != null && inviter.PartyRef == null)
                    {
                        new Party(inviter, invitedPlayer); // Cela permet d’envoyer tous les messages nécessaires
                    }
                }
                // Si l’invitation concernait un groupe vide
                else
                {
                    // Mais si l’invitant est maintenant dans un groupe, vérifier qu’il en est le chef. S’il ne l’est pas, nous ne pouvons pas rejoindre.
                    if (inviter.PartyRef is Party party)
                    {
                        if (party.PartyLeaderId == inviter.CharId)
                        {
                            party.AddMember(invitedPlayer); // Cela permet d’envoyer tous les messages nécessaires
                        }
                        else
                        {
                            byte responseByte = 3;
                            byte[] msgExpired = MergeByteArrays(ToBytes(RpcType.RpcAcceptInvite), responseByte); // L’octet 3 de la réponse signifie "l’invitation a expiré"
                            senderConn.Send(msgExpired);
                        }
                    }
                    // Si l’invitant n’a toujours pas de groupe, créer le groupe
                    else
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm} Groupe créé");
                        new Party(inviter, invitedPlayer); // Cela permet d’envoyer tous les messages nécessaires
                    }
                }
            }

            invitedPlayer.ClearPendingInvite();
        }
    }
}