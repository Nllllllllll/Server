using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PersistenceServer
{
    public class MmoWsServer
    {        
        public readonly ActionsSyncher Processor;
        public readonly GameLogic GameLogic;
        public readonly Database Database;
        public readonly SettingsReader Settings;

        public delegate void MessageReceivedHandler(RpcType inRpcType, UserConnection conn, BinaryReader reader);
        public event MessageReceivedHandler? OnMessageReceived;
        private readonly IWebHost? host;

        public static MmoWsServer? Singleton; // pour utilisation depuis les HttpControllers

        public MmoWsServer(SettingsReader inSettings, Database inDatabase)
        {
            Singleton = this;

            Settings = inSettings;
            // Contient une liste de Players, Guilds, Parties, etc
            GameLogic = new();
            // Lancer un Thread qui va 'Tick' toutes les 8ms et traiter les Actions sur une File Concurrente
            Processor = new();
            _ = Processor.Tick();

            Database = inDatabase;

            // Créer une instance de chaque classe qui hérite de BaseRPC
            List<BaseRpc> rpcReaders = typeof(BaseRpc).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(BaseRpc))).Select(t => (BaseRpc)Activator.CreateInstance(t)!).ToList();
#if DEBUG
            // Afficher tous les readers trouvés
            var readerNames = rpcReaders.Select(rpcReader => rpcReader.GetType().ToString().Replace("PersistenceServer.", "")).ToArray();
            Console.WriteLine($"Lecteurs RPC trouvés: {rpcReaders.Count} [{string.Join(", ", readerNames)}]");
#endif
            // Vérifier que leur type rpc est défini - la vérification se fait uniquement en Debug       
            foreach (var reader in rpcReaders) Debug.Assert(reader.RpcType != RpcType.RpcUndef);

            // Abonner chaque classe à l'événement OnMessageReceived
            foreach (var reader in rpcReaders) reader.SubscribeToMessages(this);

            // Construire le serveur WS
            host = new WebHostBuilder()
            .UseKestrel(options =>
            {
                options.Listen(IPAddress.Any, inSettings.Port);
            })
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers().AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null; // empêche l'utilisation de la politique camel-case par défaut, qui changerait les noms de champs de majuscules à minuscules sans raison
                });
            })
            .Configure(app =>
            {
                app.UseWebSockets();
                app.UseRouting();

                // Middleware pour les WebSockets
                app.UseEndpoints(endpoints =>
                {
                    endpoints.Map("/mmo", async context =>
                    {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                            // La partie suivante est nécessaire pour les instances de serveur UE5. On a besoin de connaître leur IP pour rediriger les joueurs vers leur adresse.
                            // Par défaut, on essaie d'utiliser l'adresse IP distante, ce qui va fonctionner si le PS et les serveurs UE tournent sur des machines différentes et ne partagent pas un réseau local.
                            // S'ils tournent sur la même machine, on va retourner Settings.PersistenceServerIP aux clients.
                            // Voici un cas particulier : si le PS tourne sur Machine1 et que le serveur UE tourne sur Machine2 et qu'ils partagent un réseau local,
                            // alors on ne peut pas déterminer l'IP réelle du serveur UE qui vient de se connecter. On connaît seulement l'IP locale du serveur.
                            // Cette dernière configuration fonctionnera quand même si les clients se connectent aussi depuis le même réseau local.
                            // Cependant, si le développeur tente de se connecter à un serveur UE5 local depuis l'extérieur du réseau local, ça échouera.
                            // C'est un cas particulier lié aux tests à domicile et aux réseaux locaux.                           

                            IPAddress ip = context.Connection.RemoteIpAddress!;
                            Console.WriteLine($"Connexion depuis: {ip}");

                            // si l'IP provient d'un réseau local
                            if (ip.ToString().StartsWith("127.") || ip.ToString().StartsWith("192.168."))
                            {
                                // Pas sûr de la qualité du fonctionnement de ceci, c'est à vous de comprendre votre configuration réseau, développeurs...
                                if (context.Connection.LocalIpAddress!.ToString() == "127.0.0.1" || context.Connection.LocalIpAddress == GetLocalIPAddress())
                                {
                                    ip = IPAddress.Parse(Settings.PersistenceServerIP);
                                }
                            }
                            //// Uncomment to debug:
                            //if (context.Connection.RemoteIpAddress != null) Console.WriteLine($"Connection's remote ip: {context.Connection.RemoteIpAddress}");
                            //if (context.Connection.LocalIpAddress != null) Console.WriteLine($"Connection's local ip: {context.Connection.LocalIpAddress}");
                            var userConnection = new UserConnection(this, webSocket, ip);
                            await userConnection.HandleConnectionAsync();
                        }
                        else
                        {
                            Console.WriteLine("Rejet de connexion avec le code 400");
                            context.Response.StatusCode = 400;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"error\": \"400 Bad Request - Seules les connexions WebSocket sont acceptées sur ce point de terminaison\"}");
                        }
                    });
                });

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

                // Middleware pour gérer les réponses 404
                app.Use(async (context, next) =>
                {
                    await next();
                    if (context.Response.StatusCode == 404)
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"error\": \"404 Point de terminaison non trouvé\"}");
                    }
                });
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Supprime tous les fournisseurs de logs, y compris celui de la console.
                logging.AddFilter("Microsoft", LogLevel.Warning) // Logger uniquement les avertissements ou erreurs des bibliothèques Microsoft
                       .AddFilter("System", LogLevel.Warning); // Logger uniquement les avertissements ou erreurs des bibliothèques System
            })
            .Build();
        }

        public void Start()
        {
            host?.Start();
        }

        public async Task Stop()
        {
            if (host != null)
                await host.StopAsync();
        }

        public void InvokeOnMessageReceived(RpcType inRpcType, UserConnection conn, BinaryReader reader)
        {
            /// Ceci appellera ReadRpc() sur toutes les sous-classes de BaseRPC qui sont du bon RpcType, <see cref="BaseRPC"/>
            OnMessageReceived?.Invoke(inRpcType, conn, reader);
        }

        public void BroadcastAdminMessage(string msg)
        {
            byte[] msgBytes = BaseRpc.WriteMmoString(msg);
            BinaryReader reader = new(new MemoryStream(msgBytes));
            // puisqu'on l'envoie depuis la console et non depuis un véritable serveur ue5, on doit utiliser un petit "hack" en trouvant une connexion serveur ue5 aléatoire
            // si on ne la trouve pas, ça signifie qu'il n'y a pas de serveurs et donc pas de joueurs en ligne, et donc on saute la diffusion du message
            if (GameLogic.GetAllServerConnections().Length > 0)
                InvokeOnMessageReceived(RpcType.RpcAdminMessage, GameLogic.GetAllServerConnections()[0], reader);
            else
                Console.WriteLine("Aucun serveur ni joueur connecté auquel envoyer un message.");
        }

        public async Task<int> RequestGuilds()
        {
            var guilds = await Database.GetGuilds();
            GameLogic.AssignGuilds(guilds);
            return guilds.Count;
        }

        // Récupère l'adresse IP de cette machine sur le réseau local
        public static IPAddress GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("Aucune carte réseau avec une adresse IPv4 dans le système !");
        }
    }
}
